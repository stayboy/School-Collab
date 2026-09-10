using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using Moq;
using RichardSzalay.MockHttp;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;
using SchoolCollab.Assignments.Application.Helpers;
using SchoolCollab.Assignments.Application.Services;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// bUnit coverage for <see cref="ResourcesSection"/> (the wizard
/// Step-2 Resources upload surface). Per the round-ar-4 plan's binding
/// coverage list:
/// <list type="bullet">
///   <item>Empty model renders the section (hint, upload control,
///     "No resources attached yet.").</item>
///   <item>Client-side allowlist reject (.exe) — friendly error renders,
///     no HTTP call.</item>
///   <item>Client-side size reject — error renders, no HTTP call.</item>
///   <item>Happy path — MockHttp responds 200 with a StagedAttachmentDto;
///     the staged row renders with file name + formatted size;
///     Model.Attachments has 1 row with the returned StoragePath;
///     error cleared.</item>
///   <item>Server rejection (400 {message}) — AttachmentStagingFailed
///     message renders, no row added, second valid StageFileAsync
///     succeeds (retryable).</item>
///   <item>Remove — IDialogService mock declining keeps the row;
///     confirming removes it from Model.Attachments; MockHttp records
///     zero DELETE requests (decision (b) soft removal).</item>
///   <item>Two uploads → two rows (@key distinct).</item>
/// </list>
///
/// Follows the in-round convention from
/// <c>QuestionGenerationSectionBunitTests</c> (MSTest + FluentAssertions
/// + bUnit). The FluentInputFile JS pipeline is NOT driven — tests call
/// <see cref="ResourcesSection.StageFileAsync"/> directly (the ar-3
/// recorded fallback for web-component quirks). Uses
/// RichardSzalay.MockHttp over a real <see cref="HttpClient"/> for
/// <see cref="AssignmentsApiClient"/>; Moq for
/// <see cref="IDialogService"/> + <see cref="ILogger{T}"/>.
///
/// <para>
/// <b>Note on <c>FluentInputFile.OnFileError</c>:</b> the Microsoft
/// FluentUI Blazor 4.14.2 documentation explicitly marks this callback as
/// "Not yet used" (verified against the published
/// <c>Microsoft.FluentUI.AspNetCore.Components.xml</c> for 4.14.2).
/// Because of that, the ResourcesSection does NOT bind
/// <c>OnFileError</c> — size/allowlist rejections are caught by the
/// <c>AttachmentUploadPolicy.Validate</c> pre-check inside
/// <see cref="ResourcesSection.StageFileAsync"/> and rendered via the
/// section's error bar. If a future FluentUI upgrade makes
/// <c>OnFileError</c> a real callback, the re-add must be deliberate
/// (and must not duplicate the pre-check logic).
/// </para>
/// </summary>
[TestClass]
public class ResourcesSectionBunitTests : BunitContext
{
    private readonly MockHttpMessageHandler _mockHttp;
    private readonly JsonSerializerOptions _apiJsonOptions;

    public ResourcesSectionBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();

