using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Api.Auth;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Constants;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// Round <c>teacher-scope-auth</c> D6.6 / [P1-4] — the endpoint-side role posture, which is what
/// turns "roles" into a scope. Discriminating: the pre-fix code had no resolver at all, and each
/// posture below (role-less ⇒ tenant-wide, teacher without a claim ⇒ EMPTY) is asserted directly
/// rather than inferred from a list result.
/// </summary>
[TestClass]
public class AssignmentScopeResolverTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClaimTeacherId = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid GradeId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid OwnAssignmentId = Guid.Parse("00000000-0000-0000-0000-0000000000f1");
    private static readonly Guid OtherTeacherId = Guid.Parse("00000000-0000-0000-0000-0000000000bb");

    private sealed class RecordingScopeProvider : ITeacherScopeProvider
    {
        public List<Guid> RequestedTeacherIds { get; } = [];
        public TeacherScope Result { get; set; } = TeacherScope.Empty;

        public Task<TeacherScope> GetScopeAsync(Guid teacherId, CancellationToken cancellationToken = default)
        {
            RequestedTeacherIds.Add(teacherId);
            return Task.FromResult(Result);
        }
    }

    private sealed class StubCurrentUser(Guid? teacherId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? TeacherId => teacherId;
        public TenantContext CurrentTenant => new(TenantId, "SchoolA", TenantType.School);
    }

    /// <summary>A principal carrying the given roles on <see cref="ClaimTypes.Role"/> — the type
    /// the OIDC/JwtBearer handlers' <c>RoleClaimType</c> pin produces.</summary>
    private static ClaimsPrincipal Principal(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "TestBearer"));

    private static Task<TeacherScope> ResolveAsync(ClaimsPrincipal principal, Guid? teacherId, RecordingScopeProvider provider) =>
        AssignmentScopeResolver.ResolveAsync(principal, new StubCurrentUser(teacherId), provider, CancellationToken.None);

    [TestMethod]
    public async Task NoRoleClaims_IsTenantWide_AndDoesNotConsultTheStudentsPort()
    {
        var provider = new RecordingScopeProvider();

        var scope = await ResolveAsync(Principal(), teacherId: ClaimTeacherId, provider);

        scope.IsUnrestricted.Should().BeTrue(
            "[P1-4] a role-less principal (CI / Integration / dev TestAuth) keeps today's tenant-wide read");
        provider.RequestedTeacherIds.Should().BeEmpty(
            "the posture is decided from the principal alone; no Students hop is needed");
    }

    [TestMethod]
    public async Task StaffRole_IsTenantWide()
    {
        var provider = new RecordingScopeProvider();

        var scope = await ResolveAsync(Principal(RealmRoleNames.Staff), teacherId: ClaimTeacherId, provider);

        scope.IsUnrestricted.Should().BeTrue();
        provider.RequestedTeacherIds.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AdminRoles_AreTenantWide()
    {
        var provider = new RecordingScopeProvider();

        (await ResolveAsync(Principal(RealmRoleNames.UserAdmin), teacherId: null, provider)).IsUnrestricted.Should().BeTrue();
        (await ResolveAsync(Principal(RealmRoleNames.PlatformAdmin), teacherId: null, provider)).IsUnrestricted.Should().BeTrue();
        provider.RequestedTeacherIds.Should().BeEmpty("admins need no taught set");
    }

    [TestMethod]
    public async Task StaffAndTeacher_IsTenantWide()
    {
        var provider = new RecordingScopeProvider();

        var scope = await ResolveAsync(
            Principal(RealmRoleNames.Teacher, RealmRoleNames.Staff), teacherId: ClaimTeacherId, provider);

        scope.IsUnrestricted.Should().BeTrue("the broader role wins — staff reads tenant-wide");
        provider.RequestedTeacherIds.Should().BeEmpty();
    }

    [TestMethod]
    public async Task TeacherWithTeacherIdClaim_IsScopedByTheStudentsPort()
    {
        var provider = new RecordingScopeProvider
        {
            Result = TeacherScope.ForTeacher(ClaimTeacherId, [new TeacherSubjectGrade(GradeId, null, null)]),
        };

        var scope = await ResolveAsync(Principal(RealmRoleNames.Teacher), teacherId: ClaimTeacherId, provider);

        scope.IsUnrestricted.Should().BeFalse();
        scope.TeacherId.Should().Be(ClaimTeacherId);
        scope.Taught.Should().ContainSingle().Which.GradeLevelId.Should().Be(GradeId);
        provider.RequestedTeacherIds.Should().Equal([ClaimTeacherId], "the id comes from the claim");
    }

    [TestMethod]
    public async Task TeacherWithoutTeacherIdClaim_IsEmpty_NeverTenantWide()
    {
        var provider = new RecordingScopeProvider
        {
            // Even a permissive provider answer must not be reachable without a claim.
            Result = TeacherScope.Unrestricted,
        };

        var scope = await ResolveAsync(Principal(RealmRoleNames.Teacher), teacherId: null, provider);

        scope.IsUnrestricted.Should().BeFalse("a teacher-only principal without a teacher_id is never tenant-wide");
        scope.IsEmpty.Should().BeTrue();
        scope.Allows(OwnAssignmentId, GradeId, null).Should().BeFalse(
            "an empty scope grants nothing — not even the caller's own creations");
        scope.Allows(OtherTeacherId, GradeId, null).Should().BeFalse();
        provider.RequestedTeacherIds.Should().BeEmpty(
            "there is no id to ask the Students port about, and the review-queue route's dev "
            + "teacherId fallback must never be used as one");
    }
}
