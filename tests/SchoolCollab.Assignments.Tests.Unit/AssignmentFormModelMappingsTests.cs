using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Unit tests for the DTO → form-model projection on
/// <see cref="AssignmentEditFormModel"/>
/// (<see cref="AssignmentEditFormModel.LoadFrom"/> /
/// <see cref="AssignmentEditFormModel.From"/>) used by the assignment edit page.
/// Keeping the projection in a named, tested method (rather than inline
/// field-by-field assignments in the razor) makes the mapping easy to verify and
/// keeps it in lockstep with both types — see
/// documents/solution/dto-form-model-mapping.md.
/// </summary>
[TestClass]
public class AssignmentFormModelMappingsTests
{
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");

    private static AssignmentSummaryDto MakeAssignment(DateTimeOffset? dueDate = null, decimal? maxScore = null) => new(
        Id: Guid.NewGuid(),
        Title: "Test Assignment",
        Description: "A description",
        AssignmentType: AssignmentTypeDto.Digital,
        GradingFormat: GradingFormatDto.TeacherGraded,
        TargetAudienceType: TargetAudienceTypeDto.AllStudents,
        TopicId: TopicId,
        TopicName: "Mathematics",
        GradeLevelId: null,
        GradeName: null,
        Status: AssignmentStatusDto.Draft,
        DueDate: dueDate,
        MaxScore: maxScore,
        MandatoryReview: false,
        CreatedByTeacherId: TeacherId,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);

    [TestMethod]
    public void LoadFrom_MapsAllEditableFields()
    {
        var assignment = MakeAssignment(
            dueDate: new DateTimeOffset(2026, 8, 13, 10, 30, 0, TimeSpan.Zero),
            maxScore: 100m);
        var model = new AssignmentEditFormModel();

        model.LoadFrom(assignment);

        model.Title.Should().Be(assignment.Title);
        model.Description.Should().Be(assignment.Description);
        model.MaxScore.Should().Be(assignment.MaxScore);
        // DueDate converts DateTimeOffset? to DateTime? (the DTO's offset is dropped).
        model.DueDate.Should().Be(assignment.DueDate!.Value.DateTime);
        model.DueDate.Should().Be(new DateTime(2026, 8, 13, 10, 30, 0));
    }

    [TestMethod]
    public void LoadFrom_NullDueDate_StaysNull()
    {
        var assignment = MakeAssignment(dueDate: null);

        var model = new AssignmentEditFormModel { DueDate = new DateTime(2020, 1, 1) };
        model.LoadFrom(assignment);

        model.DueDate.Should().BeNull("a null DTO DueDate maps to a null form-model DueDate");
    }

    [TestMethod]
    public void From_ReturnsNewPopulatedModel()
    {
        var assignment = MakeAssignment(dueDate: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), maxScore: 50m);

        var model = AssignmentEditFormModel.From(assignment);

        model.Should().NotBeNull();
        model.Title.Should().Be(assignment.Title);
        model.Description.Should().Be(assignment.Description);
        model.DueDate.Should().Be(assignment.DueDate!.Value.DateTime);
        model.MaxScore.Should().Be(assignment.MaxScore);
    }

    [TestMethod]
    public void LoadFrom_OverwritesPriorValues()
    {
        var assignment = MakeAssignment(dueDate: new DateTimeOffset(2026, 5, 5, 8, 0, 0, TimeSpan.Zero), maxScore: 75m);
        var model = new AssignmentEditFormModel
        {
            Title = "Old",
            Description = "Old desc",
            DueDate = new DateTime(2000, 1, 1),
            MaxScore = 10m,
        };

        model.LoadFrom(assignment);

        model.Title.Should().Be(assignment.Title);
        model.Description.Should().Be(assignment.Description);
        model.DueDate.Should().Be(assignment.DueDate!.Value.DateTime);
        model.MaxScore.Should().Be(assignment.MaxScore);
    }
}