using Bunit;
using Microsoft.AspNetCore.Components;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Constants;
using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Isolation probe: does <c>@bind-SelectedId</c> on a bare CodedValueDropdown
/// write through to the bound field at all? Splits the stream-binding bug
/// between "the widget's bind mechanics" and "something in the enroll dialog".
/// </summary>
[TestClass]
public class CodedValueDropdownBindIsolationTests : BunitContext
{
    private sealed class Holder { public Guid? Value { get; set; } }

    public CodedValueDropdownBindIsolationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        var http = new System.Net.Http.HttpClient(new EmptyJsonHandler())
        { BaseAddress = new Uri("http://localhost/") };
        Services.AddSingleton(new CodedValuesApiClient(http));
    }

    private sealed class EmptyJsonHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"code\":\"S_A\",\"name\":\"Stream A\"}]",
                    System.Text.Encoding.UTF8, "application/json")
            });
    }

    [TestMethod]
    public async Task BindSelectedId_WritesThrough_OnInvoke()
    {
        var holder = new Holder();
        var cut = Render<CodedValueDropdown>(p => p
            .Add(x => x.Parent, CodedValueParent.Streams)
            .Add(x => x.SelectedId, holder.Value)
            .Add(x => x.SelectedIdChanged, EventCallback.Factory.Create<Guid?>(this,
                v => holder.Value = v)));

        cut.WaitForState(() => cut.Instance.Items.Count > 0);

        await cut.InvokeAsync(() => cut.Instance.SelectedIdChanged.InvokeAsync(
            Guid.Parse("11111111-1111-1111-1111-111111111111")));

        holder.Value.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "invoking SelectedIdChanged must write through to the bound field");
    }
}
