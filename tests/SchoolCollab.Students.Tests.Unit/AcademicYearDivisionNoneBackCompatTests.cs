using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Contracts.Events;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.AddMembership;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.CreateActivityGroup;
using SchoolCollab.Students.Core.CQRS.Enrollments.Commands.EnrollStudent;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.ActivatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.Domain.Specifications;
using SchoolCollab.Students.Core.DTOs;
using SchoolCollab.Students.Core.Services;
using SchoolCollab.Students.Core.Tenancy;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Back-compat regression (period-hierarchy-terms-semesters.md NFR-H4 / EC-H5):
/// an <c>AcademicYearDivision = None</c> tenant must behave byte-identically to
/// the shipped pre-hierarchy flow — one active academic-year period, year-level
/// grade enrollment, year-to-year promotion lifecycle, and the framework-agnostic
/// activity-group spans. The promotion service was removed (student transfer
/// handles grade movement), so back-compat asserts the period lifecycle +
/// year-level grade enrollment, not a promotion service.
/// </summary>
[TestClass]
public class AcademicYearDivisionNoneBackCompatTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static CreatePeriodHandler NewCreate(StudentsTestScope s) => new(
        s.Periods, s.Cache, s.Tenants,
        NullLogger<CreatePeriodHandler>.Instance);

    private static ActivatePeriodHandler NewActivate(StudentsTestScope s) => new(
        s.Periods, Mock.Of<IIntegrationEventPublisher>(), s.Cache,
        NullLogger<ActivatePeriodHandler>.Instance);

    private static AddMembershipHandler NewAdd(StudentsTestScope s) => new(
        s.ActivityGroups, s.Memberships, s.Students,
        Mock.Of<IStudentEnrollmentRepository>(), Mock.Of<IActivePeriodProvider>(),
        s.Periods, s.Cache, s.Tenants, NullLogger<AddMembershipHandler>.Instance);

    private static async Task<Guid> SeedStudentAsync(StudentsTestScope s, string number)
    {
        var student = Student.Create(number, "A", "B",
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-10)), Guid.NewGuid()).WithTenant(s.Tenants);
        s.Db.Students.Add(student);
        await s.Db.SaveChangesAsync();
        return student.Id;
    }

    // NFR-H4 / EC-H5: under None, activating a second year closes the first —
    // the single-active-year invariant is byte-identical to the shipped flow.
    [TestMethod]
    public async Task None_AcademicYearLifecycle_SingleActiveYear()
    {
        using var s = new StudentsTestScope("none-lifecycle-" + Guid.NewGuid());
        var create = NewCreate(s);
        var ay2025 = (await create.HandleAsync(new CreatePeriod("AY2025", D(2025, 9, 1), D(2026, 8, 31),
            Division: AcademicYearDivision.None))).YearId;
        var ay2026 = (await create.HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31),
            Division: AcademicYearDivision.None))).YearId;

        var activate = NewActivate(s);
        await activate.HandleAsync(new ActivatePeriod(ay2025));
        await activate.HandleAsync(new ActivatePeriod(ay2026));

        (await s.Db.Periods.SingleAsync(p => p.Id == ay2025)).Status.Should().Be(PeriodStatus.Completed);
        (await s.Db.Periods.SingleAsync(p => p.Id == ay2026)).Status.Should().Be(PeriodStatus.Active);
    }

    // NFR-H4 / EC-H5: year-level grade enrollment via the REAL ActivePeriodProvider
    // (not a stub) attaches to the active academic year under None.
    [TestMethod]
    public async Task None_GradeEnrollment_AttachesToActiveAcademicYear()
    {
        using var s = new StudentsTestScope("none-enroll-" + Guid.NewGuid());
        var create = NewCreate(s);
        var yearId = (await create.HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31),
            Division: AcademicYearDivision.None))).YearId;
        await NewActivate(s).HandleAsync(new ActivatePeriod(yearId));

        // Seed a GradeLevel so the handler resolves it locally (no coded-values HTTP).
        var gradeCodedValueId = Guid.Parse("22222222-2222-2222-2222-222222222223");
        var gradeLevel = GradeLevel.Create(gradeCodedValueId, level: 1, name: "Grade 1", displayOrder: 1);
        s.Db.GradeLevels.Add(gradeLevel);
        await s.Db.SaveChangesAsync();

        var publisher = new RecordingPublisher();
        var handler = new EnrollStudentHandler(
            new StudentEnrollmentRepository(s.Db),
            new ActivePeriodProvider(s.Db, s.Tenants, s.Cache),
            new InMemoryGradeLevelRepository(s.Db),
            new StubCodedValuesApiClient(),
            publisher,
            s.Cache,
            NullLogger<EnrollStudentHandler>.Instance,
            new StubFeatureFlagService(false),
            new StudentRepository(s.Db),
            new CompositeEnrollmentSpecification(new ILeafEnrollmentSpecification[]
            {
                new AgeRangeSpecification(),
                new GenderRestrictionSpecification(),
                new SingleActiveEnrollmentSpecification()
            }),
            s.Tenants,
            s.Db,
            new SystemActorAccessor("test:actor", "Test Actor"));

        var sid = await SeedStudentAsync(s, "N001");
        var id = await handler.HandleAsync(new EnrollStudent(sid, yearId, gradeCodedValueId, null, D(2026, 9, 15)));

        id.Should().NotBeEmpty();
        var row = await s.Db.StudentEnrollments.SingleAsync();
        row.PeriodId.Should().Be(yearId, "grade enrollment attaches to the active academic year under None");
        publisher.Enqueued.OfType<StudentEnrolled>().Should().ContainSingle();
    }

    // NFR-H4 / EC-H5: under None, Termly/Semester groups are rejected; WholeAcademicYear is allowed.
    [TestMethod]
    public async Task None_TermlyAndSemesterGroups_Rejected_WholeYearAllowed()
    {
        using var s = new StudentsTestScope("none-groups-" + Guid.NewGuid());
        var create = NewCreate(s);
        var yearId = (await create.HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31),
            Division: AcademicYearDivision.None))).YearId;
        await NewActivate(s).HandleAsync(new ActivatePeriod(yearId));
        var h = new CreateActivityGroupHandler(s.ActivityGroups, s.Cache, s.Tenants,
            new ActivePeriodProvider(s.Db, s.Tenants, s.Cache), NullLogger<CreateActivityGroupHandler>.Instance);

        await FluentActions.Awaiting(() => h.HandleAsync(new CreateActivityGroup("Term Club", Span: EnrollmentSpan.Termly)))
            .Should().ThrowAsync<EnrollmentSpanIncompatibleException>();
        await FluentActions.Awaiting(() => h.HandleAsync(new CreateActivityGroup("Sem Club", Span: EnrollmentSpan.Semester)))
            .Should().ThrowAsync<EnrollmentSpanIncompatibleException>();

        var id = await h.HandleAsync(new CreateActivityGroup("Year Club", Span: EnrollmentSpan.WholeAcademicYear));
        (await s.ActivityGroups.GetAsync(id))!.Span.Should().Be(EnrollmentSpan.WholeAcademicYear);
    }

    // NFR-H4 / EC-H5: a WholeAcademicYear membership under None attaches to the active year.
    [TestMethod]
    public async Task None_WholeAcademicYearMembership_AttachesToActiveYear()
    {
        using var s = new StudentsTestScope("none-membership-" + Guid.NewGuid());
        var create = NewCreate(s);
        var yearId = (await create.HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31),
            Division: AcademicYearDivision.None))).YearId;
        await NewActivate(s).HandleAsync(new ActivatePeriod(yearId));

        var group = ActivityGroup.Create("Year Club", span: EnrollmentSpan.WholeAcademicYear);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();
        var sid = await SeedStudentAsync(s, "N002");

        var id = await NewAdd(s).HandleAsync(new AddMembership(group.Id, sid));
        (await s.Memberships.GetAsync(id))!.PeriodId.Should().Be(yearId);
    }

    // ── Test doubles (mirror EnrollStudentHandlerTests) ─────────────────────

    private sealed class RecordingPublisher : IIntegrationEventPublisher
    {
        public List<object> Enqueued { get; } = new();
        public Task EnqueueAsync<T>(T message, CancellationToken ct = default) where T : class
        {
            Enqueued.Add(message);
            return Task.CompletedTask;
        }
        public Task EnqueueAsync<T>(T message, Guid? tenantStamp, CancellationToken ct = default) where T : class
            => EnqueueAsync(message, ct);
    }

    private sealed class StubFeatureFlagService(bool enabled) : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => enabled;
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
    }

    private sealed class StubCodedValuesApiClient : ICodedValuesApiClient
    {
        public Task<StreamCodedValueDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<StreamCodedValueDto?>(new StreamCodedValueDto(
                id, "GRSTREAMS_TEST", "Test Stream", null,
                null, "GRSTREAMS", false, 0,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                new[] { new StreamAttributeDto("gradeLevel", "22222222-2222-2222-2222-222222222223") }));
    }

    private sealed class InMemoryGradeLevelRepository(StudentsDbContext db) : IGradeLevelRepository
    {
        public async Task<GradeLevel?> GetAsync(Guid id, CancellationToken ct = default) =>
            await db.GradeLevels.FirstOrDefaultAsync(x => x.Id == id, ct);
        public Task<GradeLevel?> GetByCodedValueIdAsync(Guid codedValueId, CancellationToken ct = default) =>
            db.GradeLevels.FirstOrDefaultAsync(x => x.CodedValueId == codedValueId, ct);
        public async Task<GradeLevelDto[]> ListAsync(CancellationToken ct = default) =>
            await db.GradeLevels.AsNoTracking().OrderBy(x => x.Level)
                .Select(x => new GradeLevelDto(x.Id, x.CodedValueId, x.Level, x.Name, x.DisplayOrder, 0, 0, x.CreatedAt, x.UpdatedAt, x.MinAge, x.MaxAge, x.AllowedGenderCodedValueId))
                .ToArrayAsync(ct);
        public async Task AddAsync(GradeLevel gradeLevel, CancellationToken ct = default)
        {
            await db.GradeLevels.AddAsync(gradeLevel, ct);
            await db.SaveChangesAsync(ct);
        }
        public async Task<GradeLevel> AddOrReuseAsync(GradeLevel candidate, CancellationToken ct = default)
        {
            await AddAsync(candidate, ct);
            return candidate;
        }
        public async Task UpdateAsync(GradeLevel gradeLevel, CancellationToken ct = default)
        {
            db.GradeLevels.Update(gradeLevel);
            await db.SaveChangesAsync(ct);
        }
    }
}
