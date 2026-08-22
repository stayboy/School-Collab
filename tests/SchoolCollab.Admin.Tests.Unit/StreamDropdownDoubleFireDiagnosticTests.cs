using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Constants;
using SchoolCollab.Admin.Shared.Services;
using System.Net;
using System.Net.Http.Json;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// DIAGNOSTIC tests for the reported issue: the EnrollStudentDialog stream
/// dropdown's <c>OnSelectedOptionChanged</c> fired twice, the second time
/// with a null <see cref="CodedValueDto"/>.
///
/// Root cause (fixed in CodedValueDropdown.razor): <c>LoadAsync</c> recorded
/// only the parent code into <c>_loadedParentCode</c>, while
/// <c>OnParametersSetAsync</c> compares against <c>LoadKey</c> which also
/// includes the <c>AttributeFilter</c>. For the filtered stream picker the
/// comparison could never be equal, so EVERY render took the reload branch,
/// clearing Items/SelectedOption mid-flight — and FluentUI's ListComponentBase
/// then raised a spurious SelectedOptionChanged(null) which flowed through the
/// binder and wiped _formModel.StreamCodedValueId.
///
/// These tests drive the shared CodedValueDropdown exactly as the dialog's
/// stream row does and record every binder callback via the :after hook, so
/// any spurious second (null) event shows up as an extra entry.
/// </summary>
[TestClass]
public class StreamDropdownDoubleFireDiagnosticTests : BunitContext
{
    private static readonly Guid Grade5CvId = Guid.Parse("ab50d479-9615-4dce-8fbc-1a163595411c");
    private static readonly Guid Grade7CvId = Guid.Parse("ab50d479-9615-4dce-8fbc-1a163595411d");
    private static readonly Guid Stream5AId = Guid.Parse("5e369893-3895-4dad-99ea-51a08979d5d6");
    private static readonly Guid Stream5BId = Guid.Parse("ce87c23f-e275-4790-a2f9-7fb90f6fffa4");

    public StreamDropdownDoubleFireDiagnosticTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public int StreamLookups;
        public bool DelayStreams; // hold the GRSTREAMS response to widen the race window

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            var query = request.RequestUri?.Query ?? "";

