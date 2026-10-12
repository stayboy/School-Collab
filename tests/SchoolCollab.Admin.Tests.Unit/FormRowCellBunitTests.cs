using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// <c>FormRowCell</c> — the paired-input cell of the shared FormRow family (owner, 2026-10-11):
/// each input in a multi-input row carries its own name UNDER it, while the row keeps the short
/// group label. These seams pin the structure: caption under the input, label association, and the
/// captionless cell as a plain grouping.
/// </summary>
[TestClass]
public class FormRowCellBunitTests : BunitContext
{
    [TestMethod]
    public void RendersTheCaptionUnderTheInput_AndLabelsIt()
    {
        var cut = Render<FormRowCell>(p => p
            .Add(c => c.Caption, "Max score")
            .Add(c => c.For, "field-1")
            .Add(c => c.Help, "Long wording lives in the tooltip")
            .AddChildContent(builder => builder.AddMarkupContent(0, "<input id=\"field-1\" />")));

        var cell = cut.Find(".form-row-cell");
        cell.Children[0].LocalName.Should().Be("input", "the caption sits UNDER its input");
        cell.Children[1].LocalName.Should().Be("label");

        var caption = cut.Find("label.form-row-cell-caption");
        caption.TextContent.Should().Contain("Max score");
        caption.GetAttribute("for").Should().Be("field-1");

        // Owner 2026-10-11: the optional Help rides the caption as a help-hint tooltip — the
        // long wording costs no inline space.
        cut.Find("label.form-row-cell-caption .help-icon").GetAttribute("title")
            .Should().Be("Long wording lives in the tooltip");
    }

    [TestMethod]
    public void WithoutCaptionOrHelp_RendersTheBareCell()
    {
        var cut = Render<FormRowCell>(p => p
            .AddChildContent(builder => builder.AddMarkupContent(0, "<input id=\"field-2\" />")));

        cut.Find(".form-row-cell").Should().NotBeNull();
        cut.Find("#field-2").Should().NotBeNull();
        cut.FindAll("label").Should().BeEmpty("no Caption and no Help mean no caption element");
    }
}