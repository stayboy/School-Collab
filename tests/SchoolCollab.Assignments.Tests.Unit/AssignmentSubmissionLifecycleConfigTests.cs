using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Data.Outbox;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Phase 5 entity-config assertions (spec §4.6–§4.13 / §5): the submission-lifecycle
/// entities map to the expected tables, unique indexes, row-version columns and
/// defaults. The model⇄snapshot sync is covered separately by
/// <see cref="MigrationGuardTests"/>.
/// </summary>
[TestClass]
public class AssignmentSubmissionLifecycleConfigTests
{
    private static AssignmentsDbContext BuildContext()
    {
        OutboxMapping.SetFlagsFor<AssignmentsDbContext>(
            OutboxConfigurationFlags.FromConfiguration(b => b.UsePartialIndexOnOccurredAt()));

        return new AssignmentsDbContext(
            new DbContextOptionsBuilder<AssignmentsDbContext>()
                .UseNpgsql("Host=localhost;Database=guard")
                .UseSnakeCaseNamingConvention()
                .Options,
            new DesignTimeTenantProvider());
    }

    [TestMethod]
    public void AssignmentRecipient_MapsPerContactUniqueIndex()
    {
        using var context = BuildContext();
        var entity = context.Model.FindEntityType(typeof(AssignmentRecipient));
        entity.Should().NotBeNull();
        entity!.GetTableName().Should().Be("assignment_recipients");

        entity.GetIndexes().Should().Contain(i =>
            i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(AssignmentRecipient.TenantId), nameof(AssignmentRecipient.AssignmentId), nameof(AssignmentRecipient.ContactId) }));

        entity.FindProperty(nameof(AssignmentRecipient.ContactId))!.IsNullable.Should().BeFalse();
        entity.FindProperty(nameof(AssignmentRecipient.Role))!.IsNullable.Should().BeTrue();
        entity.FindProperty(nameof(AssignmentRecipient.NotifyOnBroadcast))!
            .GetDefaultValue().Should().Be(true);
    }

    [TestMethod]
    public void GuardianSubmissionGate_HasUniqueIndexAndRowVersion()
    {
        using var context = BuildContext();
        var entity = context.Model.FindEntityType(typeof(GuardianSubmissionGate));
        entity.Should().NotBeNull();
        entity!.GetTableName().Should().Be("guardian_submission_gates");

        entity.GetIndexes().Should().Contain(i =>
            i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(GuardianSubmissionGate.TenantId), nameof(GuardianSubmissionGate.AssignmentId), nameof(GuardianSubmissionGate.StudentId) }));
        entity.FindProperty(nameof(GuardianSubmissionGate.RowVersion))!
            .GetColumnName().Should().Be("xmin");
    }

    [TestMethod]
    public void AssignmentSubmission_HasUniqueIndexRowVersionAndDefaults()
    {
        using var context = BuildContext();
        var entity = context.Model.FindEntityType(typeof(AssignmentSubmission));
        entity.Should().NotBeNull();
        entity!.GetTableName().Should().Be("assignment_submissions");

        entity.GetIndexes().Should().Contain(i =>
            i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(AssignmentSubmission.TenantId), nameof(AssignmentSubmission.AssignmentId), nameof(AssignmentSubmission.StudentId) }));
        entity.FindProperty(nameof(AssignmentSubmission.RowVersion))!
            .GetColumnName().Should().Be("xmin");
        entity.FindProperty(nameof(AssignmentSubmission.CurrentSource))!
            .GetDefaultValue().Should().Be(SubmissionSource.Student);
        entity.FindProperty(nameof(AssignmentSubmission.ReviewState))!
            .GetDefaultValue().Should().Be(ReviewState.Pending);
    }

    [TestMethod]
    public void AssignmentSubmissionVersion_HasUniqueIndexAndRowVersion()
    {
        using var context = BuildContext();
        var entity = context.Model.FindEntityType(typeof(AssignmentSubmissionVersion));
        entity.Should().NotBeNull();
        entity!.GetTableName().Should().Be("assignment_submission_versions");

        entity.GetIndexes().Should().Contain(i =>
            i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(AssignmentSubmissionVersion.TenantId), nameof(AssignmentSubmissionVersion.SubmissionId), nameof(AssignmentSubmissionVersion.VersionNumber) }));
        entity.FindProperty(nameof(AssignmentSubmissionVersion.RowVersion))!
            .GetColumnName().Should().Be("xmin");
    }

    [TestMethod]
    public void SubmissionReview_HasUniqueIndexAndRowVersion()
    {
        using var context = BuildContext();
        var entity = context.Model.FindEntityType(typeof(SubmissionReview));
        entity.Should().NotBeNull();
        entity!.GetTableName().Should().Be("submission_reviews");

        entity.GetIndexes().Should().Contain(i =>
            i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(SubmissionReview.TenantId), nameof(SubmissionReview.SubmissionId) }));
        entity.FindProperty(nameof(SubmissionReview.RowVersion))!
            .GetColumnName().Should().Be("xmin");
    }

    [TestMethod]
    public void Assignment_HasMandatoryReviewColumnDefaultingToTrue()
    {
        using var context = BuildContext();
        var entity = context.Model.FindEntityType(typeof(Assignment));
        var property = entity!.FindProperty(nameof(Assignment.MandatoryReview));
        property.Should().NotBeNull();
        property!.IsNullable.Should().BeFalse();
        property.GetDefaultValue().Should().Be(true);
    }
}
