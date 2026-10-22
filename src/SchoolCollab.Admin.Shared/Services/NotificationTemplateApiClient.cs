using System.Net;
using System.Net.Http.Json;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Admin.Shared.Services;

// Mirrors SchoolCollab.Settings.Core.DTOs (TenantNotificationTemplateDto + its request shapes) and the
// grade-detail applicability payload, plus Settings.Core.Domain's two template enums. Re-declared here
// (rather than referencing Settings.Core) to keep Admin.Shared free of a Settings reference, matching
// the CodedValues / ConfigFlags / NotificationPolicy / AssignmentPolicy clients.

/// <summary>Mirror of <c>Settings.Core.Domain.NotificationTemplateKind</c> — the four template kinds.</summary>
public enum NotificationTemplateKind
{
    /// <summary>Assignment published to the recipient.</summary>
    Publish = 0,

    /// <summary>Reminder for an approaching due date.</summary>
    Reminder = 1,

    /// <summary>Submission / sign-off completed.</summary>
    Completion = 2,

    /// <summary>Overdue notification.</summary>
    Overdue = 3,
}

/// <summary>Mirror of <c>Settings.Core.Domain.TemplateConstraintDimension</c> — the five constraint
/// dimensions (spec §3.1.1).</summary>
public enum TemplateConstraintDimension
{
    /// <summary>Grade level (audience).</summary>
    GradeLevel = 0,

    /// <summary>Stream (audience).</summary>
    Stream = 1,

    /// <summary>Activity group (audience).</summary>
    ActivityGroup = 2,

    /// <summary>Subject / topic (content).</summary>
    Subject = 3,

    /// <summary>Assignment type (content).</summary>
    AssignmentType = 4,
}

/// <summary>Mirror of <c>Settings.Core.Domain.TemplateChangeKind</c>.</summary>
public enum TemplateChangeKind
{
    /// <summary>Template created.</summary>
    Created = 0,

    /// <summary>Template updated.</summary>
    Updated = 1,

    /// <summary>Template deleted (reset to the built-in default).</summary>
    Deleted = 2,
}

/// <summary>
/// One notification template and its constraints. The row with an empty
/// <see cref="Constraints"/> set is its scope's default for the kind × channel; every constrained row is
/// a candidate for the applicability matcher (spec §3.1.1). <see cref="TenantId"/> is
/// <see langword="null"/> for a system-blueprint row (redesign spec §3.7), whose <see cref="OwnerName"/>
/// is then "System"; a tenant-owned row carries its tenant's name.
/// <para>
/// Naming spec (D3/D6): <see cref="Title"/> is the row's identity everywhere and
/// <see cref="Description"/> is display-only metadata; <see cref="Code"/> is the generated exact
/// reference, minted on create and never writable — <see langword="null"/> for a row that predates the
/// code column, which the UI renders as a dash.
/// </para>
/// </summary>
public sealed record TenantNotificationTemplateDto(
    Guid Id,
    NotificationTemplateKind Kind,
    NotificationChannel Channel,
    string Title,
    string? Description,
    string? Code,
    string? Subject,
    string Body,
    IReadOnlyList<TemplateConstraintDto> Constraints,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? TenantId,
    string OwnerName)
{
    /// <summary>Whether the row is part of the system blueprint (no owning tenant).</summary>
    public bool IsSystem => TenantId is null;
}

/// <summary>One constraint dimension on a template (wire shape).</summary>
public sealed record TemplateConstraintDto(
    Guid Id,
    TemplateConstraintDimension Dimension,
    string Value);

/// <summary>
/// Create request for one template row (kind × channel travel in the body, not the route).
/// <see cref="Title"/> is required — a blank one is a 400 from the server's domain rule — and
/// <see cref="Description"/> is optional. The generated code is not part of this body: the create
/// handler mints it (naming spec D5/D6).
/// </summary>
public sealed record CreateNotificationTemplateRequest(
    NotificationTemplateKind Kind,
    NotificationChannel Channel,
    string Title,
    string? Description,
    string? Subject,
    string Body,
    IReadOnlyList<TemplateConstraintRequest> Constraints,
    string? Reason = null);

/// <summary>
/// Replace request for one template, addressed by id — kind, channel and the generated code are
/// immutable per row. <see cref="Title"/> is required and <see cref="Description"/> optional, exactly as
/// on create.
/// </summary>
public sealed record UpdateNotificationTemplateRequest(
    string Title,
    string? Description,
    string? Subject,
    string Body,
    IReadOnlyList<TemplateConstraintRequest> Constraints,
    string? Reason = null);

/// <summary>One constraint dimension in a create/update request.</summary>
public sealed record TemplateConstraintRequest(
    TemplateConstraintDimension Dimension,
    string Value);

