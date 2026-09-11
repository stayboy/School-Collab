namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Cross-bounded-context resolver for the <b>effective</b> guardian-signature
/// default of an assignment being created (assignment-request-implementation-details
/// .md §2 WS-C — the C1 prerequisite bullet; assignment-request-go-forward-breakdown
/// .md §2, spec §7 Q1). The interface lives in Assignments.Core; the implementation
/// (an HTTP client to the Settings + Students APIs) lives in Assignments.Api so this
/// module stays free of HTTP. Mirrors <see cref="INotificationPolicyResolver"/>.
///
/// <para>Resolution: the tenant-global default (Settings API, <see langword="false"/>
/// when unset) with an optional per-grade override (Students API; null = inherit).
/// The wizard pre-fills the create checkbox from the result — the author may override,
/// and the request value is the persisted snapshot. When
/// <paramref name="gradeLevelId"/> is null only the tenant-global default applies.</para>
/// </summary>
public interface ISignatureDefaultResolver
{
    Task<bool> ResolveRequiresSignatureDefaultAsync(
        Guid? gradeLevelId, CancellationToken cancellationToken = default);
}