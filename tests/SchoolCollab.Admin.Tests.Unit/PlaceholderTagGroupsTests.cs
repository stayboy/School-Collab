using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.RichText;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// <see cref="PlaceholderTagGroups"/> — the placeholder-tag menu's ONE group map (naming spec D14). Its
/// contract is a partition: every tag the caller offers reaches exactly one group, the declared groups
/// keep their order, and nothing the map does not name is lost.
/// </summary>
[TestClass]
public class PlaceholderTagGroupsTests
{
    [TestMethod]
    public void TheRealVocabulary_ProjectsOntoExactlyTheFourDeclaredGroups_LeavingOtherEmpty()
    {
        // Act
        var groups = PlaceholderTagGroups.For(NotificationTemplateTags.All);

        // Assert
        groups.Select(g => g.Name).Should().Equal(
            PlaceholderTagGroups.All.Select(g => g.Name),
            "the ten tags are all placed, so the declared groups are exactly what the menu renders");
        groups.Select(g => g.Name).Should().NotContain(PlaceholderTagGroups.OtherGroupName,
            "an empty group is dropped, and the trailing catch-all has nothing to catch");
    }

    [TestMethod]
    public void TheGroups_UnionToTheWholeVocabulary()
    {
        // Act
        var tags = PlaceholderTagGroups.For(NotificationTemplateTags.All).SelectMany(g => g.Tags).ToList();

        // Assert
        tags.Should().BeEquivalentTo(NotificationTemplateTags.All,
            "no vocabulary entry may become unreachable, and the map may not invent one");
        tags.Should().OnlyHaveUniqueItems("a tag offered once is listed once");
    }

    [TestMethod]
    public void ARepeatedTag_IsOfferedOnce()
    {
        // Act
        var groups = PlaceholderTagGroups.For(["Title", "Title", "Instructions", "Instructions"]);

        // Assert
        groups.SelectMany(g => g.Tags).Should().Equal(["Title", "Instructions"]);
    }

    [TestMethod]
    public void AnUnknownTag_IsKeptInATrailingOtherGroup()
    {
        // Act
        var groups = PlaceholderTagGroups.For(["NotInTheVocabulary", "Title"]);

        // Assert
        groups[^1].Name.Should().Be(PlaceholderTagGroups.OtherGroupName);
        groups[^1].Tags.Should().Equal(["NotInTheVocabulary"]);
        groups.Take(groups.Count - 1).SelectMany(g => g.Tags).Should().Equal(["Title"],
            "the catch-all comes LAST, after every declared group");
    }

    [TestMethod]
    public void GroupsTheCallerOffersNothingFrom_AreDropped()
    {
        // Act
        var groups = PlaceholderTagGroups.For([NotificationTemplateTags.Link]);

        // Assert
        groups.Should().ContainSingle().Which.Name.Should().Be("School & links");
    }
}
