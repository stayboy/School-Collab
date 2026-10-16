using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards the v5 dialog-chrome contract found on 2026-10-15 (Round A of the FluentUI v5 migration).
///
/// <para>v5 removed <c>FluentDialogHeader</c>/<c>FluentDialogFooter</c>; a dialog's header — its title
/// and its close (X) — renders <b>only</b> inside <c>FluentDialogBody</c>. Nothing in the repo rendered
/// that component, so all of the repo's dialogs were simultaneously title-less and X-less and the build
/// was silent about it: a missing body is a valid dialog body. The fix is the shared
/// <c>DialogShellBody</c> wrapper (the body-side sibling of <c>DialogShellFooter</c>), adopted by every
/// dialog.</para>
///
/// <para>This is the <b>class-level</b> guard for that omission, because the defect cost nothing to
/// introduce: it fails on any dialog reachable through the shared dialog service that does not render
/// <c>DialogShellBody</c> — every <c>@inherits DialogShellBase&lt;…&gt;</c> component, and every
/// component the repo opens via <c>ShowReadonlyDialogAsync&lt;T&gt;</c>.</para>
///
/// <para><b>Deliberate exclusion:</b> <c>ConfirmDialog</c> is opened by
/// <c>ShowConfirmDialogAsync</c>, which builds its own <c>DialogOptions</c> — it is outside this
/// guard's two categories, and it matches FluentUI's own <c>FluentMessageBox</c>, which renders no
/// header close action by default. Give it chrome deliberately, in its own change, if that is wanted.</para>
/// </summary>
[TestClass]
public class DialogChromeArchitectureTests
{
    private static readonly string SrcRoot = FindSrcRoot();

    /// <summary>A shell dialog — it must render the shared body/chrome.</summary>
    private static readonly Regex ShellDialogInherits =
        new(@"@inherits\s+DialogShellBase\s*<", RegexOptions.Compiled);

    /// <summary>A read-only dialog open: <c>ShowReadonlyDialogAsync&lt;DialogType&gt;(…)</c>. A generic
    /// argument such as <c>SectionListDialog&lt;TItem&gt;</c> yields the outer type name.</summary>
    private static readonly Regex ReadonlyDialogOpen =
        new(@"ShowReadonlyDialogAsync\s*<\s*(?<type>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    private const string ShellBodyOpenTag = "<DialogShellBody>";
    [TestMethod]
    public void EveryDialog_RendersTheSharedDialogShellBody_SoTheTitleAndCloseXRender()
    {
        var offenders = new List<string>();

        foreach (var file in RazorFiles())
        {
            var text = File.ReadAllText(file);

            if (ShellDialogInherits.IsMatch(text) && !text.Contains(ShellBodyOpenTag, StringComparison.Ordinal))
            {
                offenders.Add(
                    $"{Relative(file)} — inherits DialogShellBase<…> but renders no <DialogShellBody>. " +
                    "Without it v5 renders no header (no title, no close X) and says nothing.");
            }
        }

        foreach (var type in ReadonlyDialogTypesOpenedByTheService())
        {
            var dialog = RazorFiles().FirstOrDefault(
                p => string.Equals(Path.GetFileNameWithoutExtension(p), type, StringComparison.Ordinal));

            if (dialog is null)
            {
                offenders.Add($"ShowReadonlyDialogAsync<{type}>(…) — no {type}.razor found under src/.");
                continue;
            }

            if (!File.ReadAllText(dialog).Contains(ShellBodyOpenTag, StringComparison.Ordinal))
            {
                offenders.Add(
                    $"{Relative(dialog)} — opened via ShowReadonlyDialogAsync<{type}>(…) but renders no " +
                    "<DialogShellBody>. Without it v5 renders no header (no title, no close X).");
            }
        }

        offenders.Should().BeEmpty(
            "every dialog must render DialogShellBody, or it silently loses its title and close X");
    }

    /// <summary>The dialog types the repo opens through <c>ShowReadonlyDialogAsync&lt;T&gt;</c>.</summary>
    private static IEnumerable<string> ReadonlyDialogTypesOpenedByTheService()
    {
        var types = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in SourceFiles())
        {
            foreach (Match match in ReadonlyDialogOpen.Matches(File.ReadAllText(file)))
            {
                types.Add(match.Groups["type"].Value);
            }
        }

        return types;
    }

    private static IEnumerable<string> RazorFiles() => SourceFiles("*.razor");

    /// <summary>
    /// The repo's source files (razor + C#), excluding generated output and the
    /// <c>DialogServiceExtensions</c> declaration file — its own
    /// <c>ShowReadonlyDialogAsync&lt;TComponent&gt;</c> signature is a declaration, not an open.
    /// </summary>
    private static IEnumerable<string> SourceFiles(string pattern = "*.*")
    {
        var extensions = pattern == "*.razor" ? new[] { ".razor" } : new[] { ".razor", ".cs" };

        return Directory.EnumerateFiles(SrcRoot, "*", SearchOption.AllDirectories)
            .Where(p => extensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .Where(p => !Path.GetFileName(p).Equals("DialogServiceExtensions.cs", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static string Relative(string path) => path.Replace(SrcRoot, "src").Replace('\\', '/');

    private static string FindSrcRoot()
    {
        var asmDir = Path.GetDirectoryName(typeof(DialogChromeArchitectureTests).Assembly.Location)!;
        return Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", "..", "src"));
    }
}
