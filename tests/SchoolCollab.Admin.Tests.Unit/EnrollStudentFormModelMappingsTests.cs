using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Unit tests for the DTO → form-model projection on
/// <see cref="EnrollStudentFormModel"/> (<see cref="EnrollStudentFormModel.LoadFrom"/> /
/// <see cref="EnrollStudentFormModel.From"/>) and the model → request projection
/// (<see cref="EnrollStudentFormModel.ToEnrollRequest"/>) used by the new-enrollment
/// dialog. Keeping the projections in named, tested methods (rather than inline
/// field-by-field assignments in the razor) makes the mapping easy to verify and keeps
/// it in lockstep with the wire contract — documents/solution/dto-form-model-mapping.md.
/// </summary>
[TestClass]
public class EnrollStudentFormModelMappingsTests
{
    private static GradeLevelDto MakeGrade() => new(
        Id: Guid.NewGuid(),
        CodedValueId: Guid.NewGuid(),
        Level: 5,
        Name: "Grade 5",
        DisplayOrder: 3,
        TopicCount: 0,
        StudentCount: 0,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow,
        MinAge: 10,
        MaxAge: 12,
        AllowedGenderCodedValueId: Guid.NewGuid());

    [TestMethod]
    public void LoadFrom_SuggestedGrade_SetsGradeCodedValueId()
    {
        var grade = MakeGrade();
        var model = new EnrollStudentFormModel();

        model.LoadFrom(grade);

        // The dialog submits the picked CodedValueId; the CodedValueId ->
        // GradeLevelId join happens server-side (EnrollStudentHandler).
        model.GradeCodedValueId.Should().Be(grade.CodedValueId);
    }

    [TestMethod]
    public void LoadFrom_NullSuggestion_IsNoOp()
    {
        var model = new EnrollStudentFormModel();

        model.LoadFrom(null);

        model.GradeCodedValueId.Should().BeNull();
    }

    [TestMethod]
    public void From_ReturnsNewModelWithSuggestionApplied()
    {
        var grade = MakeGrade();

        var model = EnrollStudentFormModel.From(grade);

        model.Should().NotBeNull();
        model.GradeCodedValueId.Should().Be(grade.CodedValueId);
    }

    [TestMethod]
    public void NewModel_HasDefaultsForCommonCase()
    {
        var model = new EnrollStudentFormModel();

        // Enrolled-on defaults to today so the common case needs no edit.
        model.EnrolledOn.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
        model.StreamCodedValueId.Should().BeNull("a stream is optional");
    }

    [TestMethod]
    public void ToEnrollRequest_ProjectsAllFields()
    {
        var studentId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var gradeCodedValueId = Guid.NewGuid();
        var streamId = Guid.NewGuid();
        var enrolledOn = new DateOnly(2026, 9, 1);
        var model = new EnrollStudentFormModel
        {
            GradeCodedValueId = gradeCodedValueId,
            StreamCodedValueId = streamId,
            EnrolledOn = enrolledOn,
        };

        var request = model.ToEnrollRequest(studentId, periodId);

        request.StudentId.Should().Be(studentId);
        request.PeriodId.Should().Be(periodId);
        request.GradeCodedValueId.Should().Be(gradeCodedValueId,
            "the request carries the GRADE coded value id; the GradeLevelId join is server-side");
        request.StreamCodedValueId.Should().Be(streamId);
        request.EnrolledOn.Should().Be(enrolledOn);
    }

    [TestMethod]
    public void ToEnrollRequest_AllowsNullOptionalFields()
    {
        // GradeCodedValueId is required (the dialog guards it before submit);
        // stream and enrolled-on are optional and project as null.
        var model = new EnrollStudentFormModel
        {
            GradeCodedValueId = Guid.NewGuid(),
            StreamCodedValueId = null,
            EnrolledOn = null,
        };

        var request = model.ToEnrollRequest(Guid.NewGuid(), Guid.NewGuid());

        request.StreamCodedValueId.Should().BeNull();
        request.EnrolledOn.Should().BeNull();
    }
}
