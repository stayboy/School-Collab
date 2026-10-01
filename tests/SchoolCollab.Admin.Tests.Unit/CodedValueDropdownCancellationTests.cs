using System.Net;
using System.Net.Http;
using System.Text;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Constants;
using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The silent-failure half of the admin-page-load abort hunt
/// (<c>documents/solution/client-abort-not-a-fault.md</c>): the request abort itself is a
/// routine client disconnect, but <c>CodedValueDropdown</c> used to swallow <b>every</b>
/// <see cref="OperationCanceledException"/>, so a cancellation that did *not* come from its
/// own load token (e.g. the HTTP transport timing out) left the control disabled on its
/// placeholder with no error and no explanation. These tests pin both halves of the guard:
/// a foreign cancellation is surfaced, our own superseded load stays silent.
/// </summary>
[TestClass]
public class CodedValueDropdownCancellationTests : BunitContext
{
    private const string OneStreamJson =
        """[{"id":"11111111-1111-1111-1111-111111111111","code":"S_A","name":"Stream A"}]""";

    private sealed class Handler(Func<CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => send(ct);
    }

    public CodedValueDropdownCancellationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private void Register(Func<CancellationToken, Task<HttpResponseMessage>> send)
    {
        var http = new HttpClient(new Handler(send)) { BaseAddress = new Uri("http://localhost/") };
        Services.AddSingleton(new CodedValuesApiClient(http));
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [TestMethod]
    public void Cancellation_that_is_not_this_loads_own_token_is_surfaced()
    {
        // A transport timeout surfaces as TaskCanceledException, but this component's own
        // load token is untouched — so the dropdown must tell the user, not sit disabled.
        Register(_ => Task.FromException<HttpResponseMessage>(new TaskCanceledException()));

        var cut = Render<CodedValueDropdown>(p => p.Add(x => x.Parent, CodedValueParent.Streams));

        cut.WaitForAssertion(
            () => cut.Markup.Should().Contain("Unable to load options",
                "a cancellation the component did not ask for is a load failure, not a silent no-op"));
    }

    [TestMethod]
    public async Task A_superseded_load_stays_silent_and_the_newer_load_wins()
    {
        var calls = 0;
        Register(async ct =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                // The first load hangs until its own token is cancelled — the real transport
                // behaviour when the component supersedes a load it no longer needs.
                await Task.Delay(Timeout.Infinite, ct);
            }

            return Json(OneStreamJson);
        });

        var cut = Render<CodedValueDropdown>(p => p.Add(x => x.Parent, CodedValueParent.Streams));

        await cut.InvokeAsync(() => cut.Instance.RefreshAsync());

        cut.WaitForAssertion(() => cut.Instance.Items.Should().NotBeEmpty(
            "the newer load must still populate the dropdown"));
        cut.Markup.Should().NotContain("Unable to load options",
            "a load that was superseded by our own newer load is obsolete, not a failure");
    }
}
