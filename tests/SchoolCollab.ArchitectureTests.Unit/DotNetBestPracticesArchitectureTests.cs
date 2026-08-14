using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// CI enforcement for the checkable subset of
/// <c>.github/copilot/rules/dotnet-best-practices.md</c> (and its backing skill
/// <c>.github/skills/dotnet-best-practices/SKILL.md</c>). These are source-inspection
/// guards that scan the <c>src/</c> tree for the skill's "Never" list and fail the build
/// (via the <c>~Tests.Unit</c> CI filter) if a violation creeps in.
///
/// Only invariants that actually hold across the current codebase are covered here, so a
/// new guard never fails on pre-existing code. When you add more best-practice rules, first
/// verify they hold repo-wide (scan like the existing tests do), then add a guard.
/// </summary>
[TestClass]
public class DotNetBestPracticesArchitectureTests
{
    private static readonly string SrcRoot = FindSrcRoot();

    private static string FindSrcRoot()
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", "..", "src"));
    }

    private static IEnumerable<string> SourceFiles(string[] extensions)
    {
        return Directory.EnumerateFiles(SrcRoot, "*", SearchOption.AllDirectories)
            .Where(p => extensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .Where(p => !p.Contains("\\obj\\") && !p.Contains("\\bin\\"));
    }

    private static string Relative(string path) => path.Replace(SrcRoot, "src");

    /// <summary>
    /// The repo's CQRS uses its own <c>ICommandHandler&lt;T,R&gt;</c> / <c>IQueryHandler&lt;T,R&gt;</c>
    /// interfaces (dotnet-best-practices). MediatR is a "Never". Guard each file against the
    /// unambiguous MediatR surface: <c>IMediator</c>, <c>using MediatR</c>, and the <c>MediatR</c>
    /// namespace qualifier. (Deliberately NOT a bare "CommandHandler&lt;" — that matches the repo's
    /// own <c>ICommandHandler&lt;</c> interface.)
    /// </summary>
    [TestMethod]
    public void No_MediatR_In_Source()
    {
        var forbidden = new[] { "IMediator", "using MediatR", "MediatR." };
        var failures = new List<string>();

        foreach (var file in SourceFiles(new[] { ".cs", ".razor" }))
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbidden)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                {
                    failures.Add($"{Relative(file)} contains '{token}' (MediatR is a never — use the repo's ICommandHandler/IQueryHandler).");
                }
            }
        }

        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    /// <summary>
    /// The generic awesome-copilot dotnet-best-practices guidance wrongly references
    /// SemanticKernel; this repo does not use it. Guard src/ against it.
    /// </summary>
    [TestMethod]
    public void No_SemanticKernel_In_Source()
    {
        var failures = SourceFiles(new[] { ".cs", ".razor" })
            .Where(f => File.ReadAllText(f).Contains("SemanticKernel", StringComparison.Ordinal))
            .Select(Relative)
            .ToList();

        failures.Should().BeEmpty(
            "SemanticKernel is not used in this repo (dotnet-best-practices). Files:\n" +
            string.Join("\n", failures));
    }

    /// <summary>
    /// Production code must not call <c>Console.WriteLine</c> (dotnet-best-practices "Never").
    /// Legitimate <c>Main</c> entry points (e.g. workers, MigrationService Program.cs) are the
    /// exception and are excluded by filename.
    /// </summary>
    [TestMethod]
    public void No_ConsoleWriteLine_Outside_ProgramCs()
    {
        var failures = SourceFiles(new[] { ".cs" })
            .Where(f => !Path.GetFileName(f).Equals("Program.cs", StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllText(f).Contains("Console.WriteLine", StringComparison.Ordinal))
            .Select(Relative)
            .ToList();

        failures.Should().BeEmpty(
            "Console.WriteLine is not allowed in production code outside Main/Program.cs " +
            "(dotnet-best-practices). Files:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// FluentUI 4.14.x renders <c>ShowReadonlyDialogAsync</c> content via
    /// <c>DynamicComponent</c> with only <c>{ "Content": &lt;DialogParameters&gt; }</c> — it does
    /// NOT spread <c>DialogParameters</c> indexer entries onto separate <c>[Parameter]</c>s.
    /// So a component opened that way must read its inputs from <c>Content.TryGet&lt;T&gt;(XxxKey)</c>
    /// and must NOT declare a data-input <c>[Parameter]</c> (which silently defaults to
    /// empty/null — the empty-guid edit-dialog bug). Only <c>[Parameter] DialogParameters Content</c>
    /// is allowed. See documents/solution/dialog-parameter-binding.md.
    /// </summary>
    [TestMethod]
    public void Dialog_ContentParameter_Only_For_ReadonlyDialogs()
    {
        var rx = new System.Text.RegularExpressions.Regex(
            @"\[Parameter[^\]]*\]\s*public\s+(\S+)\s+(\w+)\s*\{",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        var failures = new List<string>();

        foreach (var file in SourceFiles(new[] { ".razor" })
            .Where(f => File.ReadAllText(f).Contains("IDialogContentComponent<DialogParameters>", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match m in rx.Matches(text))
            {
                var type = m.Groups[1].Value;
                var name = m.Groups[2].Value;
                if (type != "DialogParameters" || name != "Content")
                {
                    failures.Add(
                        $"{Relative(file)} declares [Parameter] {type} {name} — a ShowReadonlyDialogAsync dialog must read its " +
                        $"inputs from Content.TryGet<...>(...), not a separate [Parameter] (dialog-parameter-binding.md).");
                }
            }
        }

        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    /// <summary>
    /// <c>StudentFormModel</c> fields must be projected via
    /// <c>StudentFormModel.LoadFrom/From</c>, not copied field-by-field in a
    /// razor file. An inline copy silently drifts out of sync (a blank UI field) with no
    /// compile error or failing test. See documents/solution/dto-form-model-mapping.md.
    /// </summary>
    [TestMethod]
    public void No_Inline_StudentFormModel_FieldCopies_In_Razor()
    {
        var studentFields = new[] { "StudentNumber", "FirstName", "LastName", "DateOfBirth", "GenderCodedValueId", "TitleCodedValueId" };
        var failures = new List<string>();

        foreach (var file in SourceFiles(new[] { ".razor" }))
        {
            var text = File.ReadAllText(file);
            foreach (var field in studentFields)
            {
                // _model.FirstName = ...;  (an inline copy, not the mapping extension)
                if (System.Text.RegularExpressions.Regex.IsMatch(text, $"_model\\.{field}\\s*="))
                {
                    failures.Add(
                        $"{Relative(file)} inline-copies StudentFormModel.{field} — use " +
                        $"StudentFormModel.LoadFrom/From instead (dto-form-model-mapping.md).");
                }
            }
        }

        failures.Should().BeEmpty(string.Join("\n", failures));
    }
}