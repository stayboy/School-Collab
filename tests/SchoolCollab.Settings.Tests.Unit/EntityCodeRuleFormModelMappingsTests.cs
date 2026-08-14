using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Settings.Application.Components.Pages.EntityCodeRules;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Unit tests for the DTO → form-model projection on
/// <see cref="EntityCodeRuleFormModel"/>
/// (<see cref="EntityCodeRuleFormModel.LoadFrom"/> /
/// <see cref="EntityCodeRuleFormModel.From"/>) used by the entity-code-rule edit
/// page. Keeping the projection (including the segment DTO → segment form-model
/// conversion) in a named, tested method makes the mapping easy to verify and
/// keeps it in lockstep with both types.
/// </summary>
[TestClass]
public class EntityCodeRuleFormModelMappingsTests
{
    private static EntityCodeRuleDto MakeRule(IReadOnlyList<EntityCodeSegmentDto>? segments = null) => new(
        Id: Guid.NewGuid(),
        Code: "STU",
        Name: "Student Rule",
        Description: "Generates student codes",
        IsActive: true,
        TenantId: null,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow,
        Segments: segments ?? Array.Empty<EntityCodeSegmentDto>());

    private static EntityCodeSegmentDto MakeSegment(int index, SegmentTypeDto type = SegmentTypeDto.Fixed,
        ResetPeriodDto resetPeriod = ResetPeriodDto.None) => new(
        Id: Guid.NewGuid(),
        Index: index,
        Role: $"seg-{index}",
        Type: (int)type,
        FixedText: "F",
        Prefix: "P",
        Suffix: "S",
        ResetPeriod: (int)resetPeriod,
        MinWidth: 3,
        UpperLimit: "99",
        LastSequence: 0,
        LastPrefix: null,
        LastPeriodBucket: null);

    [TestMethod]
    public void LoadFrom_MapsNameDescriptionAndIsActive()
    {
        var rule = MakeRule();
        var model = new EntityCodeRuleFormModel();

        model.LoadFrom(rule);

        model.Name.Should().Be(rule.Name);
        model.Description.Should().Be(rule.Description);
        model.IsActive.Should().Be(rule.IsActive);
    }

    [TestMethod]
    public void LoadFrom_ProjectsSegments_OrderedByIndex_WithAllFields()
    {
        // Deliberately out of order to assert the projection sorts by Index.
        var segments = new[]
        {
            MakeSegment(2, SegmentTypeDto.NumericSequence, ResetPeriodDto.Yearly),
            MakeSegment(0, SegmentTypeDto.Fixed, ResetPeriodDto.None),
            MakeSegment(1, SegmentTypeDto.AlphanumericSequence, ResetPeriodDto.Quarterly),
        };
        var rule = MakeRule(segments);
        var model = new EntityCodeRuleFormModel();

        model.LoadFrom(rule);

        model.Segments.Should().HaveCount(3);
        model.Segments.Select(s => s.Index).Should().Equal(0, 1, 2);

        var seg0 = model.Segments[0];
        seg0.Role.Should().Be("seg-0");
        seg0.Type.Should().Be(SegmentTypeDto.Fixed);
        seg0.FixedText.Should().Be("F");
        seg0.Prefix.Should().Be("P");
        seg0.Suffix.Should().Be("S");
        seg0.ResetPeriod.Should().Be(ResetPeriodDto.None);
        seg0.MinWidth.Should().Be(3);
        seg0.UpperLimit.Should().Be("99");

        var seg1 = model.Segments[1];
        seg1.Type.Should().Be(SegmentTypeDto.AlphanumericSequence);
        seg1.ResetPeriod.Should().Be(ResetPeriodDto.Quarterly);

        var seg2 = model.Segments[2];
        seg2.Type.Should().Be(SegmentTypeDto.NumericSequence);
        seg2.ResetPeriod.Should().Be(ResetPeriodDto.Yearly);
    }

    [TestMethod]
    public void LoadFrom_EmptySegments_ProducesEmptyList()
    {
        var rule = MakeRule(Array.Empty<EntityCodeSegmentDto>());
        var model = new EntityCodeRuleFormModel();

        model.LoadFrom(rule);

        model.Segments.Should().BeEmpty();
    }
}