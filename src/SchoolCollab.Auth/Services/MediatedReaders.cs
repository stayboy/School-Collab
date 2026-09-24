using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SchoolCollab.Auth.Services;

/// <summary>
/// Outcome of one mediated read (spec D17). Deliberately a single failure state: the portal can do
/// exactly one thing with a failed mediated read — render the AC10 degraded card — so the mediator
/// does not invent distinctions the caller cannot act on (a rejected forward, a 5xx and an
/// unparseable body all mean "this read produced no data"). The failure CLASS is logged
/// server-side; it is never disclosed in the response and no upstream body is ever echoed.
/// </summary>
public enum MediatedReadStatus
{
    /// <summary>The upstream module answered with the expected data-only shape.</summary>
    Ok,

    /// <summary>The data could not be read: transport failure, a non-success status, or a body
    /// that was not the expected shape. Fail closed.</summary>
    Unavailable,
}

/// <summary>A mediated read's result: the upstream data as a value, or nothing (<see
/// cref="MediatedReadStatus.Unavailable"/>).</summary>
public sealed record MediatedRead<TData>(MediatedReadStatus Status, TData? Data = default)
{
    /// <summary>A successful read. Internal by design: the mediator is the only producer, so no
    /// caller can fabricate an "Ok" without data.</summary>
    internal static MediatedRead<TData> Ok(TData data) => new(MediatedReadStatus.Ok, data);

    /// <summary>A failed read.</summary>
    internal static MediatedRead<TData> Unavailable() => new(MediatedReadStatus.Unavailable);
}

/// <summary>A tenant-picker row (D17) — the data-only projection of settings-api's tenant
/// registry, carrying exactly what a picker renders. Nothing else from the upstream record
/// travels, so a mediated response cannot leak more than the picker needs (AC11).</summary>
public sealed record PickerTenant(Guid Id, string Name, string Type);

/// <summary>A teacher-picker row (D17) — the data-only projection of students-api's teacher
/// list: identity and display text only, no assignments, dates or coding references.</summary>
public sealed record PickerTeacher(Guid Id, string FirstName, string LastName, string? DisplayName);

/// <summary>
/// A mediated read of another module's data on the portal's behalf (spec D17 / D15's boundary):
/// the portal holds no credential, so the auth service performs the read **as the session's user**
/// by forwarding that session's custody access token (AC11). The upstream module's own tenant
/// middleware resolves the forwarded token's <c>tenant_id</c> claim, so the results are scoped
/// exactly as they are for a direct authenticated call — the mediator adds no scoping of its own.
/// </summary>
/// <remarks>
/// One implementation per upstream module (a typed <see cref="HttpClient"/> each, registered with
/// the cross-module retry handler and long handler lifetime); the shared half here is only the
/// request/response discipline: bearer-forward, typed deserialization, fail-closed on anything
/// else. The client deliberately propagates no tenant header — the identity travels in the
/// forwarded bearer token, and a dev-tenant header would be a second, weaker source of scope.
/// </remarks>
public abstract class MediatedReader<TItem>(
    HttpClient httpClient,
    ILogger<MediatedReader<TItem>> logger)
{
    /// <summary>The upstream route this reader calls, relative to its typed client's base address.</summary>
    protected abstract string UpstreamPath { get; }

    /// <summary>Web defaults, stated explicitly rather than relied on implicitly (the B4
    /// precedent): every module serializes .NET records as camelCase, and the read must tolerate
    /// case changes rather than blank a picker.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Reads the upstream list while authenticated as the session whose access token is passed in.
    /// </summary>
    /// <remarks>
    /// Fail-closed by construction: a non-success status, a transport fault, a timeout or a body
    /// that is not the expected JSON shape all return
    /// <see cref="MediatedReadStatus.Unavailable"/> — never an exception into the request pipeline
    /// (a mediated read must not turn an upstream outage into a 500) and never the upstream body.
    /// The upstream status is logged as a category; neither the token nor the session id is logged.
    /// </remarks>
    public async Task<MediatedRead<IReadOnlyList<TItem>>> ReadAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, UpstreamPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Includes a rejected forward (401/403): the read produced no data, and the portal
                // cannot act on the difference — it renders the same degraded card. The class is
                // logged, so the distinction is not lost to operators.
                logger.LogWarning(
                    "The mediated read of {UpstreamPath} failed with status {StatusCode}.",
                    UpstreamPath,
                    (int)response.StatusCode);
                return MediatedRead<IReadOnlyList<TItem>>.Unavailable();
            }

            var items = await response.Content.ReadFromJsonAsync<List<TItem>>(JsonOptions, cancellationToken);
            return items is null
                ? MediatedRead<IReadOnlyList<TItem>>.Unavailable()
                : MediatedRead<IReadOnlyList<TItem>>.Ok(items);
        }
        catch (HttpRequestException ex)
        {
            LogUnreachable(ex);
            return MediatedRead<IReadOnlyList<TItem>>.Unavailable();
        }
        catch (JsonException ex)
        {
            LogUnreachable(ex);
            return MediatedRead<IReadOnlyList<TItem>>.Unavailable();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller went away (the portal navigated off) — propagate the cancellation instead
            // of inventing a degraded read for a request nobody is waiting for.
            throw;
        }
        catch (OperationCanceledException ex)
        {
            LogUnreachable(ex);
            return MediatedRead<IReadOnlyList<TItem>>.Unavailable();
        }
    }

    private void LogUnreachable(Exception ex) =>
        logger.LogWarning(
            ex,
            "The mediated read of {UpstreamPath} could not be completed.",
            UpstreamPath);
}

/// <summary>Tenants for the portal's picker (D17): settings-api's read-only tenant registry,
/// <c>GET /api/tenants</c>, read as the session's user.</summary>
public sealed class TenantDirectoryReader(
    HttpClient httpClient,
    ILogger<MediatedReader<PickerTenant>> logger)
    : MediatedReader<PickerTenant>(httpClient, logger)
{
    /// <inheritdoc />
    protected override string UpstreamPath => "api/tenants";
}

/// <summary>Teachers for the portal's picker (D17): students-api's teacher list,
/// <c>GET /teachers</c>, read as the session's user — the same call the students-api tenant
/// middleware scopes for any other authenticated caller.</summary>
public sealed class TeacherDirectoryReader(
    HttpClient httpClient,
    ILogger<MediatedReader<PickerTeacher>> logger)
    : MediatedReader<PickerTeacher>(httpClient, logger)
{
    /// <inheritdoc />
    protected override string UpstreamPath => "teachers";
}