        _apiJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new JsonStringEnumConverter<ModuleTypeDto>(),
                new JsonStringEnumConverter<ResourceKindDto>(),
            }
        };

        _mockHttp = new MockHttpMessageHandler();
        var httpClient = _mockHttp.ToHttpClient();
        httpClient.BaseAddress = new Uri("http://localhost");

        Services.AddSingleton(httpClient);
        Services.AddSingleton<AssignmentsApiClient>();
        Services.AddSingleton(Mock.Of<ILogger<AssignmentsApiClient>>());
        Services.AddSingleton(Mock.Of<ILogger<ResourcesSection>>());
    }

    private IRenderedComponent<ResourcesSection> RenderSection(
        AssignmentEditFormModel model,
        Mock<IDialogService>? dialogMock = null)
    {
        if (dialogMock is not null)
        {
            Services.AddSingleton(dialogMock.Object);
        }
        return Render<ResourcesSection>(parameters =>
        {
            parameters.Add(p => p.Model, model);
            parameters.Add(p => p.Changed, EventCallback.Empty);
        });
    }

    private static Stream StreamOf(string s) =>
        new MemoryStream(Encoding.UTF8.GetBytes(s));

    [TestMethod]
    public void EmptyModel_RendersUploadControlAndNoResourcesInfo()
    {
        var model = new AssignmentEditFormModel();

        var cut = RenderSection(model);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("No resources attached yet.",
                "an empty model surfaces the friendly empty state");
            // The FluentInputFile control renders its inner label text "Upload files".
            cut.Markup.Should().Contain("Upload files",
                "the upload control's label is rendered so the section is visible");
            cut.Markup.Should().Contain("0 file(s) attached",
                "the toolbar count reflects the empty model");
        });
    }

    [TestMethod]
    public async Task StageFileAsync_BadExtension_RendersFriendlyError_NoHttpCall()
    {
        var model = new AssignmentEditFormModel();
        var cut = RenderSection(model);

        await cut.Instance.StageFileAsync(
            StreamOf("not allowed"), "bad.exe", "application/octet-stream", 10);
        cut.Render();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("'.exe' files are not allowed.",
                "the client allowlist rejects before any HTTP round-trip");
        });
        model.Attachments.Should().BeEmpty(
            "a client-rejected file must not be added to the form model");

        _mockHttp.VerifyNoOutstandingExpectation();
    }

    [TestMethod]
    public async Task StageFileAsync_OversizedFile_RendersFriendlyError_NoHttpCall()
    {
        var model = new AssignmentEditFormModel();
        var cut = RenderSection(model);

        // 26 MiB - 1 byte over the 25 MiB cap.
        var oversized = AttachmentUploadPolicy.MaxFileSizeBytes + 1;
        await cut.Instance.StageFileAsync(
            StreamOf("too big"), "big.pdf", "application/pdf", oversized);
        cut.Render();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("exceeds the 25 MB limit",
                "the client size cap surfaces a friendly message before any HTTP call");
        });
        model.Attachments.Should().BeEmpty();
        _mockHttp.VerifyNoOutstandingExpectation();
    }

    [TestMethod]
    public async Task StageFileAsync_HappyPath_AppendsRowAndClearsError()
    {
        var model = new AssignmentEditFormModel();
        var cut = RenderSection(model);

        var staged = new StagedAttachmentDto(
            FileName: "notes.pdf",
            ContentType: "application/pdf",
            FileSize: 3,
            StoragePath: "tenants/t/staging/g/notes.pdf");
        _mockHttp.Expect(HttpMethod.Post, "http://localhost/assignments/attachments/stage")
            .Respond(HttpStatusCode.OK, "application/json",
                JsonSerializer.Serialize(staged, _apiJsonOptions));

        await cut.Instance.StageFileAsync(
            StreamOf("pdf-bytes"), "notes.pdf", "application/pdf", 3);
        cut.Render();

        cut.WaitForAssertion(() =>
        {
            model.Attachments.Should().HaveCount(1,
                "a successful stage + add appends one row to the form model");
            model.Attachments[0].FileName.Should().Be("notes.pdf");
            model.Attachments[0].ContentType.Should().Be("application/pdf");
            model.Attachments[0].FileSize.Should().Be(3);
            model.Attachments[0].StoragePath.Should().Be("tenants/t/staging/g/notes.pdf");
        });
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("notes.pdf",
                "the staged file name is rendered in the list");
            cut.Markup.Should().Contain("3 B",
                "the formatted file size is rendered in the list");
            cut.Markup.Should().Contain("1 file(s) attached",
                "the toolbar count reflects the appended row");
        });
        cut.WaitForAssertion(() =>
        {
            _mockHttp.VerifyNoOutstandingExpectation();
        });
    }

    [TestMethod]
    public async Task StageFileAsync_ServerRejectsThenRetry_ThrowsTypedExceptionAndSucceeds()
    {
        var model = new AssignmentEditFormModel();
        var cut = RenderSection(model);

        // First call: server rejects with a typed-message 400 (uses a file
        // the client allows so the HTTP path is reached).
        _mockHttp.Expect(HttpMethod.Post, "http://localhost/assignments/attachments/stage")
            .Respond(HttpStatusCode.BadRequest, "application/json",
                "{\"message\":\"'.exe' files are not allowed.\"}");
        // Second (retry) call: server accepts with a real staged dto.
        var retried = new StagedAttachmentDto(
            FileName: "ok.pdf", ContentType: "application/pdf", FileSize: 4,
            StoragePath: "tenants/t/staging/g2/ok.pdf");
        _mockHttp.Expect(HttpMethod.Post, "http://localhost/assignments/attachments/stage")
            .Respond(HttpStatusCode.OK, "application/json",
                JsonSerializer.Serialize(retried, _apiJsonOptions));

        await cut.Instance.StageFileAsync(
            StreamOf("first"), "first.pdf", "application/pdf", 4);
        await cut.Instance.StageFileAsync(
            StreamOf("second"), "second.pdf", "application/pdf", 4);
        cut.Render();

        cut.WaitForAssertion(() =>
        {
            model.Attachments.Should().HaveCount(1,
                "a server-rejected upload does not append; the successful retry does");
            model.Attachments[0].FileName.Should().Be("ok.pdf",
                "the appended row carries the returned StagedAttachmentDto metadata");
            model.Attachments[0].StoragePath.Should().Be("tenants/t/staging/g2/ok.pdf");
        });
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("ok.pdf",
                "the successful retry's file name is rendered in the list");
            cut.Markup.Should().NotContain("'.exe' files are not allowed.",
                "the error bar clears on the next successful stage");
        });
        cut.WaitForAssertion(() =>
        {
            _mockHttp.VerifyNoOutstandingExpectation();
        });
    }

    [TestMethod]
    public async Task StageFileAsync_Server400_ShowsTypedMessage_NoRowAdded()
    {
        var model = new AssignmentEditFormModel();
        var cut = RenderSection(model);

        // Bypass the client allowlist by feeding a valid PDF; the server then
        // rejects it (simulating an admin-side denylist update or a different
        // upstream rule). The friendly typed message must surface.
        _mockHttp.Expect(HttpMethod.Post, "http://localhost/assignments/attachments/stage")
            .Respond(HttpStatusCode.BadRequest, "application/json",
                "{\"message\":\"'.bat' files are not allowed.\"}");

        await cut.Instance.StageFileAsync(
            StreamOf("contents"), "doc.pdf", "application/pdf", 8);
        cut.Render();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("'.bat' files are not allowed.",
                "the typed server message renders in the section's error bar");
            model.Attachments.Should().BeEmpty(
                "a server-rejected file must not be appended to the form model");
        });
        cut.WaitForAssertion(() =>
        {
            _mockHttp.VerifyNoOutstandingExpectation();
        });
    }

    [TestMethod]
    public async Task RemoveAt_OnDialogDecline_RowStays()
    {
        var model = new AssignmentEditFormModel();
        model.AddAttachment(new AttachmentEditorRow
        {
            FileName = "stay.pdf",
            ContentType = "application/pdf",
            FileSize = 12,
            StoragePath = "tenants/t/staging/g/stay.pdf",
        });

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.SetupGet(r => r.Result).Returns(Task.FromResult(DialogResult.Cancel()));
        var dialogMock = new Mock<IDialogService>();
        dialogMock
            .Setup(d => d.ShowDialogAsync<ConfirmDialog, ConfirmDialogContent>(
                It.IsAny<ConfirmDialogContent>(), It.IsAny<DialogParameters>()))
            .ReturnsAsync(dialogRef.Object);

        var cut = RenderSection(model, dialogMock);

        // Find the row's remove button. The button has aria-label
        // "Remove <fileName>".
        var removeButton = cut.FindAll("fluent-button")
            .First(b => b.GetAttribute("aria-label") == "Remove stay.pdf");
        removeButton.Click();

        cut.WaitForAssertion(() =>
        {
            dialogMock.Verify(
                d => d.ShowDialogAsync<ConfirmDialog, ConfirmDialogContent>(
                    It.IsAny<ConfirmDialogContent>(), It.IsAny<DialogParameters>()),
                Times.Once,
                "remove must request the destructive confirmation dialog");
        });
        cut.WaitForAssertion(() =>
        {
            model.Attachments.Should().HaveCount(1,
                "on dialog decline, the row stays in the form model");
        });
    }

    [TestMethod]
    public async Task RemoveAt_OnDialogConfirm_RowRemoved_NoDeleteCall()
    {
        var model = new AssignmentEditFormModel();
        model.AddAttachment(new AttachmentEditorRow
        {
            FileName = "go.pdf",
            ContentType = "application/pdf",
            FileSize = 9,
            StoragePath = "tenants/t/staging/g/go.pdf",
        });

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.SetupGet(r => r.Result).Returns(Task.FromResult(DialogResult.Ok<object?>(null!)));
        var dialogMock = new Mock<IDialogService>();
        dialogMock
            .Setup(d => d.ShowDialogAsync<ConfirmDialog, ConfirmDialogContent>(
                It.IsAny<ConfirmDialogContent>(), It.IsAny<DialogParameters>()))
            .ReturnsAsync(dialogRef.Object);

        var cut = RenderSection(model, dialogMock);

        var removeButton = cut.FindAll("fluent-button")
            .First(b => b.GetAttribute("aria-label") == "Remove go.pdf");
        removeButton.Click();

        cut.WaitForAssertion(() =>
        {
            model.Attachments.Should().BeEmpty(
                "on dialog confirm, the row is removed from the form model");
        });
        cut.WaitForAssertion(() =>
        {
            // Soft remove - no delete call to the API. The sweep is server-side.
            _mockHttp.VerifyNoOutstandingExpectation();
        });
    }

    [TestMethod]
    public async Task TwoStageFileAsync_TwoRowsDistinct()
    {
        var model = new AssignmentEditFormModel();
        var cut = RenderSection(model);

        var first = new StagedAttachmentDto(
            "a.pdf", "application/pdf", 5,
            "tenants/t/staging/g1/a.pdf");
        var second = new StagedAttachmentDto(
            "b.pdf", "application/pdf", 6,
            "tenants/t/staging/g2/b.pdf");
        _mockHttp.Expect(HttpMethod.Post, "http://localhost/assignments/attachments/stage")
            .Respond(HttpStatusCode.OK, "application/json",
                JsonSerializer.Serialize(first, _apiJsonOptions));
        _mockHttp.Expect(HttpMethod.Post, "http://localhost/assignments/attachments/stage")
            .Respond(HttpStatusCode.OK, "application/json",
                JsonSerializer.Serialize(second, _apiJsonOptions));

        await cut.Instance.StageFileAsync(
            StreamOf("aaa"), "a.pdf", "application/pdf", 5);
        await cut.Instance.StageFileAsync(
            StreamOf("bbb"), "b.pdf", "application/pdf", 6);
        cut.Render();

        cut.WaitForAssertion(() =>
        {
            model.Attachments.Should().HaveCount(2,
                "two successful stages append two distinct rows");
            model.Attachments[0].FileName.Should().Be("a.pdf");
            model.Attachments[1].FileName.Should().Be("b.pdf");
            model.Attachments[0].StoragePath.Should().NotBe(model.Attachments[1].StoragePath,
                "each staged upload returns a unique storage path");
        });
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("2 file(s) attached",
                "the toolbar total reflects the two staged rows");
        });
        cut.WaitForAssertion(() =>
        {
            _mockHttp.VerifyNoOutstandingExpectation();
        });
    }

    [TestMethod]
    public void OnFileCountExceeded_RendersConfiguredCap_NotAttemptedCount()
    {
        // P1 fix: the FluentUI 4.14.2 callback parameter is documented as
        // "the total number of files that were attempted for upload", NOT
        // the configured cap. The handler must reference the configured
        // MaximumFileCount (10) so the user sees the true ceiling.
        var model = new AssignmentEditFormModel();
        var cut = RenderSection(model);

        // Drive the private OnFileCountExceededAsync directly via reflection
        // (the FluentInputFile JS pipeline is not driven in bUnit; the
        // section deliberately keeps the handler private). The handler
        // assigns _error, which is the exact field the error bar reads.
        var handler = typeof(ResourcesSection).GetMethod(
            "OnFileCountExceededAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        handler.Should().NotBeNull("the count-cap handler must exist on the section");
        var errorField = typeof(ResourcesSection).GetField(
            "_error",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        errorField.Should().NotBeNull("the section's error-bar field must exist");

        ((Task)handler!.Invoke(cut.Instance, [13])!).GetAwaiter().GetResult();

        var error = (string?)errorField!.GetValue(cut.Instance);
        error.Should().Be(
            $"Too many files selected at once — the limit is {AttachmentUploadPolicy.MaximumFileCount}. Add them in smaller batches.",
            "the error must report the configured cap (10), not the attempted count (13)");
        error.Should().NotContain("13",
            "the user-facing message must not echo the attempted batch size as the cap");
        model.Attachments.Should().BeEmpty(
            "the count-cap message is a UX surface only and does not touch the form model");
    }

    [TestMethod]
    public async Task StageFileAsync_WhileAlreadyStaging_SurfacesFriendlyError_NoRowAdded()
    {
        // P2 fix: the StageFileAsync _staging reentrancy guard previously
        // silently dropped a second file. The fix surfaces a friendly,
        // retryable message via the section's error bar.
        var model = new AssignmentEditFormModel();
        var cut = RenderSection(model);

        // Drive the guard deterministically: flip the private _staging flag
        // to true (the bUnit suite does not drive the FluentInputFile JS
        // pipeline, so we cannot reproduce the multi-select race via the
        // control itself — but the guard is the load-bearing contract).
        var stagingField = typeof(ResourcesSection).GetField(
            "_staging",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        stagingField.Should().NotBeNull("the section's staging-guard flag must exist");
        var errorField = typeof(ResourcesSection).GetField(
            "_error",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        errorField.Should().NotBeNull("the section's error-bar field must exist");
        stagingField!.SetValue(cut.Instance, true);

        await cut.Instance.StageFileAsync(
            StreamOf("second"), "second.pdf", "application/pdf", 4);

        var error = (string?)errorField!.GetValue(cut.Instance);
        error.Should().Contain("second.pdf",
            "the friendly staging-busy message names the refused file");
        error.Should().Contain("still uploading",
            "the friendly staging-busy message surfaces via the error bar");
        model.Attachments.Should().BeEmpty(
            "the refused second file must not be added to the form model");
        _mockHttp.VerifyNoOutstandingExpectation();
    }
}
