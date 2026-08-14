using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Integration;

/// <summary>
/// Integration tests for the atomic <c>PUT /students/{id}/with-linked-data</c> endpoint
/// (Unit of Work) — the edit counterpart of <c>POST /students/with-linked-data</c>.
/// Proves the whole update — profile + guardian reconcile (link/unlink/update) + contact
/// reconcile (add/update/delete) — is all-or-nothing, and that optimistic concurrency
/// (stale <c>ExpectedRowVersion</c>, or a guardian/contact added/removed by another user
/// since the client loaded) surfaces as 409 Conflict.
///
/// Runs against the real Students API + Postgres via <see cref="ApiFactory"/>.
/// </summary>
[TestClass]
[DoNotParallelize]
public class UpdateStudentWithLinkedDataEndpointTests
{
    private static ApiFactory _factory = default!;
    private static HttpClient _client = default!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        _factory = new ApiFactory();
        await _factory.InitializeAsync();
        _client = _factory.CreateClient();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE contact_subscriptions, contacts, student_enrollments, " +
            "student_guardians, guardian_name_history, students, guardians, periods, " +
            "grade_levels CASCADE;");
    }

    [TestMethod]
    public async Task ValidUpdate_ReconcilesProfileGuardiansAndContacts()
    {
        var tenant = ApiFactory.TestTenantA;
        var studentId = await SeedStudentAsync(tenant, "Jane", "Doe");
        var existingGuardian = await SeedGuardianAsync(tenant, "Alice", "Existing");
        await LinkGuardianAsync(tenant, studentId, existingGuardian);
        var contactId = await AddContactAsync(tenant, studentId, "jane@example.com");
        var rowVersion = await GetStudentRowVersionAsync(tenant, studentId);

        // Update profile + drop the existing guardian + add a new guardian + update the
        // existing contact + add a new contact — all in one atomic request.
        var response = await PutUpdateAsync(tenant, studentId, new
        {
            FirstName = "Jane",
            LastName = "Smith",
            DateOfBirth = new DateOnly(2015, 1, 1),
            GenderCodedValueId = Guid.NewGuid(),
            ExpectedRowVersion = rowVersion,
            Guardians = new[]
            {
                new { ExistingGuardianId = (Guid?)null, Role = GuardianRole.CC, FirstName = "Sam", LastName = "NewParent", RelationshipCodedValueId = (Guid?)null }
            },
            Contacts = new[]
            {
                new { Id = (Guid?)contactId, Channel = ContactChannel.Email, Value = "jane.smith@example.com", Label = "Work", DisplayOrder = 0 },
                new { Id = (Guid?)null, Channel = ContactChannel.SMS, Value = "555-1234", Label = "Home", DisplayOrder = 1 }
            },
            LoadedGuardianIds = new[] { existingGuardian },
            LoadedContactIds = new[] { contactId }
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        await accessor.RunWithExplicitTenantAsync(tenant, async _ =>
        {
            var student = await db.Students.SingleAsync(s => s.Id == studentId);
            student.LastName.Should().Be("Smith", "the profile must be updated");

            // The existing guardian was dropped; the new guardian is linked.
            (await db.StudentGuardians.CountAsync(l => l.StudentId == studentId && l.GuardianId == existingGuardian))
                .Should().Be(0, "the dropped guardian link must be removed");
            var newGuardianId = (await db.StudentGuardians
                .Where(l => l.StudentId == studentId)
                .Select(l => l.GuardianId)
                .ToArrayAsync()).Single();
            newGuardianId.Should().NotBe(existingGuardian);
            (await db.Guardians.SingleAsync(g => g.Id == newGuardianId)).FirstName.Should().Be("Sam");

            // The existing contact was updated; a new contact was added.
            var contacts = await db.Contacts
                .Where(c => c.OwnerType == ContactOwnerType.Student && c.OwnerId == studentId && !c.IsDeleted)
                .OrderBy(c => c.DisplayOrder)
                .ToArrayAsync();
            contacts.Should().HaveCount(2);
            contacts[0].Value.Should().Be("jane.smith@example.com", "the existing contact must be updated");
            contacts[1].Value.Should().Be("555-1234", "the new contact must be added");
            return true;
        });
    }

    [TestMethod]
    public async Task StaleRowVersion_ReturnsConflict()
    {
        var tenant = ApiFactory.TestTenantA;
        var studentId = await SeedStudentAsync(tenant, "Jane", "Doe");
        var rowVersion = await GetStudentRowVersionAsync(tenant, studentId);

        // Another user updates the student after the client loaded it → the row version bumps.
        await BumpStudentRowVersionAsync(tenant, studentId);

        var response = await PutUpdateAsync(tenant, studentId, new
        {
            FirstName = "Jane",
            LastName = "Doe",
            DateOfBirth = new DateOnly(2015, 1, 1),
            GenderCodedValueId = Guid.NewGuid(),
            ExpectedRowVersion = rowVersion,
            LoadedGuardianIds = Array.Empty<Guid>(),
            LoadedContactIds = Array.Empty<Guid>()
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a stale ExpectedRowVersion must be rejected with 409");
    }

    [TestMethod]
    public async Task ConcurrentGuardianAddition_ReturnsConflict()
    {
        var tenant = ApiFactory.TestTenantA;
        var studentId = await SeedStudentAsync(tenant, "Jane", "Doe");
        var guardian = await SeedGuardianAsync(tenant, "Alice", "Existing");
        await LinkGuardianAsync(tenant, studentId, guardian);
        var rowVersion = await GetStudentRowVersionAsync(tenant, studentId);

        // The client loaded the student BEFORE the guardian was linked, so its
        // LoadedGuardianIds is empty — the current link is a concurrent addition.
        var response = await PutUpdateAsync(tenant, studentId, new
        {
            FirstName = "Jane",
            LastName = "Doe",
            DateOfBirth = new DateOnly(2015, 1, 1),
            GenderCodedValueId = Guid.NewGuid(),
            ExpectedRowVersion = rowVersion,
            LoadedGuardianIds = Array.Empty<Guid>(),
            LoadedContactIds = Array.Empty<Guid>()
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a guardian linked by another user since the client loaded must be rejected with 409");
    }

    [TestMethod]
    public async Task ConcurrentGuardianRemoval_ReturnsConflict()
    {
        // Another user UNLINKED a guardian the client saw (loaded set has it, current
        // does not) => 409, not a silent re-link. Exercises the loaded.Except(current)
        // half of the subset check.
        var tenant = ApiFactory.TestTenantA;
        var studentId = await SeedStudentAsync(tenant, "Jane", "Doe");
        var guardian = await SeedGuardianAsync(tenant, "Alice", "Existing");
        await LinkGuardianAsync(tenant, studentId, guardian);
        var rowVersion = await GetStudentRowVersionAsync(tenant, studentId);

        // Another user unlinks the guardian after the client loaded it.
        await UnlinkGuardianDbAsync(tenant, studentId, guardian);

        var response = await PutUpdateAsync(tenant, studentId, new
        {
            FirstName = "Jane",
            LastName = "Doe",
            DateOfBirth = new DateOnly(2015, 1, 1),
            GenderCodedValueId = Guid.NewGuid(),
            ExpectedRowVersion = rowVersion,
            // The client still believes the guardian is linked (it's in the loaded set).
            Guardians = new[]
            {
                new { ExistingGuardianId = (Guid?)guardian, Role = GuardianRole.Primary, RelationshipCodedValueId = (Guid?)null }
            },
            LoadedGuardianIds = new[] { guardian },
            LoadedContactIds = Array.Empty<Guid>()
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a guardian unlinked by another user since the client loaded must be rejected with 409");
    }

    [TestMethod]
    public async Task ConcurrentContactAddition_ReturnsConflict()
    {
        // Another user ADDED a contact the client didn't see (current set has it,
        // loaded does not) => 409, not a blind delete during reconciliation.
        var tenant = ApiFactory.TestTenantA;
        var studentId = await SeedStudentAsync(tenant, "Jane", "Doe");
        var rowVersion = await GetStudentRowVersionAsync(tenant, studentId);

        // Another user adds a contact after the client loaded (the client's loaded set is empty).
        await AddContactAsync(tenant, studentId, "other@example.com");

        var response = await PutUpdateAsync(tenant, studentId, new
        {
            FirstName = "Jane",
            LastName = "Doe",
            DateOfBirth = new DateOnly(2015, 1, 1),
            GenderCodedValueId = Guid.NewGuid(),
            ExpectedRowVersion = rowVersion,
            LoadedGuardianIds = Array.Empty<Guid>(),
            LoadedContactIds = Array.Empty<Guid>()
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a contact added by another user since the client loaded must be rejected with 409");
    }

    // ── seed helpers ──────────────────────────────────────────────────────────

    private async Task<Guid> SeedStudentAsync(Guid tenantId, string firstName, string lastName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        return await accessor.RunWithExplicitTenantAsync(tenantId, async _ =>
        {
            var student = Student.Create(
                    $"STU{Guid.NewGuid():N}"[..12], firstName, lastName,
                    new DateOnly(2015, 1, 1), Guid.NewGuid())
                .WithTenant(tenantId);
            db.Students.Add(student);
            await db.SaveChangesAsync();
            return student.Id;
        });
    }

    private async Task<Guid> SeedGuardianAsync(Guid tenantId, string firstName, string lastName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        return await accessor.RunWithExplicitTenantAsync(tenantId, async _ =>
        {
            var guardian = Guardian.Create(null, firstName, lastName, null, null, null)
                .WithTenant(tenantId);
            guardian.AddInitialNameHistory();
            db.Guardians.Add(guardian);
            db.GuardianNameHistories.Add(guardian.NameHistory[0]);
            await db.SaveChangesAsync();
            return guardian.Id;
        });
    }

    private async Task LinkGuardianAsync(Guid tenantId, Guid studentId, Guid guardianId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        await accessor.RunWithExplicitTenantAsync(tenantId, async _ =>
        {
            var link = StudentGuardian.Create(studentId, guardianId, GuardianRole.Primary, null, false, null)
                .WithTenant(tenantId);
            db.StudentGuardians.Add(link);
            await db.SaveChangesAsync();
            return true;
        });
    }

    /// <summary>Simulates another user unlinking a guardian (concurrent removal).</summary>
    private async Task UnlinkGuardianDbAsync(Guid tenantId, Guid studentId, Guid guardianId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        await accessor.RunWithExplicitTenantAsync(tenantId, async _ =>
        {
            var link = await db.StudentGuardians
                .SingleAsync(l => l.StudentId == studentId && l.GuardianId == guardianId);
            db.StudentGuardians.Remove(link);
            await db.SaveChangesAsync();
            return true;
        });
    }

    private async Task<Guid> AddContactAsync(Guid tenantId, Guid studentId, string value)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        return await accessor.RunWithExplicitTenantAsync(tenantId, async _ =>
        {
            var contact = Contact.Create(ContactOwnerType.Student, studentId, ContactChannel.Email, value, null, null, 0)
                .WithTenant(tenantId);
            db.Contacts.Add(contact);
            await db.SaveChangesAsync();
            return contact.Id;
        });
    }

    private async Task<uint> GetStudentRowVersionAsync(Guid tenantId, Guid studentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        return await accessor.RunWithExplicitTenantAsync(tenantId, async _ =>
        {
            var student = await db.Students.SingleAsync(s => s.Id == studentId);
            return student.RowVersion;
        });
    }

    private async Task BumpStudentRowVersionAsync(Guid tenantId, Guid studentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        await accessor.RunWithExplicitTenantAsync(tenantId, async _ =>
        {
            var student = await db.Students.SingleAsync(s => s.Id == studentId);
            student.Update(student.FirstName, student.LastName, student.DateOfBirth, student.GenderCodedValueId, student.TitleCodedValueId);
            await db.SaveChangesAsync();
            return true;
        });
    }

    private Task<HttpResponseMessage> PutUpdateAsync(Guid tenantId, Guid studentId, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/students/{studentId}/with-linked-data")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("x-tenant-id", tenantId.ToString());
        return _client.SendAsync(request);
    }
}