/// <summary>One audit entry of a kind × channel's template history (spec §3.9).</summary>
public sealed record NotificationTemplateAuditEntryDto(
    Guid Id,
    Guid TemplateId,
    TemplateChangeKind ChangeKind,
    string? PreviousSubject,
    string? NewSubject,
    string? PreviousBody,
    string? NewBody,
    string? PreviousConstraints,
    string? NewConstraints,
    string? Reason,
    string ActorId,
    string ActorDisplayName,
    DateTimeOffset OccurredAt);

/// <summary>
/// One authorable kind × channel slot of the grade-detail card's payload (spec §3.11), with the
/// templates whose constraints can apply to the queried grade, most specific first.
/// </summary>
public sealed record ApplicableNotificationTemplatesDto(
    IReadOnlyList<ApplicableTemplateSlotDto> Slots);

/// <summary>One kind × channel slot of <see cref="ApplicableNotificationTemplatesDto"/>.</summary>
public sealed record ApplicableTemplateSlotDto(
    NotificationTemplateKind Kind,
    NotificationChannel Channel,
    IReadOnlyList<ApplicableTemplateDto> Templates);

/// <summary>One template applicable to the queried grade; <see cref="IsDefault"/> marks the scope
/// default and <see cref="TenantId"/>/<see cref="OwnerName"/> say whether it is a system-blueprint row
/// (redesign spec §3.7).</summary>
public sealed record ApplicableTemplateDto(
    Guid Id,
    string? Subject,
    bool IsDefault,
    IReadOnlyList<ApplicableConstraintDto> Constraints,
    Guid? TenantId,
    string OwnerName)
{
    /// <summary>Whether the row is part of the system blueprint (no owning tenant).</summary>
    public bool IsSystem => TenantId is null;
}

/// <summary>One constraint of an applicable template, with its resolved display label.</summary>
public sealed record ApplicableConstraintDto(
    TemplateConstraintDimension Dimension,
    string Value,
    string Display);

/// <summary>
/// Admin client for the notification-template store at
/// <c>/api/settings/notification-templates</c> (spec §5). Base address
/// <c>https+http://settings-api</c> is configured in DI. <c>TenantNotificationTemplate</c> is a
/// <b>hybrid</b> tenant entity (redesign spec §3.7): a tenant sees its own rows plus the system
/// blueprint, so the registered <c>TenantPropagationDelegatingHandler</c> must still carry the selected
/// tenant — it decides which tenant's rows the server resolves.
/// <para>
/// <b>Failure posture:</b> every non-success response throws (the admin surface must show the error —
/// fail-open is the render path's posture, not the editor's). The single exception is the documented
/// <c>204</c> of <see cref="GetDefaultAsync"/>, which means "no tenant default; the built-in default
/// applies" and maps to <see langword="null"/>.
/// </para>
/// <para>
/// <b>Route shapes follow the code, not spec §5's table:</b> mutations are addressed by <b>id</b>
/// (<c>PUT</c>/<c>DELETE …/{id}</c>, round notification-templates-b2c). The (kind, channel)-keyed
/// upsert the spec still lists could not tell two candidates of one slot apart.
/// </para>
/// </summary>
public sealed class NotificationTemplateApiClient(HttpClient http)
{
    private const string BasePath = "/api/settings/notification-templates";

