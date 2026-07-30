using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Admin.Services;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the new "Full Name (Gender, Age)" column shape and
/// wrap-friendly header templates added to the Students landing page. The
/// markup lives in <c>TestStudentColumnsGrid.razor</c> so the
/// <c>HeaderCellTitleTemplate</c> + <c>ChildContent</c> syntax only valid in
/// <c>.razor</c> files can be exercised here.
/// </summary>
[TestClass]
public class StudentLandingColumnsTests : BunitContext
{
    public StudentLandingColumnsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private static StudentDto MakeStudent(
        string first = "Jane",
        string last = "Doe",
        int? age = 12,
        string? gender = "Female",
        string? currentGradeName = "Grade 5",
        bool isDeleted = false) =>
        new(
            Id: Guid.NewGuid(),
            StudentNumber: "S0001",
            FirstName: first,
            LastName: last,
            DateOfBirth: null,
            GenderCodedValueId: null,
            IsDeleted: isDeleted,
            CreatedAt: default,
            UpdatedAt: default,
            Age: age,
            GenderName: gender,
            CurrentGrade: currentGradeName is null
                ? null
                : new GradeLevelDto(
                    Id: Guid.NewGuid(),
                    CodedValueId: Guid.NewGuid(),
                    Level: 5,
                    Name: currentGradeName,
                    DisplayOrder: 5,
                    SubjectCount: 0,
                    StudentCount: 0,
                    CreatedAt: default,
                    UpdatedAt: default));

    [TestMethod]
    public void FullName_Header_Uses_Wrappable_Template()
    {
        var cut = Render<TestStudentColumnsGrid>(p => p
            .Add(x => x.Items, new[] { MakeStudent() }));

        // The new header template renders an explicit <span class="grid-header-wrap">
        // instead of relying on the default title text — the CSS class is what
        // opts the cell back into word-break instead of ellipsis.
        cut.Markup.Should().Contain("grid-header-wrap");
        cut.Markup.Should().Contain("Full Name (Gender, Age)");
    }

    [TestMethod]
    public void CurrentGrade_Header_Uses_Wrappable_Template()
    {
        var cut = Render<TestStudentColumnsGrid>(p => p
            .Add(x => x.Items, new[] { MakeStudent() }));

        cut.Markup.Should().Contain("title=\"Current Grade\"");
    }

    [TestMethod]
    public void FullName_Column_Renders_Name_And_Demographics()
    {
        var cut = Render<TestStudentColumnsGrid>(p => p
            .Add(x => x.Items, new[] { MakeStudent(first: "Ada", last: "Lovelace", age: 14, gender: "Female") }));

        // The body splits the two halves into their own spans so the CSS
        // can stack them visually.
        cut.Markup.Should().Contain("student-full-name__name");
        cut.Markup.Should().Contain("Ada Lovelace");
        cut.Markup.Should().Contain("student-full-name__demographics");
        cut.Markup.Should().Contain("Female, 14");
    }

    [TestMethod]
    public void CurrentGrade_Column_Renders_Badge_With_GradeName()
    {
        var cut = Render<TestStudentColumnsGrid>(p => p
            .Add(x => x.Items, new[] { MakeStudent(currentGradeName: "Grade 7") }));

        cut.Markup.Should().Contain("Grade 7");
    }

    [TestMethod]
    public void CurrentGrade_Column_Falls_Back_To_EmDash_When_Null()
    {
        var cut = Render<TestStudentColumnsGrid>(p => p
            .Add(x => x.Items, new[] { MakeStudent(currentGradeName: null) }));

        // When no grade is enrolled, the cell shows "—" instead of an empty
        // badge so the row doesn't collapse.
        cut.Markup.Should().Contain("—");
    }

    [TestMethod]
    public void Actions_Menu_Button_Has_Accessibility_Attributes()
    {
        var cut = Render<TestStudentColumnsGrid>(p => p
            .Add(x => x.Items, new[] { MakeStudent() }));

        // The kebab trigger renders as a FluentButton with aria-haspopup and a
        // title/aria-label for screen readers.
        cut.Markup.Should().Contain("aria-haspopup=\"true\"");
        cut.Markup.Should().Contain("Student actions");
    }

    [TestMethod]
    public void Actions_Menu_Renders_View_Edit_Transfer_Delete_For_Active_Student()
    {
        var cut = Render<TestStudentColumnsGrid>(p => p
            .Add(x => x.Items, new[] { MakeStudent() }));

        // Open the kebab menu by clicking the trigger button.
        cut.Find("fluent-button[title='Student actions']").Click();

        // Active student: View, Edit, Transfer, Delete — no Recover.
        cut.Markup.Should().Contain("View");
        cut.Markup.Should().Contain("Edit");
        cut.Markup.Should().Contain("Transfer");
        cut.Markup.Should().Contain("Delete");
        cut.Markup.Should().NotContain("Recover");
    }

    [TestMethod]
    public void Actions_Menu_Renders_View_Edit_Recover_For_Deleted_Student()
    {
        var cut = Render<TestStudentColumnsGrid>(p => p
            .Add(x => x.Items, new[] { MakeStudent(isDeleted: true) }));

        // Open the kebab menu.
        cut.Find("fluent-button[title='Student actions']").Click();

        // Deleted student: View, Edit, Recover — no Transfer, no Delete action.
        cut.Markup.Should().Contain("View");
        cut.Markup.Should().Contain("Edit");
        cut.Markup.Should().Contain("Recover");
        cut.Markup.Should().NotContain("Transfer");
        cut.Markup.Should().NotContain("Delete");
    }
}
