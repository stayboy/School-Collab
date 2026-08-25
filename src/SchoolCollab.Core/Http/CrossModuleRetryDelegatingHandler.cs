using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SchoolCollab.Core.Http;

/// <summary>
/// Retries cross-module HTTP calls that fail with handler/connection-level
/// transient faults: <see cref="ObjectDisposedException"/>,
/// <see cref="IOException"/> whose message mentions a disposed
/// <c>NetworkStream</c>, <see cref="HttpRequestException"/>, and 5xx/408
/// responses. These faults are typical when <see cref="IHttpClientFactory"/>
/// rotates its handler pool while a request is in flight, or when a pooled
/// TCP connection is closed by the remote end.
/// </summary>
/// <remarks>
/// This handler is intentionally the <b>outermost</b> delegating handler in a
/// cross-module pipeline: it observes every failure that bubbles up from the
/// inner handlers and decides whether to retry. Because it is registered as
/// <see cref="ServiceLifetime.Transient"/>, it is never captured by a
/// disposed DI scope or reused beyond its handler-chain lifetime.
/// </remarks>
public sealed class CrossModuleRetryDelegatingHandler : DelegatingHandler
{
    private readonly int _maxRetries;
    private readonly ILogger<CrossModuleRetryDelegatingHandler>? _logger;

    public CrossModuleRetryDelegatingHandler(
        IOptions<CrossModuleHttpClientOptions> options,
        ILogger<CrossModuleRetryDelegatingHandler>? logger = null)
    {
        var opts = options?.Value ?? new CrossModuleHttpClientOptions();
        _maxRetries = opts.MaxRetries >= 0
            ? opts.MaxRetries
            : throw new ArgumentOutOfRangeException(nameof(options), "MaxRetries must be non-negative.");
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpRequestMessage currentRequest = request;
        Exception? lastException = null;

        for (var attempt = 0; attempt <= _maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (attempt > 0)
            {
                currentRequest = await CloneRequestAsync(currentRequest, cancellationToken)
                    .ConfigureAwait(false);
            }

            try
            {
                var response = await base.SendAsync(currentRequest, cancellationToken)
                    .ConfigureAwait(false);

                if (attempt < _maxRetries && IsRetryableStatusCode(response.StatusCode))
                {
                    _logger?.LogWarning(
                        "Cross-module call returned {StatusCode} for {Method} {Url}; retrying (attempt {Attempt}/{Max}).",
                        (int)response.StatusCode,
                        request.Method,
                        request.RequestUri,
                        attempt + 1,
                        _maxRetries);

                    response.Dispose();
                    continue;
                }

                return response;
            }
            catch (Exception ex) when (attempt < _maxRetries && IsRetryableException(ex))
            {
                _logger?.LogWarning(
                    ex,
                    "Cross-module call failed for {Method} {Url}; retrying (attempt {Attempt}/{Max}).",
                    request.Method,
                    request.RequestUri,
                    attempt + 1,
                    _maxRetries);

                lastException = ex;
            }
        }

        throw lastException ?? new InvalidOperationException(
            "Cross-module retry loop exited without a result.");
    }

    private static bool IsRetryableException(Exception ex)
    {
        if (ex is ObjectDisposedException)
            return true;

        if (ex is HttpRequestException)
            return true;

        if (ex is IOException io && (
            io.Message.Contains("NetworkStream", StringComparison.OrdinalIgnoreCase) ||
            io.Message.Contains("The request was aborted", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return ex.InnerException is not null && IsRetryableException(ex.InnerException);
    }

    private static bool IsRetryableStatusCode(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.RequestTimeout || (int)statusCode >= 500;

    /// <summary>
    /// Re-create the request if it carries content, so a retry does not try
    /// to re-read a possibly-consumed stream. GET/DELETE/HEAD requests are
    /// returned unchanged (their message has no content to corrupt).
    /// </summary>
    private static async Task<HttpRequestMessage> CloneRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Content is null)
            return request;

        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        var memory = new MemoryStream();
        await request.Content.CopyToAsync(memory, cancellationToken)
            .ConfigureAwait(false);
        memory.Position = 0;

        clone.Content = new StreamContent(memory);
        foreach (var header in request.Content.Headers)
            clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);

        return clone;
    }
}
