using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The add-constraint dialog''s state (redesign spec §3.6), at its pure seam: picks accumulate once,
/// values the draft already carries are not offered again, and availability shrinks as picks are made.
/// </summary>
[TestClass]
public class ConstraintEditModelTests
{
    private static ConstraintEditModel Model(params string[] existing) => new()
    {
        Dimension = TemplateConstraintDimension.GradeLevel,
        Options =
        [
            new ConstraintOption("grade-1", "Grade 1"),
            new ConstraintOption("grade-2", "Grade 2"),
            new ConstraintOption("grade-3", "Grade 3"),
        ],
        ExistingValues = existing,
        TemplateLabel = "Publish · Email",
    };

    [TestMethod]
    public void Append_AddsOnce_AndIgnoresDuplicates()
    {
        var model = Model();
        var grade = model.Options[0];

        model.Append(grade);
        model.Append(grade);

        model.Picked.Should().ContainSingle().Which.Should().Be(grade);
    }

    [TestMethod]
    public void Append_IgnoresValuesTheDraftAlreadyCarries()
    {
        var model = Model("grade-1");

        model.Append(model.Options[0]);

        model.Picked.Should().BeEmpty();
    }

    [TestMethod]
    public void Available_ExcludesPickedAndExistingValues()
    {
        var model = Model("grade-1");

        model.Available.Select(o => o.Value).Should().BeEquivalentTo(["grade-2", "grade-3"]);

        model.Append(model.Options[1]);

        model.Available.Select(o => o.Value).Should().BeEquivalentTo(["grade-3"]);
    }

    [TestMethod]
    public void Remove_MakesTheValueAvailableAgain()
    {
        var model = Model();
        model.Append(model.Options[0]);

        model.Remove(model.Options[0]);

        model.Picked.Should().BeEmpty();
        model.Available.Should().HaveCount(3);
    }
}