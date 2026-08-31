using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Services;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Unit tests for the single-GET lookups on <see cref="StudentsApiClient"/>
/// that previously dropped the response body on non-NotFound failures
/// (follow-ups Round 2, F5). Pins the read-body-on-failure contract: a 404
/// still maps to null (callers treat it as "not found"), but any other
/// non-success throws an <see cref="HttpRequestException"/> whose message
/// carries BOTH the status code and the server body — the same shape as the
/// already-fixed write-paths (EnrollStudentAsync) and the period-family
/// lookups (GetActiveAcademicYearAsync / GetActiveSubPeriodAsync).
/// </summary>
[TestClass]
public class StudentsApiClientLookupErrorBodyTests
{
    private sealed class StubHttpHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private static StudentsApiClient CreateClient(HttpStatusCode status, string body)
    {
        var handler = new StubHttpHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        return new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, new CodedValuesApiClient(http));
    }

    [TestMethod]
    public async Task GetPeriodByIdAsync_ServerError_ThrowsWithStatusAndBody()
    {
        var client = CreateClient(HttpStatusCode.InternalServerError, "{\"message\":\"boom\"}");

        var act = () => client.GetPeriodByIdAsync(Guid.NewGuid());

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.Message.Should().Contain("500", "the message names the numeric status");
        ex.Which.Message.Should().Contain("boom", "the message carries the server body");
        ex.Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [TestMethod]
    public async Task GetPeriodByIdAsync_NotFound_ReturnsNull()
    {
        var client = CreateClient(HttpStatusCode.NotFound, "");

        var result = await client.GetPeriodByIdAsync(Guid.NewGuid());

        result.Should().BeNull("a 404 maps to 'period not found', not an exception (Edit.razor load path)");
    }

    [TestMethod]
    public async Task GetStudentByIdAsync_ServerError_ThrowsWithStatusAndBody()
    {
        var client = CreateClient(HttpStatusCode.InternalServerError, "{\"message\":\"boom\"}");

        var act = () => client.GetStudentByIdAsync(Guid.NewGuid());

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.Message.Should().Contain("500");
        ex.Which.Message.Should().Contain("boom");
        ex.Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [TestMethod]
    public async Task GetStudentByNumberAsync_ServerError_ThrowsWithStatusAndBody()
    {
        var client = CreateClient(HttpStatusCode.InternalServerError, "{\"message\":\"boom\"}");

        var act = () => client.GetStudentByNumberAsync("S-100");

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.Message.Should().Contain("500");
        ex.Which.Message.Should().Contain("boom");
        ex.Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }
}
