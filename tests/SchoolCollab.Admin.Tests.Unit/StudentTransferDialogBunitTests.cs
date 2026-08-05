using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for <see cref="StudentTransferDialog"/> — the promote/demote
/// dialog that moves a student to a different grade level. Mirrors the
/// <see cref="EnrollStudentDialogBunitTests"/> hosting pattern (real
/// <see cref="FluentDialogProvider"/> + <see cref="IDialogService"/> + a stub
/// <see cref="HttpMessageHandler"/>).
///
/// <para>The headline test is the regression for the "clicking Transfer does
/// nothing" silent-no-op bug: <see cref="StudentTransferDialog"/>'s
/// <c>SubmitAsync</c> used to do <c>if (_selectedGrade is null) return null;</c>
/// with no <c>Error</c> set. The <c>EditForm</c> has no
/// <c>DataAnnotationsValidator</c> (the <c>FluentSelect Required</c> is
/// display-only), so Blazor validation did not block submit when the grade was
/// unset — the dialog silently stayed open with zero feedback. The fix surfaces
/// "Select a grade to transfer to." as a visible message bar instead.</para>
/// </summary>
[TestClass]
public class StudentTransferDialogBunitTests : BunitContext
{
    private static readonly Guid StudentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid EnrollmentId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid CurrentGradeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OtherGradeId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public StudentTransferDialogBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed class TestAuthProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    /// <summary>
    /// Stub handler backing <see cref="StudentsApiClient"/>. Defaults to the
    /// transfer-ready state: one Active enrollment (in CurrentGrade) + two
    /// grade levels (current + other) so the dialog's grade-options guard
    /// passes and the form renders with the grade FluentSelect unselected.
    /// </summary>
    private sealed class TransferHttpHandler : HttpMessageHandler
    {
        public int TransferPostCount;
        public bool ReturnNoActiveEnrollment; // return an enrollment list with no Active/unclosed row

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(Respond(request));

        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            // GET /students/enrollments/by-student/{studentId} — one Active enrollment
            // (or, when ReturnNoActiveEnrollment is set, a single Withdrawn/closed row
            // so the dialog's "no active enrollment to transfer" guard fires).
            if (path.StartsWith("/students/enrollments/by-student/", StringComparison.OrdinalIgnoreCase)
                && HttpMethod.Get.Equals(request.Method))
            {
                if (ReturnNoActiveEnrollment)
                {
                    return Json(HttpStatusCode.OK, new StudentEnrollmentDto[]
                    {
                        new(EnrollmentId, StudentId, Guid.NewGuid(), CurrentGradeId, null,
                            new DateOnly(2025, 9, 1), new DateOnly(2025, 10, 1), "Withdrawn",
                            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                    });
                }
                return Json(HttpStatusCode.OK, new StudentEnrollmentDto[]
                {
                    new(EnrollmentId, StudentId, Guid.NewGuid(), CurrentGradeId, null,
                        new DateOnly(2025, 9, 1), null, "Active",
                        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                });
            }

            // GET /students/grade-levels — current + other grade (so gradeOptions = [other], Count > 0).
            if (path.Contains("/students/grade-levels", StringComparison.OrdinalIgnoreCase)
                && HttpMethod.Get.Equals(request.Method))
            {
                return Json(HttpStatusCode.OK, new GradeLevelDto[]
                {
                    new(CurrentGradeId, Guid.NewGuid(), 7, "Grade 7", 7, 0, 0,
                        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
                    new(OtherGradeId, Guid.NewGuid(), 8, "Grade 8", 8, 0, 0,
                        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                });
            }

            // POST /students/enrollments/{id}/transfer — the actual transfer.
            if (path.Contains("/transfer", StringComparison.OrdinalIgnoreCase)
                && HttpMethod.Post.Equals(request.Method))
            {
                TransferPostCount++;
                return Json(HttpStatusCode.OK, new { Id = Guid.NewGuid() });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            { Content = new StringContent($"Unhandled request: {request.Method} {path}") };
        }

        private static HttpResponseMessage Json<T>(HttpStatusCode status, T body) =>
            new(status) { Content = JsonContent.Create(body) };
    }

    private TransferHttpHandler RegisterServices()
    {
        var handler = new TransferHttpHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthProvider());
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        Services.AddSingleton(new StudentsApiClient(
            http, NullLogger<StudentsApiClient>.Instance, codedValuesClient));
        return handler;
    }

    private (IRenderedComponent<FluentDialogProvider> cut, Task<StudentTransferResult?> task)
        OpenDialog(StudentTransferModel model)
    {
        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<StudentTransferDialog, StudentTransferModel, StudentTransferResult>(
            model, title: "Transfer student", size: DialogSize.Medium);
        return (cut, task);
    }

    [TestMethod]
    public void Form_Renders_WhenActiveEnrollmentAndGradeOptionsExist()
    {
        // Sanity: the dialog renders the transfer form (not the "no active
        // enrollment" / "no other grades" warnings) when the student has an
        // Active enrollment and at least one other grade to move to.
        RegisterServices();
        var (cut, _) = OpenDialog(new StudentTransferModel(StudentId));

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull(
            "the transfer form renders when there is an Active enrollment + other grade options"));
        cut.Markup.Should().Contain("Transfer to grade", "the grade FluentSelect is rendered");
        cut.Markup.Should().Contain("Current grade", "the read-only current-grade row is rendered");
    }

    [TestMethod]
    public void SubmitWithNoGrade_ShowsSelectGradeError_InsteadOfSilentNoOp()
    {
        // Regression for the reported "clicking Transfer does nothing" bug.
        // The grade FluentSelect has no default selection, so _selectedGrade
        // starts null. The EditForm has no DataAnnotationsValidator (the
        // FluentSelect Required is display-only), so Blazor validation does NOT
        // block submit. SubmitAsync's null-guard MUST surface a visible error
        // instead of silently returning null (which left the dialog open with
        // zero feedback).
        var handler = RegisterServices();
        var (cut, task) = OpenDialog(new StudentTransferModel(StudentId));

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Select a grade to transfer to",
            "a missing grade must surface a visible error, not a silent no-op — the bug was the silent null-return with no Error set"));
        task.IsCompleted.Should().BeFalse(
            "the dialog stays open so the user can pick a grade and retry; it does not close on a rejected submit");
        handler.TransferPostCount.Should().Be(0,
            "no transfer POST must be sent when the grade is not selected");
    }

    [TestMethod]
    public void NoActiveEnrollment_ShowsWarning_InsteadOfForm()
    {
        // When the student has no Active enrollment, the dialog shows the
        // "no active enrollment to transfer" warning and does NOT render the
        // form (there is nothing to transfer). This is the existing guard; pin
        // it so a regression that renders an empty form is caught.
        var handler = RegisterServices();
        // Override: return an enrollment list with no Active/unclosed enrollment.
        handler.ReturnNoActiveEnrollment = true;
        var (cut, _) = OpenDialog(new StudentTransferModel(StudentId));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("no active enrollment to transfer",
                "the warning shows when the student has no Active enrollment");
            cut.FindAll("form").Count.Should().Be(0,
                "the transfer form must NOT render when there is no enrollment to transfer");
        });
    }
}