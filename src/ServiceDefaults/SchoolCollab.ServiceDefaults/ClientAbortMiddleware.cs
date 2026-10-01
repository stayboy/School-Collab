using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SchoolCollab.ServiceDefaults;

/// <summary>
/// Keeps a client disconnect from being reported as a server fault.
/// </summary>
/// <remarks>
/// <para>
/// When a caller goes away mid-request, ASP.NET Core cancels <see cref="HttpContext.RequestAborted"/>
/// and anything awaiting that token throws <see cref="OperationCanceledException"/>. With
/// <c>HybridCache</c> stampede protection that is easy to hit: the first caller runs the factory and
/// every other caller <i>joins</i> it, awaiting with its <b>own</b> token — so a joiner that
/// disconnects throws out of an endpoint whose query never ran. Observed shape:
/// <c>DefaultHybridCache.StampedeState.JoinAsync</c> → <c>ThrowIfCancellationRequested</c> →
/// <c>ListRootCodedValuesHandler.HandleAsync</c>, surfacing through the developer exception page as an
/// unhandled exception and therefore logged at Error.
/// </para>
/// <para>
/// That is not a fault: the client is gone, nothing was computed, and no response can be delivered.
/// This middleware absorbs exactly that shape — an <see cref="OperationCanceledException"/> raised
/// while <see cref="HttpContext.RequestAborted"/> is cancelled. An
/// <see cref="OperationCanceledException"/> raised while the request is still alive (a genuine
/// cancellation bug: a disposed, wrongly-scoped or prematurely-cancelled token) propagates unchanged,
/// as does every other exception.
/// </para>
/// </remarks>
public sealed class ClientAbortMiddleware(RequestDelegate next, ILogger<ClientAbortMiddleware> logger)
{
    /// <summary>Runs the rest of the pipeline, absorbing a cancellation that a client disconnect explains.</summary>
    /// <param name="context">The current request.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException exception) when (context.RequestAborted.IsCancellationRequested)
        {
            // Debug, not Error: a disconnect is routine (page navigation, worker shutdown, circuit
            // teardown), nothing was computed, and Serilog's request logging has already recorded the
            // aborted request.
            logger.LogDebug(
                exception,
                "{Method} {Path} was aborted by the client; the cancellation is a disconnect, not a fault",
                context.Request.Method,
                context.Request.Path);
        }
    }
}