            if (path.StartsWith("/api/coded-values/by-parent") && query.Contains("parentCode=GRSTREAMS"))
            {
                Interlocked.Increment(ref StreamLookups);
                if (DelayStreams) { await Task.Delay(250, ct); }
                return query.Contains($"attributeValue={Grade5CvId}")
                    ? Json(HttpStatusCode.OK, new[]
                        {
                            new CodedValueDto(Stream5AId, "GRSTREAMS_5A", "Stream A", null,
                                null, "GRSTREAMS", false, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], [], 0, false, null, false),
                            new CodedValueDto(Stream5BId, "GRSTREAMS_5B", "Stream B", null,
                                null, "GRSTREAMS", false, 2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], [], 0, false, null, false),
                        })
                    : Json(HttpStatusCode.OK, Array.Empty<CodedValueDto>());
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            { Content = new StringContent($"Unhandled: {request.Method} {path}{query}") };
        }

        private static HttpResponseMessage Json<T>(HttpStatusCode status, T body) =>
            new(status) { Content = JsonContent.Create(body) };
    }

    // ── Probe host (pure C# — mirrors EnrollStudentDialog's stream row) ──

    /// <summary>
    /// Renders &lt;CodedValueDropdown&gt; exactly like the dialog's stream row
    /// (Parent=Streams + AttributeFilter + ShowEmptyOption) and records EVERY
    /// binder callback via `@bind-SelectedId:after`. A spurious second
    /// OnSelectedOptionChanged(null) inside CodedValueDropdown flows out as
    /// SelectedIdChanged(null) → binder → :after with the field already
    /// reset, so AfterValues gains an extra entry.
    /// </summary>
    private sealed class StreamPickerProbeHost : ComponentBase
    {
        [Parameter] public (string Key, string Value)? Filter { get; set; }

        private Guid? _boundId;

        /// <summary>Every post-bind observation, in order. One user pick = one entry.</summary>
        public List<Guid?> AfterValues { get; } = new();

        public int RenderCount { get; private set; }

        public Guid? BoundId => _boundId;

        /// <summary>bUnit 2.7.2 has no SetParam on IRenderedComponent — mutate
        /// our own parameter + re-render so the child sees a new filter.</summary>
        public Task ChangeFilterAsync((string Key, string Value)? filter)
        {
            Filter = filter;
            StateHasChanged();
            return Task.CompletedTask;
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            RenderCount++;
            builder.OpenComponent<CodedValueDropdown>(0);
            builder.AddAttribute(1, nameof(CodedValueDropdown.Parent), CodedValueParent.Streams);
            builder.AddAttribute(2, nameof(CodedValueDropdown.AttributeFilter), Filter);
            builder.AddAttribute(3, nameof(CodedValueDropdown.ShowEmptyOption), true);
            builder.AddAttribute(4, nameof(CodedValueDropdown.EmptyOptionText), "No stream");
            builder.AddAttribute(5, nameof(CodedValueDropdown.Placeholder), "Select stream…");
            builder.AddAttribute(6, nameof(CodedValueDropdown.SelectedId), _boundId);
            // Mirror @bind-SelectedId + :after semantics: assign the bound
            // field, run the :after hook, then re-render.
            builder.AddAttribute(7, nameof(CodedValueDropdown.SelectedIdChanged),
                EventCallback.Factory.Create<Guid?>(this, v =>
                {
                    _boundId = v;
                    AfterValues.Add(_boundId);
                    StateHasChanged();
                }));
            builder.CloseComponent();
        }
    }

    private (IRenderedComponent<StreamPickerProbeHost> cut, StubHandler handler) Mount((string, string)? filter)
    {
        var handler = new StubHandler();
        Services.AddSingleton(new CodedValuesApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") }));
        var cut = Render<StreamPickerProbeHost>(ps => ps.Add(p => p.Filter, ToTuple(filter)));
        return (cut, handler);
    }

    private static (string, string)? ToTuple((string, string)? f) => f;

    private FluentSelect<CodedValueDto> Select(IRenderedComponent<StreamPickerProbeHost> cut) =>
        cut.FindComponent<FluentSelect<CodedValueDto>>().Instance;

    private CodedValueDropdown Dropdown(IRenderedComponent<StreamPickerProbeHost> cut) =>
        cut.FindComponent<CodedValueDropdown>().Instance;

    private void Dump(string label, IReadOnlyList<Guid?> values)
    {
        Console.WriteLine($"{label}: [{string.Join(", ", values.Select(v => v?.ToString() ?? "null"))}]");
    }

    // ── Scenario A: one user pick → exactly one binder callback ──
    [TestMethod]
    public async Task SinglePick_RaisesExactlyOneCallback()
    {
        var (cut, _) = Mount(("gradeLevel", Grade5CvId.ToString()));
        cut.WaitForAssertion(() => Dropdown(cut).Items.Count.Should().Be(3)); // 2 streams + sentinel

        var picked = Dropdown(cut).Items.First(i => i.Id == Stream5AId);
        await cut.InvokeAsync(() => Select(cut).SelectedOptionChanged.InvokeAsync(picked));

        Dump("AfterValues", cut.Instance.AfterValues);
        cut.Instance.AfterValues.Should().ContainSingle("one user pick = exactly one binder callback");
        cut.Instance.BoundId.Should().Be(Stream5AId);
    }

    // ── Scenario A2: the ORIGINAL bug — re-renders after a pick must not reload ──
    // With the old _loadedParentCode-vs-LoadKey mismatch, EVERY parent
    // re-render (including the one the pick itself triggers) took the reload
    // branch, and each reload raised a spurious null callback right after the
    // pick. Assert the pick survives several unrelated parent re-renders.
    [TestMethod]
    public async Task PickStream_ThenUnrelatedParentRerenders_SelectionSurvives()
    {
        var (cut, handler) = Mount(("gradeLevel", Grade5CvId.ToString()));
        var lookupsAfterInitialLoad = 0;
        cut.WaitForAssertion(() =>
        {
            Dropdown(cut).Items.Count.Should().Be(3);
            lookupsAfterInitialLoad = handler.StreamLookups;
        });

        var picked = Dropdown(cut).Items.First(i => i.Id == Stream5AId);
        await cut.InvokeAsync(() => Select(cut).SelectedOptionChanged.InvokeAsync(picked));

        for (var i = 0; i < 3; i++)
        {
            await cut.InvokeAsync(() => cut.Render());   // unrelated parent re-renders
        }

        Dump("AfterValues", cut.Instance.AfterValues);
        cut.Instance.AfterValues.Should().ContainSingle(
            "re-renders with an unchanged load key must not reload or raise selection events");
        cut.Instance.BoundId.Should().Be(Stream5AId, "the picked stream must survive sibling re-renders");
        handler.StreamLookups.Should().Be(lookupsAfterInitialLoad,
            "no additional GRSTREAMS lookups may be issued when nothing relevant changed");
    }

    // ── Scenario B: pick a stream, then change the filter (grade change) ──
    [TestMethod]
    public async Task PickStream_ThenChangeFilter_RecordsEveryCallback()
    {
        var (cut, _) = Mount(("gradeLevel", Grade5CvId.ToString()));
        cut.WaitForAssertion(() => Dropdown(cut).Items.Count.Should().Be(3));

        var picked = Dropdown(cut).Items.First(i => i.Id == Stream5AId);
        await cut.InvokeAsync(() => Select(cut).SelectedOptionChanged.InvokeAsync(picked));
        cut.Instance.BoundId.Should().Be(Stream5AId);

        // Simulate the grade change: new filter value → reload.
        await cut.InvokeAsync(() => cut.Instance.ChangeFilterAsync(("gradeLevel", Grade7CvId.ToString())));
        cut.WaitForAssertion(() => Dropdown(cut).Items.Count.Should().Be(1)); // sentinel only

        Dump("AfterValues", cut.Instance.AfterValues);
        // Changing the grade invalidates the previously-picked stream (it is
        // filtered against the OLD grade's coded value), so exactly ONE clear
        // callback is expected — and it must not loop: subsequent renders are
        // stable because FluentUI resets its internal Value along with the
        // event, and the wrapper records the new full load key.
        cut.Instance.AfterValues.Should().HaveCount(2);
        cut.Instance.AfterValues[0].Should().Be(Stream5AId);
        cut.Instance.AfterValues[1].Should().BeNull(
            "the stale stream from the previous grade must be cleared exactly once on reload");
        cut.Instance.BoundId.Should().BeNull("the invalidated stream id must be cleared from the bound field");

        // Quiesce: further renders must NOT raise any more callbacks.
        var countAfterReload = cut.Instance.AfterValues.Count;
        for (var i = 0; i < 3; i++)
        {
            await cut.InvokeAsync(() => cut.Render());
        }
        cut.Instance.AfterValues.Should().HaveCount(countAfterReload,
            "after the reload settles, no further selection events may fire");
    }

    // ── Scenario C: forced re-render while the reload is in flight ──
    [TestMethod]
    public async Task PickStream_ThenReRenderDuringReload_RecordsEveryCallback()
    {
        var (cut, handler) = Mount(("gradeLevel", Grade5CvId.ToString()));
        cut.WaitForAssertion(() => Dropdown(cut).Items.Count.Should().Be(3));

        var picked = Dropdown(cut).Items.First(i => i.Id == Stream5AId);
        await cut.InvokeAsync(() => Select(cut).SelectedOptionChanged.InvokeAsync(picked));
        cut.Instance.BoundId.Should().Be(Stream5AId);

        // Widen the in-flight window and slam a parent re-render into it.
        handler.DelayStreams = true;
        var reload = cut.InvokeAsync(() => cut.Instance.ChangeFilterAsync(("gradeLevel", Grade7CvId.ToString())));
        await Task.Delay(50);                       // land inside the HTTP await
        cut.Render();                               // full-tree re-render mid-load
        await reload;
        cut.WaitForAssertion(() => Dropdown(cut).Items.Count.Should().Be(1));

        Dump("AfterValues", cut.Instance.AfterValues);
        cut.Instance.AfterValues.Should().HaveCount(2,
            "pick + one clear when the stale selection no longer matches the new filter — never a loop");
        cut.Instance.AfterValues[1].Should().BeNull();
        cut.Instance.BoundId.Should().BeNull();

        var countAfterReload = cut.Instance.AfterValues.Count;
        for (var i = 0; i < 3; i++)
        {
            await cut.InvokeAsync(() => cut.Render());
        }
        cut.Instance.AfterValues.Should().HaveCount(countAfterReload,
            "after everything quiesces, no further selection events may fire");
    }
}