    /// <summary>
    /// Every template row visible to the caller's scope — its own rows plus the system blueprint — with
    /// its constraints and owner.
    /// <para>
    /// <paramref name="includeTenantOwned"/> is the promote picker's source (redesign spec §3.6): the
    /// server honours it <b>only</b> in system scope, where it returns every tenant's rows. A tenant
    /// session's flag is ignored, so asking never widens what it can see.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<TenantNotificationTemplateDto>> ListAsync(
        bool includeTenantOwned = false, CancellationToken ct = default)
    {
        var path = includeTenantOwned ? $"{BasePath}?includeTenantOwned=true" : BasePath;
        var response = await http.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();

        var templates = await response.Content
            .ReadFromJsonAsync<List<TenantNotificationTemplateDto>>(cancellationToken: ct);
        return templates ?? [];
    }

    /// <summary>
    /// One template by id, or <see langword="null"/> on <c>404</c> (unknown or outside the caller's
    /// scope — the server does not distinguish the two). The per-template detail route's read.
    /// </summary>
    public async Task<TenantNotificationTemplateDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"{BasePath}/{id:D}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadFromJsonAsync<TenantNotificationTemplateDto>(cancellationToken: ct);
    }

    /// <summary>
    /// The current tenant's <b>default</b> row for <paramref name="kind"/> × <paramref name="channel"/>
    /// — the row with no constraints — or <see langword="null"/> on <c>204</c> (unset; the built-in
    /// default applies, spec §5).
    /// </summary>
    public async Task<TenantNotificationTemplateDto?> GetDefaultAsync(
        NotificationTemplateKind kind, NotificationChannel channel, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"{BasePath}/{kind}/{channel}", ct);
        if (response.StatusCode == HttpStatusCode.NoContent)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadFromJsonAsync<TenantNotificationTemplateDto>(cancellationToken: ct);
    }

    /// <summary>Creates one template row (the row's identity is its id, so kind × channel ride in the body).</summary>
    public async Task<TenantNotificationTemplateDto> CreateAsync(
        CreateNotificationTemplateRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(BasePath, request, ct);
        response.EnsureSuccessStatusCode();
        return await ReadTemplateAsync(response, ct);
    }

    /// <summary>Replaces one template row, addressed by id.</summary>
    public async Task<TenantNotificationTemplateDto> UpdateAsync(
        Guid id, UpdateNotificationTemplateRequest request, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"{BasePath}/{id:D}", request, ct);
        response.EnsureSuccessStatusCode();
        return await ReadTemplateAsync(response, ct);
    }

    /// <summary>
    /// Tombstones one template row, addressed by id. <paramref name="reason"/> is required by the
    /// endpoint (a reset is audited, so the audit row carries the operator's own words).
    /// </summary>
    public async Task DeleteAsync(Guid id, string reason, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync(
            $"{BasePath}/{id:D}?reason={Uri.EscapeDataString(reason)}", ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Copies a system-blueprint template into the current tenant (redesign spec §3.8) and returns the
    /// new row. The source is untouched; an identical constraint set already in this tenant is a 409
    /// whose message is surfaced verbatim.
    /// </summary>
    public async Task<TenantNotificationTemplateDto> AdoptAsync(
        Guid id, string? reason = null, CancellationToken ct = default)
    {
        var response = await http.PostAsync(CopyPath(id, "adopt", reason), content: null, ct);
        return await ReadCopyResultAsync(response, ct);
    }

    /// <summary>
    /// Publishes a tenant template portal-wide (redesign spec §3.8) and returns the blueprint copy.
    /// System scope only (the server answers 403 otherwise); an identical system-side set is a 409.
    /// </summary>
    public async Task<TenantNotificationTemplateDto> PromoteAsync(
        Guid id, string? reason = null, CancellationToken ct = default)
    {
        var response = await http.PostAsync(CopyPath(id, "promote", reason), content: null, ct);
        return await ReadCopyResultAsync(response, ct);
    }

    private static string CopyPath(Guid id, string action, string? reason) =>
        reason is null
            ? $"{BasePath}/{id:D}/{action}"
            : $"{BasePath}/{id:D}/{action}?reason={Uri.EscapeDataString(reason)}";

    /// <summary>
    /// Adopt/promote answer with the new row, or with a refusal whose <c>MessageResponse</c> body is the
    /// text the operator needs ("already adopted", "requires the system scope"). Throwing the plain
    /// status would throw that message away, so it is carried into the exception.
    /// </summary>
    private static async Task<TenantNotificationTemplateDto> ReadCopyResultAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"{(int)response.StatusCode} {response.ReasonPhrase}: {detail}", null, response.StatusCode);
        }

        return await ReadTemplateAsync(response, ct);
    }

    /// <summary>The audit trail of one kind × channel, most recent first (spec §3.9).</summary>
    public async Task<IReadOnlyList<NotificationTemplateAuditEntryDto>> GetAuditAsync(
        NotificationTemplateKind kind, NotificationChannel channel, Guid? templateId = null, CancellationToken ct = default)
    {
        var path = templateId is { } id
            ? $"{BasePath}/{kind}/{channel}/audit?templateId={id:D}"
            : $"{BasePath}/{kind}/{channel}/audit";
        var response = await http.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();

        var entries = await response.Content
            .ReadFromJsonAsync<List<NotificationTemplateAuditEntryDto>>(cancellationToken: ct);
        return entries ?? [];
    }

    /// <summary>
    /// The templates whose constraints can apply to <paramref name="gradeLevelId"/>, grouped into the
    /// eight authorable kind × channel slots (spec §3.11 — the grade-detail card's data).
    /// </summary>
    public async Task<ApplicableNotificationTemplatesDto> GetApplicableAsync(
        Guid gradeLevelId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"{BasePath}/applicable/{gradeLevelId:D}", ct);
        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<ApplicableNotificationTemplatesDto>(cancellationToken: ct)
            ?? throw BodyMissing(response);
    }

    private static async Task<TenantNotificationTemplateDto> ReadTemplateAsync(
        HttpResponseMessage response, CancellationToken ct) =>
        await response.Content.ReadFromJsonAsync<TenantNotificationTemplateDto>(cancellationToken: ct)
        ?? throw BodyMissing(response);

    private static HttpRequestException BodyMissing(HttpResponseMessage response) =>
        new($"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} returned no body.");
}
