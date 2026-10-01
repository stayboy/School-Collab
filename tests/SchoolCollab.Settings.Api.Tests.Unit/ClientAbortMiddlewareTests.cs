using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.ServiceDefaults;

namespace SchoolCollab.Settings.Api.Tests.Unit;

/// <summary>
/// The startup <c>OperationCanceledException</c> raised in <c>ListRootCodedValuesHandler</c> is a
/// client disconnect surfacing as a fault (evidence and mechanism:
/// <c>documents/solution/adr-cross-module-calls.md</c>, the backfill step's shipped-deviation note).
/// <see cref="ClientAbortMiddleware"/> must absorb the shape a disconnect explains — and nothing else,
/// because an <see cref="OperationCanceledException"/> raised while the request is still alive is a
/// genuine cancellation bug that must keep failing loudly.
/// </summary>
[TestClass]
public class ClientAbortMiddlewareTests
{
    private static HttpContext ContextWith(bool aborted)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/coded-values/";
        context.RequestAborted = aborted ? new CancellationToken(canceled: true) : CancellationToken.None;
        return context;
    }

    [TestMethod]
    public async Task Cancellation_with_a_client_abort_is_absorbed()
    {
        var middleware = new ClientAbortMiddleware(
            _ => Task.FromException(new OperationCanceledException()),
            NullLogger<ClientAbortMiddleware>.Instance);

        var act = () => middleware.InvokeAsync(ContextWith(aborted: true));

        await act.Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task Timeout_cancellation_with_a_client_abort_is_absorbed()
    {
        var middleware = new ClientAbortMiddleware(
            _ => Task.FromException(new TaskCanceledException()),
            NullLogger<ClientAbortMiddleware>.Instance);

        var act = () => middleware.InvokeAsync(ContextWith(aborted: true));

        await act.Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task Cancellation_without_a_client_abort_still_propagates()
    {
        var middleware = new ClientAbortMiddleware(
            _ => Task.FromException(new OperationCanceledException()),
            NullLogger<ClientAbortMiddleware>.Instance);

        var act = () => middleware.InvokeAsync(ContextWith(aborted: false));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public async Task Other_exceptions_still_propagate_even_when_the_client_left()
    {
        var middleware = new ClientAbortMiddleware(
            _ => Task.FromException(new InvalidOperationException("boom")),
            NullLogger<ClientAbortMiddleware>.Instance);

        var act = () => middleware.InvokeAsync(ContextWith(aborted: true));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task A_clean_pipeline_runs_the_next_delegate_once()
    {
        var calls = 0;
        var middleware = new ClientAbortMiddleware(
            _ =>
            {
                calls++;
                return Task.CompletedTask;
            },
            NullLogger<ClientAbortMiddleware>.Instance);

        await middleware.InvokeAsync(ContextWith(aborted: false));

        calls.Should().Be(1);
    }
}
