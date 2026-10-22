using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The ONE read-only constraints dialog (redesign spec §3.6/§3.8): a stored id renders with the name the
/// editor uses (the shared lookup vocabulary), and an empty set reads as "this is the default" rather
/// than as a broken panel.
/// </summary>
[TestClass]
public class TemplateConstraintsDialogTests : BunitContext
{
    public TemplateConstraintsDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    [TestMethod]
    public void ListsEachConstraint_WithItsResolvedName()
    {
        TemplateTestServices.Register<TemplateConstraintsDialog>(this);

        var cut = Render<TemplateConstraintsDialog>(p => p
            .Add(x => x.TemplateLabel, "Grade 3 reminder")
            .Add(x => x.Constraints, new List<TemplateConstraintDto>
            {
                new(Guid.NewGuid(), TemplateConstraintDimension.GradeLevel, TemplateApiHandler.GradeB),
                new(Guid.NewGuid(), TemplateConstraintDimension.AssignmentType, "Digital"),
            }));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Grade level");
            cut.Markup.Should().Contain("Grade 3");
            cut.Markup.Should().Contain("Assignment type");
            cut.Markup.Should().Contain("Digital");
            cut.FindAll(".constraint-list__item").Should().HaveCount(2);
        });
    }

    [TestMethod]
    public void AnEmptySet_SaysTheTemplateIsTheDefault()
    {
        TemplateTestServices.Register<TemplateConstraintsDialog>(this);

        var cut = Render<TemplateConstraintsDialog>(p => p
            .Add(x => x.TemplateLabel, "Publish email")
            .Add(x => x.Constraints, Array.Empty<TemplateConstraintDto>()));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No constraints"));
    }
}