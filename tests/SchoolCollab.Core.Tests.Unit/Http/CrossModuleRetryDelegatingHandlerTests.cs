using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Moq.Protected;
using SchoolCollab.Core.Http;

namespace SchoolCollab.Core.Tests.Unit.Http;

[TestClass]
public class CrossModuleRetryDelegatingHandlerTests
{
    private static CrossModuleRetryDelegatingHandler CreateHandler(
        Func<int, HttpResponseMessage> behavior,
        int maxRetries = 1)
    {
        var inner = new Mock<HttpMessageHandler>();
        var callCount = 0;
        inner.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage _, CancellationToken __) =>
            {
                callCount++;
                return behavior(callCount);
            });

        var options = Options.Create(new CrossModuleHttpClientOptions { MaxRetries = maxRetries });
        return new CrossModuleRetryDelegatingHandler(options, NullLogger<CrossModuleRetryDelegatingHandler>.Instance)
        {
            InnerHandler = inner.Object
        };
    }

    [TestMethod]
    public async Task ObjectDisposedException_RetriesOnceThenSucceeds()
    {
        var handler = CreateHandler(
            n => n == 1
                ? throw new ObjectDisposedException("System.Net.Sockets.NetworkStream")
                : new HttpResponseMessage(HttpStatusCode.OK),
            maxRetries: 1);

        using var client = new HttpClient(handler);
        var response = await client.GetAsync("http://settings-api/api/coded-values/123");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task NetworkStreamIOException_Retries()
    {
        var io = new IOException(
            "Unable to read data from the transport connection: System.Net.Sockets.NetworkStream.");
        var handler = CreateHandler(
            n => n == 1 ? throw io : new HttpResponseMessage(HttpStatusCode.OK),
            maxRetries: 1);

        using var client = new HttpClient(handler);
        var response = await client.GetAsync("http://settings-api/api/coded-values/123");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task HttpRequestException_Retries()
    {
        var handler = CreateHandler(
            n => n == 1
                ? throw new HttpRequestException("Connection refused")
                : new HttpResponseMessage(HttpStatusCode.OK),
            maxRetries: 1);

        using var client = new HttpClient(handler);
        var response = await client.GetAsync("http://settings-api/api/coded-values/123");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task InnerRetryableException_Retries()
    {
        var outer = new InvalidOperationException(
            "outer",
            new ObjectDisposedException("System.Net.Sockets.NetworkStream"));
        var handler = CreateHandler(
            n => n == 1 ? throw outer : new HttpResponseMessage(HttpStatusCode.OK),
            maxRetries: 1);

        using var client = new HttpClient(handler);
        var response = await client.GetAsync("http://settings-api/api/coded-values/123");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task StatusCode500_Retries()
    {
        var handler = CreateHandler(
            n => n == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.OK),
            maxRetries: 1);

        using var client = new HttpClient(handler);
        var response = await client.GetAsync("http://settings-api/api/coded-values/123");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task StatusCode408_Retries()
    {
        var handler = CreateHandler(
            n => n == 1
                ? new HttpResponseMessage(HttpStatusCode.RequestTimeout)
                : new HttpResponseMessage(HttpStatusCode.OK),
            maxRetries: 1);

        using var client = new HttpClient(handler);
        var response = await client.GetAsync("http://settings-api/api/coded-values/123");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task NonRetryableException_DoesNotRetry()
    {
        var handler = CreateHandler(_ => throw new InvalidOperationException("boom"));

        using var client = new HttpClient(handler);
        Func<Task> act = async () => await client.GetAsync("http://settings-api/api/coded-values/123");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task StatusCode404_DoesNotRetry()
    {
        var handler = CreateHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        using var client = new HttpClient(handler);
        var response = await client.GetAsync("http://settings-api/api/coded-values/123");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task ExhaustedRetries_ThrowsLastException()
    {
        var ex = new ObjectDisposedException("System.Net.Sockets.NetworkStream");
        var handler = CreateHandler(_ => throw ex, maxRetries: 1);

        using var client = new HttpClient(handler);
        Func<Task> act = async () => await client.GetAsync("http://settings-api/api/coded-values/123");

        (await act.Should().ThrowAsync<ObjectDisposedException>())
            .Which.Message.Should().Contain("NetworkStream");
    }
}
