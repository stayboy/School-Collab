using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.GradeNotificationPolicies.Commands.UpsertGradeNotificationPolicy;
using SchoolCollab.Students.Core.CQRS.GradeNotificationPolicies.Queries.GetGradeNotificationPolicy;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

[TestClass]
public class GradeNotificationPolicyHandlerTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static async Task<Guid> SeedGradeAsync(StudentsTestScope s, string name = "Grade 1")
    {
        var gl = GradeLevel.Create(Guid.NewGuid(), 1, name, 1);
        s.Db.GradeLevels.Add(gl);
        await s.Db.SaveChangesAsync();
        return gl.Id;
    }

    private static void AsTenant(StudentsTestScope s, Guid tenantId) =>
        ((TenantProvider)s.Tenants).SetTenant(new TenantContext(tenantId, tenantId.ToString(), TenantType.School));

    private static GetGradeNotificationPolicyHandler NewGet(StudentsTestScope s) =>
        new(s.Db);

    private static UpsertGradeNotificationPolicyHandler NewUpsert(StudentsTestScope s) =>
        new(s.Db, s.Tenants);

    [TestMethod]
    public async Task Get_returns_null_when_no_override()
    {
        using var s = new StudentsTestScope("gnp-get-null");
        var gradeId = await SeedGradeAsync(s);

        var result = await NewGet(s).HandleAsync(new GetGradeNotificationPolicy(gradeId));
        result.Should().BeNull();
    }

    [TestMethod]
    public async Task Upsert_creates_override_and_get_returns_it()
    {
        using var s = new StudentsTestScope("gnp-upsert-create");
        var gradeId = await SeedGradeAsync(s);

        await NewUpsert(s).HandleAsync(new UpsertGradeNotificationPolicy(
            gradeId, [NotificationChannel.SMS], [NotificationChannel.Email],
            MaxNotifications: 5, null, null, null, null, null));

        var result = await NewGet(s).HandleAsync(new GetGradeNotificationPolicy(gradeId));
        result.Should().NotBeNull();
        result!.PreferredChannelOrder.Should().Equal(NotificationChannel.SMS);
        result.BlockedChannels.Should().Equal(NotificationChannel.Email);
        result.MaxNotifications.Should().Be(5);
    }

    [TestMethod]
    public async Task Upsert_updates_existing_and_null_fields_clear()
    {
        using var s = new StudentsTestScope("gnp-upsert-update");
        var gradeId = await SeedGradeAsync(s);
        var upsert = NewUpsert(s);

        await upsert.HandleAsync(new UpsertGradeNotificationPolicy(
            gradeId, [NotificationChannel.Email], null, MaxNotifications: 10, null, null, null, null, null));
        await upsert.HandleAsync(new UpsertGradeNotificationPolicy(
            gradeId, [NotificationChannel.SMS], null, MaxNotifications: null, null, null, null, null, null));

        var result = await NewGet(s).HandleAsync(new GetGradeNotificationPolicy(gradeId));
        result!.PreferredChannelOrder.Should().Equal(NotificationChannel.SMS);
        result.MaxNotifications.Should().BeNull(); // cleared → inherit
        (await s.Db.GradeNotificationPolicies.CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task Upsert_rejects_nonexistent_grade()
    {
        using var s = new StudentsTestScope("gnp-upsert-nograde");
        var act = () => NewUpsert(s).HandleAsync(new UpsertGradeNotificationPolicy(
            Guid.NewGuid(), null, null, null, null, null, null, null, null));
        await act.Should().ThrowAsync<GradeLevelNotFoundException>();
    }

    [TestMethod]
    public async Task Tenant_isolation_policy_not_visible_to_other_tenant()
    {
        using var s = new StudentsTestScope("gnp-isolation");
        var gradeId = await SeedGradeAsync(s);

        await NewUpsert(s).HandleAsync(new UpsertGradeNotificationPolicy(
            gradeId, [NotificationChannel.SMS], null, null, null, null, null, null, null));

        AsTenant(s, TenantB);
        var result = await NewGet(s).HandleAsync(new GetGradeNotificationPolicy(gradeId));
        result.Should().BeNull();
    }
}
