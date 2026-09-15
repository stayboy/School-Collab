using System;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Moq;
using SchoolCollab.Core.Auth;
using SchoolCollab.Families.Services;

namespace SchoolCollab.Families.Tests.Unit;

/// <summary>
/// F1 (slice 2b) — module wiring. The Families host's HTTP client must target the
/// Assignments API through Aspire service discovery. JS never runs here — this is a
/// pure DI/base-address assert against the registered typed client.
/// </summary>
[TestClass]
public class FamiliesModuleWiringTests
{
    [TestMethod]
    public void AddFamiliesModule_ConfiguresAssignmentsApiBaseAddress()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IDevTenantSelection>());
        services.AddFamiliesModule();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        // A typed client is registered via AddHttpClient<TClient>, whose underlying
        // named client is keyed by typeof(TClient).Name (not FullName). Use Name so this
        // stays correct now that FamiliesApiClient lives in a namespace.
        var client = factory.CreateClient(typeof(FamiliesApiClient).Name!);

        client.BaseAddress.Should().Be(new Uri("https+http://assignments-api"));
    }
}
