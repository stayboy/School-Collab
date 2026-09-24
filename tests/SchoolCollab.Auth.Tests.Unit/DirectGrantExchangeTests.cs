using System.Net;
using System.Web;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Moq.Protected;
using SchoolCollab.Auth.Options;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Tests.Unit;

/// <summary>
/// Direct-Grant exchange coverage (plan step 6): the happy path, every failure
/// taxonomy branch, and the outgoing request shape — the repo's MockHttp matcher
/// pitfall is handled by asserting the request method explicitly (it must be POST)
/// and by reading the form body inside the mocked SendAsync.
/// </summary>
[TestClass]
public class DirectGrantExchangeTests
{
    private static readonly Uri TokenEndpoint =
        new("http://keycloak:8080/realms/school-collab/protocol/openid-connect/token");

    private static readonly AuthServiceOptions Options = new()
    {
        Keycloak = new AuthServiceOptions.KeycloakOptions
        {
            Authority = "http://keycloak:8080/realms/school-collab",
            ClientId = "school-collab-client",
            ClientSecret = "a-client-secret",
            ServiceAccountClientSecret = "a-service-account-secret",
        },
    };

    private static Mock<HttpMessageHandler> RespondingHandler(
        HttpStatusCode status,
        string body,
        Action<HttpRequestMessage, string>? observe = null)
    {
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send =
            async (request, _) =>
            {
                var requestBody = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(CancellationToken.None);
                observe?.Invoke(request, requestBody);
                return new HttpResponseMessage(status) { Content = new StringContent(body) };
            };

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(send);
        return handler;
    }

