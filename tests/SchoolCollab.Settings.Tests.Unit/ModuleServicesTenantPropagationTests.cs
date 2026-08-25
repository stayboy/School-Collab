using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Auth;
using SchoolCollab.Settings.Application;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Verifies that <see cref="ModuleServices.AddSettingsModule"/> wires
/// <see cref="TenantPropagationDelegatingHandler"/> onto the
/// <see cref="CodedValuesApiClient"/> HttpClient pipeline, so the enroll
/// dialog's grade + stream pickers carry the dev-selected tenant to the
/// settings-api via the x-tenant-id header (hybrid-tenant coded values are
/// otherwise resolved against the default tenant). Regression coverage for
/// docs/plans/2026-08-22-tenant-propagation-enroll-stream-investigation.md.
/// </summary>
[TestClass]
public class ModuleServicesTenantPropagationTests
{
    private sealed class StubSelection : IDevTenantSelection
    {
        private readonly Guid? _value;
        public StubSelection(Guid? value) => _value = value;
        public Task<Guid?> GetSelectedTenantIdAsync(CancellationToken ct = default) => Task.FromResult(_value);
        public Task SetSelectedTenantIdAsync(Guid? tenantId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private static (CodedValuesApiClient Client, CapturingHandler Handler) BuildClient(Guid? selectedTenant)
    {
        var capture = new CapturingHandler();

        var services = new ServiceCollection();
        services.AddLogging();
        services.TryAddSingleton<IDevTenantSelection>(new StubSelection(selectedTenant));
        services.AddSettingsModule();

        // Swap the primary handler so outgoing requests never leave the process
        // and can be inspected. The DI-registered TenantPropagationDelegatingHandler
        // stays in the pipeline above it.
        services.ConfigureAll<Microsoft.Extensions.Http.HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder =>
                builder.PrimaryHandler = capture));

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<CodedValuesApiClient>();
        return (client, capture);
    }

    [TestMethod]
    public async Task AddSettingsModule_CodedValuesApiClient_Carries_x_tenant_id_WhenTenantSelected()
    {
        var tenantId = Guid.NewGuid();
        var (client, capture) = BuildClient(tenantId);

        await client.GetChildrenByParentCodeAsync("GRADES");

        capture.LastRequest.Should().NotBeNull();
        capture.LastRequest!.Headers.Contains("x-tenant-id").Should()
            .BeTrue("AddSettingsModule must attach TenantPropagationDelegatingHandler to CodedValuesApiClient");
        capture.LastRequest.Headers.GetValues("x-tenant-id").Should()
            .ContainSingle().Which.Should().Be(tenantId.ToString());
    }

    [TestMethod]
    public async Task AddSettingsModule_CodedValuesApiClient_Omits_x_tenant_id_WhenNoTenantSelected()
    {
        var (client, capture) = BuildClient(null);

        await client.GetChildrenByParentCodeAsync("GRADES");

        capture.LastRequest.Should().NotBeNull();
        capture.LastRequest!.Headers.Contains("x-tenant-id").Should()
            .BeFalse("no dev tenant selected -> the propagation handler must not stamp a default/empty tenant");
    }
}
