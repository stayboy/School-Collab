using FluentAssertions;
using SchoolCollab.Assignments.Admin.Helpers;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Tests.Unit;

[TestClass]
public class EnumHelperTests
{
    // --- AssignmentTypeDto ---

    [TestMethod]
    public void GetDescription_Digital_ReturnsOnline()
    {
        EnumHelper.GetDescription(AssignmentTypeDto.Digital).Should().Be("Online");
    }

    [TestMethod]
    public void GetDescription_SemiManual_ReturnsHybrid()
    {
        EnumHelper.GetDescription(AssignmentTypeDto.SemiManual).Should().Be("Hybrid");
    }

    [TestMethod]
    public void GetDescription_Manual_ReturnsOffline()
    {
        EnumHelper.GetDescription(AssignmentTypeDto.Manual).Should().Be("Offline");
    }

    // --- GradingFormatDto ---

    [TestMethod]
    public void GetDescription_TeacherGraded_ReturnsTeacherMarked()
    {
        EnumHelper.GetDescription(GradingFormatDto.TeacherGraded).Should().Be("Teacher Marked");
    }

    [TestMethod]
    public void GetDescription_AutoGraded_ReturnsAutoScored()
    {
        EnumHelper.GetDescription(GradingFormatDto.AutoGraded).Should().Be("Auto Scored");
    }

    [TestMethod]
    public void GetDescription_InstantGraded_ReturnsInstantFeedback()
    {
        EnumHelper.GetDescription(GradingFormatDto.InstantGraded).Should().Be("Instant Feedback");
    }

    // --- TargetAudienceTypeDto ---

    [TestMethod]
    public void GetDescription_AllStudents_ReturnsEveryone()
    {
        EnumHelper.GetDescription(TargetAudienceTypeDto.AllStudents).Should().Be("Everyone");
    }

    [TestMethod]
    public void GetDescription_SelectedGrades_ReturnsByGradeLevel()
    {
        EnumHelper.GetDescription(TargetAudienceTypeDto.SelectedGrades).Should().Be("By Grade Level");
    }

    [TestMethod]
    public void GetDescription_SelectedGroups_ReturnsByGroup()
    {
        EnumHelper.GetDescription(TargetAudienceTypeDto.SelectedGroups).Should().Be("By Group");
    }

    // --- Fallback: enum without [Description] attribute ---

    [TestMethod]
    public void GetDescription_NoDescriptionAttribute_ReturnsEnumName()
    {
        // AssignmentStatusDto has no [Description] attributes
        EnumHelper.GetDescription(AssignmentStatusDto.Draft).Should().Be("Draft");
        EnumHelper.GetDescription(AssignmentStatusDto.Published).Should().Be("Published");
        EnumHelper.GetDescription(AssignmentStatusDto.Closed).Should().Be("Closed");
    }

    // --- All values covered ---

    [TestMethod]
    public void GetDescription_AllAssignmentTypeValues_HaveDescriptions()
    {
        foreach (AssignmentTypeDto value in Enum.GetValues(typeof(AssignmentTypeDto)))
        {
            var result = EnumHelper.GetDescription(value);
            result.Should().NotBeNullOrEmpty(because: $"enum member {value} should have a description or fallback");
            result.Should().NotBe(value.ToString(), because: $"enum member {value} has a [Description] attribute and should not fall back to its name");
        }
    }

    [TestMethod]
    public void GetDescription_AllGradingFormatValues_HaveDescriptions()
    {
        foreach (GradingFormatDto value in Enum.GetValues(typeof(GradingFormatDto)))
        {
            var result = EnumHelper.GetDescription(value);
            result.Should().NotBeNullOrEmpty();
            result.Should().NotBe(value.ToString(), because: $"{value} has a [Description] attribute");
        }
    }

    [TestMethod]
    public void GetDescription_AllTargetAudienceTypeValues_HaveDescriptions()
    {
        foreach (TargetAudienceTypeDto value in Enum.GetValues(typeof(TargetAudienceTypeDto)))
        {
            var result = EnumHelper.GetDescription(value);
            result.Should().NotBeNullOrEmpty();
            result.Should().NotBe(value.ToString(), because: $"{value} has a [Description] attribute");
        }
    }
}