    private static Mock<HttpMessageHandler> ThrowingHandler(Exception exception)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(exception);
        return handler;
    }

    private static DirectGrantExchanger Exchange(Mock<HttpMessageHandler> handler)
        => new(new HttpClient(handler.Object), Microsoft.Extensions.Options.Options.Create(Options));

    [TestMethod]
    public async Task Exchange_Success_ReturnsTheTokenSet()
    {
        var handler = RespondingHandler(HttpStatusCode.OK, """
            {"access_token":"at-1","refresh_token":"rt-1","id_token":"it-1","expires_in":300,"token_type":"Bearer"}
            """);

        var result = await Exchange(handler).ExchangeAsync("dev-teacher", "dev-only-password");

        result.Status.Should().Be(DirectGrantStatus.Success);
        result.IsSuccess.Should().BeTrue();
        result.AccessToken.Should().Be("at-1");
        result.RefreshToken.Should().Be("rt-1");
        result.IdToken.Should().Be("it-1");
        result.ExpiresInSeconds.Should().Be(300);
    }

    [TestMethod]
    public async Task Exchange_InvalidCredentials_OnUnauthorized()
    {
        var handler = RespondingHandler(HttpStatusCode.Unauthorized, """
            {"error":"invalid_grant","error_description":"Invalid user credentials"}
            """);

        var result = await Exchange(handler).ExchangeAsync("dev-teacher", "wrong-password");

        result.Status.Should().Be(DirectGrantStatus.InvalidCredentials);
        result.IsSuccess.Should().BeFalse();
        result.AccessToken.Should().BeNull();
    }

    [TestMethod]
    public async Task Exchange_DisabledUser_OnCanonicalKeycloakMessage()
    {
        // The REAL Keycloak 26.4 disabled-account error_description (accountDisabledMessage).
        // Note the intervening "is ": the string reads "Account IS disabled, ...", so a naive
        // matcher looking for "account disabled" misses it and would classify this as
        // InvalidCredentials — exactly the classification bug this fixture exists to pin.
        var handler = RespondingHandler(HttpStatusCode.Unauthorized, """
            {"error":"invalid_grant","error_description":"Account is disabled, contact your administrator."}
            """);

        var result = await Exchange(handler).ExchangeAsync("dev-teacher", "dev-only-password");

        result.Status.Should().Be(DirectGrantStatus.DisabledUser);
        result.IsSuccess.Should().BeFalse();
    }

    [TestMethod]
    public async Task Exchange_BruteForceLockout_StaysInvalidCredentials()
    {
        // accountTemporarilyDisabledMessage = "Invalid username or password." in Keycloak 26.4:
        // the brute-force lockout is DELIBERATELY indistinguishable from bad credentials to avoid
        // user enumeration, so it must keep classifying as InvalidCredentials and never become
        // DisabledUser. Mapping it any other way would leak lockout state through the portal.
        var handler = RespondingHandler(HttpStatusCode.Unauthorized, """
            {"error":"invalid_grant","error_description":"Invalid username or password."}
            """);

        var result = await Exchange(handler).ExchangeAsync("dev-teacher", "dev-only-password");

        result.Status.Should().Be(DirectGrantStatus.InvalidCredentials);
        result.IsSuccess.Should().BeFalse();
    }

    [TestMethod]
    public async Task Exchange_KeycloakUnreachable_OnHttpRequestException()
    {
        var handler = ThrowingHandler(new HttpRequestException("connection refused"));

        var result = await Exchange(handler).ExchangeAsync("dev-teacher", "dev-only-password");

        result.Status.Should().Be(DirectGrantStatus.KeycloakUnreachable);
        result.IsSuccess.Should().BeFalse();
    }

    [TestMethod]
    public async Task Exchange_CallerCancellation_IsRethrownNotSwallowed()
    {
        var handler = ThrowingHandler(new OperationCanceledException("cancelled"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var exchange = Exchange(handler);
        var act = () => exchange.ExchangeAsync("dev-teacher", "dev-only-password", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "caller cancellation is not a Keycloak failure — it must propagate, never be swallowed into a Status result.");
    }

    [TestMethod]
    public async Task Exchange_HttpClientTimeout_MapsToUnreachable()
    {
        // The same OperationCanceledException family WITHOUT caller cancellation is an
        // HttpClient-side timeout: it must map to KeycloakUnreachable, never to a credential verdict.
        var handler = ThrowingHandler(new OperationCanceledException("timeout"));

        var result = await Exchange(handler).ExchangeAsync("dev-teacher", "dev-only-password");

        result.Status.Should().Be(DirectGrantStatus.KeycloakUnreachable);
        result.IsSuccess.Should().BeFalse();
    }

    [TestMethod]
    public async Task Exchange_KeycloakUnreachable_OnServerError()
    {
        var handler = RespondingHandler(HttpStatusCode.InternalServerError, "oops");

        var result = await Exchange(handler).ExchangeAsync("dev-teacher", "dev-only-password");

        result.Status.Should().Be(DirectGrantStatus.KeycloakUnreachable,
            "a 5xx is an endpoint outage, not a credential verdict.");
    }

    [TestMethod]
    public async Task Exchange_UsesExplicitPost_WithConfidentialClientCredentials()
    {
        HttpRequestMessage? observedRequest = null;
        var observedBody = string.Empty;
        var handler = RespondingHandler(
            HttpStatusCode.OK,
            """{"access_token":"at","expires_in":300}""",
            (request, body) => { observedRequest = request; observedBody = body; });

        await Exchange(handler).ExchangeAsync("dev-teacher", "dev-only-password");

        observedRequest.Should().NotBeNull();
        observedRequest!.Method.Should().Be(HttpMethod.Post,
            "the token endpoint is a POST and the repo's MockHttp matcher will not infer the verb; the explicit method is asserted here.");
        observedRequest.RequestUri.Should().Be(TokenEndpoint);

        var form = HttpUtility.ParseQueryString(observedBody);
        form["grant_type"].Should().Be("password");
        form["client_id"].Should().Be("school-collab-client");
        form["client_secret"].Should().Be("a-client-secret");
        form["username"].Should().Be("dev-teacher");
        form["password"].Should().Be("dev-only-password");
    }
}
