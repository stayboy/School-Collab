using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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
    /// FluentUI 5.x renders dialog content by spreading
    /// <c>DialogOptions.Parameters</c> onto the dialog component's
    /// <c>[Parameter]</c> properties <em>by name</em> — v4's
    /// <c>IDialogContentComponent&lt;DialogParameters&gt;</c> +
    /// <c>Content.TryGet&lt;T&gt;(key)</c> pair is gone (it was the
    /// <c>ShowReadonlyDialogAsync</c> binding contract, and the marker string it
    /// keyed on no longer exists). Two failure modes replace the old "a ReadOnly
    /// dialog must not declare a data [Parameter]" rule:
    /// <list type="bullet">
    ///   <item>a key that matches no <c>[Parameter]</c> throws at render — the
    ///         dialogs declare their keys as <c>const string XxxKey =
    ///         nameof(Xxx)</c>, so this guard checks every key constant's target
    ///         really is a declared parameter;</item>
    ///   <item>a REQUIRED param the caller never passes silently defaults — the
    ///         empty-guid edit-dialog bug this guard exists for
    ///         (<c>StudentEditDialog</c> / <c>GuardianContactsDialog</c> opening
    ///         with an empty id and a blank form).</item>
    /// </list>
    /// A parameter is optional when it declares a default, is nullable, or is a
    /// delegate — the documented optional-callback family, and
    /// <c>OverrideDialog.SegmentOptions</c> (shell dialog, empty-array default,
    /// no caller) is the precedent for default-means-optional. Shell dialogs
    /// (<c>@inherits DialogShellBase&lt;…&gt;</c>) receive their data through the
    /// base's single <c>Content</c> parameter, so a data <c>[Parameter]</c> they
    /// declare themselves has no key any caller could pass.
    /// See documents/solution/dialog-parameter-binding.md.
    /// </summary>
    [TestMethod]
    public void Dialog_Parameters_AreDeclaredAndSuppliedByCallers()
    {
        // [Parameter] public X Name { get; set; } [= default][;]  — the trailing
        // semicolon is present only when the property has an initializer.
        var paramRx = new Regex(
            @"\[Parameter[^\]]*\]\s*public\s+(?<type>[^={\r\n]+?)\s+(?<name>\w+)\s*\{(?<body>[^{}]*)\}(?<default>\s*=\s*[^;\r\n]+)?\s*;?",
            RegexOptions.Multiline);
        var keyRx = new Regex(
            @"public\s+const\s+string\s+(?<const>\w+)\s*=\s*nameof\((?<param>\w+)\)");
        var failures = new List<string>();

        var allFiles = SourceFiles(new[] { ".razor", ".cs" }).ToList();
        var texts = allFiles.ToDictionary(f => f, File.ReadAllText);
        var byComponentName = texts.Keys
            .Where(f => Path.GetExtension(f).Equals(".razor", StringComparison.OrdinalIgnoreCase))
            .GroupBy(f => Path.GetFileNameWithoutExtension(f))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Every key constant must target a declared [Parameter], or the key can
        // never bind and FluentUI throws on the DynamicComponent render.
        foreach (var file in allFiles.Where(f => Path.GetExtension(f).Equals(".razor", StringComparison.OrdinalIgnoreCase)))
        {
            var text = texts[file];
            var declaredNames = paramRx.Matches(text).Select(m => m.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
            foreach (Match key in keyRx.Matches(text))
            {
                var target = key.Groups["param"].Value;
                if (!declaredNames.Contains(target))
                {
                    failures.Add(
                        $"{Relative(file)} declares key constant {key.Groups["const"].Value} = nameof({target}), but no " +
                        $"[Parameter] {target} exists — DialogOptions.Parameters keys must match a [Parameter] name " +
                        "(dialog-parameter-binding.md).");
                }
            }
        }

        // Each helper's call block must pass every required parameter of the dialog it opens.
        var callRx = new Regex(@"(?:ShowReadonlyDialogAsync|ShowShellDialogAsync)\s*<\s*(?<dialog>\w+)\s*[>,]");
        foreach (var callerFile in allFiles)
        {
            var callerText = texts[callerFile];
            foreach (Match call in callRx.Matches(callerText))
            {
                var dialogName = call.Groups["dialog"].Value;
                if (!byComponentName.TryGetValue(dialogName, out var dialogFile)) continue;

                var block = CallBlock(callerText, call.Index + call.Length - 1);
                var dialogText = texts[dialogFile];
                var keyNames = keyRx.Matches(dialogText)
                    .ToDictionary(m => m.Groups["param"].Value, m => m.Groups["const"].Value, StringComparer.Ordinal);

                foreach (Match p in paramRx.Matches(dialogText))
                {
                    var name = p.Groups["name"].Value;
                    if (IsOptionalParameter(p)) continue;

                    keyNames.TryGetValue(name, out var keyConstant);
                    // The call's own argument list first; a caller may hoist its
                    // dictionary into a local (Students/Detail.razor does), in which
                    // case the key is still mentioned in the same file — that
                    // file-level mention is the fallback.
                    var supplied = keyConstant is not null
                        && (Regex.IsMatch(block, $@"\b{dialogName}\s*<[^<>]*>\s*\.\s*{Regex.Escape(keyConstant)}\b")
                            || Regex.IsMatch(block, $@"\b{dialogName}\s*\.\s*{Regex.Escape(keyConstant)}\b")
                            || callerText.Contains($"{dialogName}.{keyConstant}", StringComparison.Ordinal));
                    supplied = supplied || block.Contains($"\"{name}\"", StringComparison.Ordinal);

                    if (!supplied)
                    {
                        failures.Add(
                            $"{Relative(callerFile)} opens {dialogName} without supplying required parameter '{name}'" +
                            (keyConstant is null
                                ? " (the dialog declares no key constant for it — add `const string " + name + "Key = nameof(" + name + ")`)"
                                : $" (pass {dialogName}.{keyConstant} in DialogOptions.Parameters)") +
                            " — an unsupplied parameter silently defaults (the empty-guid edit-dialog bug; dialog-parameter-binding.md).");
                    }
                }
            }
        }

        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    /// <summary>
    /// The text of the balanced <c>( … )</c> argument list starting at
    /// <paramref name="openParenIndex"/> — lets each check read one call site's
    /// arguments without mis-truncating a multi-line dictionary literal.
    /// </summary>
    private static string CallBlock(string text, int openParenIndex)
    {
        var depth = 0;
        for (var i = openParenIndex; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')')
            {
                depth--;
                if (depth == 0) return text.Substring(openParenIndex, i - openParenIndex + 1);
            }
        }
        return text.Substring(openParenIndex);
    }

    /// <summary>
    /// A parameter is optional when it declares a default value, is nullable, or
    /// is a delegate/callback (the documented optional-callback family).
    /// </summary>
    private static bool IsOptionalParameter(Match parameter)
    {
        if (parameter.Groups["default"].Success) return true;

        var type = parameter.Groups["type"].Value.Trim();
        if (type.EndsWith("?", StringComparison.Ordinal)) return true;

        foreach (var delegatePrefix in new[] { "Func<", "Action<", "EventCallback", "RenderFragment" })
        {
            if (type.StartsWith(delegatePrefix, StringComparison.Ordinal)) return true;
        }
        return false;
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

    /// <summary>
    /// The all-inclusive edit dialog must save the whole student (profile + guardians +
    /// contacts) in ONE atomic request via <c>UpdateStudentWithLinkedDataAsync</c> — not the
    /// old profile-only <c>UpdateStudentAsync</c>, and not per-row live link/unlink/contact
    /// calls. The page-side <c>Students/Edit.razor</c> is intentionally still live (it manages
    /// guardians/contacts over time), so this guard is scoped to the dialog file only.
    /// See documents/solution/dto-form-model-mapping.md.
    /// </summary>
    [TestMethod]
    public void StudentEditDialog_Saves_Atomically_Not_Live()
    {
        var file = SourceFiles(new[] { ".razor" })
            .FirstOrDefault(f => Path.GetFileName(f).Equals("StudentEditDialog.razor", StringComparison.OrdinalIgnoreCase));
        file.Should().NotBeNull("StudentEditDialog.razor should exist");
        var text = File.ReadAllText(file!);

        var forbidden = new[]
        {
            "UpdateStudentAsync",   // old profile-only save
            "LinkGuardianAsync",    // live per-row link
            "UnlinkGuardianAsync",  // live per-row unlink
            "AddContactAsync",      // live per-row contact add
            "UpdateContactAsync",   // live per-row contact update
            "DeleteContactAsync",   // live per-row contact delete
        };
        var failures = forbidden.Where(t => text.Contains(t, StringComparison.Ordinal)).ToList();
        failures.Should().BeEmpty(
            "StudentEditDialog must save atomically via UpdateStudentWithLinkedDataAsync, not " +
            "UpdateStudentAsync or live per-row link/unlink/contact calls (dto-form-model-mapping.md). Found: " +
            string.Join(", ", failures));
    }

    /// <summary>
    /// WS-E1 (ar-14-deep-links): both deep-link hosts must persist their DataProtection
    /// keyring under the SAME <see cref="SchoolCollab.Core.DeepLinks.DeepLinkConstants.ApplicationName"/>
    /// or Families cannot unprotect mint-side ciphertext. Guard the actual host entry points
    /// (source shape) so a host forgetting <c>SetApplicationName(DeepLinkConstants.ApplicationName)</c>
    /// fails here even when the wiring tests in <c>SchoolCollab.Families.Tests.Unit</c> build
    /// their own providers (which could mask a dropped call in the real host).
    /// </summary>
    [TestMethod]
    public void DeepLinkHosts_SetSharedKeyringApplicationName()
    {
        var deeplinkPrograms = new[]
        {
            Path.Combine(SrcRoot, "Assignments", "SchoolCollab.Assignments.Api", "Program.cs"),
            Path.Combine(SrcRoot, "SchoolCollab.Families", "Program.cs"),
        };

        var missing = deeplinkPrograms.Where(p => !File.Exists(p)).ToList();
        missing.Should().BeEmpty("expected deep-link host Program.cs files to exist: " + string.Join(", ", missing));

        var failures = deeplinkPrograms
            .Where(File.Exists)
            .Where(p => !File.ReadAllText(p).Contains(
                "SetApplicationName(DeepLinkConstants.ApplicationName)", StringComparison.Ordinal))
            .Select(Relative)
            .ToList();

        failures.Should().BeEmpty(
            "both deep-link hosts must call " +
            "SetApplicationName(DeepLinkConstants.ApplicationName) on their keyring " +
            "(shared DataProtection contract, ar-14-deep-links). Files:\n" + string.Join("\n", failures));
    }
}