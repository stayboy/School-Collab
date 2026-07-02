using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Admin.Tests.Unit;

[TestClass]
public class ConfigFlagsApiClientTests
{
    [TestMethod]
    public async Task ListAsync_DeserializesServerDto_WithEnumKind_AsBoolean()
    {
        var serverJson = JsonSerializer.Serialize(new[]
        {
            new
            {
                Id = Guid.NewGuid(),
                Key = "FEATURE:ENABLECODEDVALUESAICHAT",
                Name = "AI Chat",
                Description = (string?)null,
                Kind = 0, // FlagKindDto.Boolean as JSON number
                IsEnabled = true,
                IsArchived = false,
                IsDeleted = false,
                OverrideCount = 0,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        });

        var handler = new TestHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(serverJson)
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var api = new ConfigFlagsApiClient(client);

        var flags = await api.ListAsync(null, false);

        Assert.AreEqual(1, flags.Length);
        Assert.AreEqual("FEATURE:ENABLECODEDVALUESAICHAT", flags[0].Key);
        Assert.AreEqual(FlagKindDto.Boolean, flags[0].Kind);
        Assert.IsTrue(flags[0].IsEnabled);
    }

    [TestMethod]
    public async Task ListAsync_PropagatesHttpError_InsteadOfSwallowingAsCancellation()
    {
        var handler = new TestHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var api = new ConfigFlagsApiClient(client);

        try
        {
            await api.ListAsync(null, false);
            Assert.Fail("Expected HttpRequestException");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.InternalServerError)
        {
            // expected
        }
    }

    [TestMethod]
    public async Task GetAsync_ReturnsNull_ForNotFound()
    {
        var handler = new TestHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var api = new ConfigFlagsApiClient(client);

        var flag = await api.GetAsync("FEATURE:MISSING");

        Assert.IsNull(flag);
    }

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public TestHttpMessageHandler(HttpResponseMessage response) => _response = response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_response);
    }
}
