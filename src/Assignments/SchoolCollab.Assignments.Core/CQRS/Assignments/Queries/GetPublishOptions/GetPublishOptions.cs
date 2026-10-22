using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetPublishOptions;

/// <summary>
/// The publish dialog's pre-select source (round notification-templates-b2a/b2c, spec §3.7 / WS5):
/// per stamped channel, the assignment's ordered applicable template candidates, the tenant default
/// row, the effective policy's channel configuration, and the assignment's stored stamps.
/// Tenant-scoped — a foreign assignment is an <see cref="AssignmentNotFoundException"/> (404).
/// </summary>
public sealed record GetPublishOptions(Guid AssignmentId) : IQuery<PublishOptionsDto>;

/// <summary>Handles <see cref="GetPublishOptions"/>.</summary>
public sealed class GetPublishOptionsHandler(
    IAssignmentRepository repository,
    INotificationTemplateResolver templateResolver,
    INotificationPolicyResolver policyResolver,
    ITenantProvider tenantProvider)
    : IQueryHandler<GetPublishOptions, PublishOptionsDto>
{
    /// <summary>The stamped slots, in the order the dialog renders them (§3.7 pins two columns).</summary>
    private static readonly (NotificationChannel Channel, ContactChannelDto Dto)[] StampedSlots =
    [
        (NotificationChannel.Email, ContactChannelDto.Email),
        (NotificationChannel.SMS, ContactChannelDto.SMS),
    ];

    /// <inheritdoc />
    public async Task<PublishOptionsDto> HandleAsync(
        GetPublishOptions query, CancellationToken cancellationToken = default)
    {
        // The tenant query filter is the scoping: another tenant's assignment reads as absent.
        var assignment = await repository.GetAsync(query.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(query.AssignmentId);

        var tenantId = tenantProvider.GetTenantContext().TenantId;
        var policy = await policyResolver.ResolveEffectiveAsync(
            tenantId, AssignmentPolicyScope.DeriveGrade(assignment), cancellationToken);

        // The assignment's own dimensions decide applicability; it is already in hand (no extra read).
        var dimensions = AssignmentTemplateContext.FromAssignment(assignment);

        var channels = new List<PublishChannelOptionsDto>(StampedSlots.Length);
        foreach (var (channel, dto) in StampedSlots)
        {
            // Fail-open (§3.3): an unreadable Settings read offers no template — the dialog then
            // shows the built-in default for that channel, which is exactly what would render.
            var read = await templateResolver.ReadCandidatesAsync(
                NotificationKind.Publish, channel, tenantId, cancellationToken);
            var candidates = read.Candidates;

            // The ordered APPLICABLE list — the evaluator's order is the pre-select order
            // (most specific first; the tenant default wins a tie).
            var applicable = NotificationTemplateEvaluator.Rank(candidates, dimensions);

            // The legacy single-slot fields stay meaningful by pinning them to the tenant DEFAULT
            // row (the unconstrained one), mirroring the narrowed GET …/{kind}/{channel}.
            var defaultRow = candidates.FirstOrDefault(c => c.Constraints.Count == 0);

            channels.Add(new PublishChannelOptionsDto(
                NotificationKindDto.Publish,
                dto,
                defaultRow?.TemplateId,
                defaultRow?.Subject,
                defaultRow?.Constraints
                    .Select(c => new PublishTemplateConstraintDto(c.Dimension, c.Value))
                    .ToList() ?? [],
                applicable
                    .Select(c => new PublishTemplateCandidateDto(
                        c.TemplateId,
                        c.Constraints
                            .Select(constraint => new PublishTemplateConstraintDto(constraint.Dimension, constraint.Value))
                            .ToList(),
                        IsDefault: c.Constraints.Count == 0,
                        IsSystem: c.IsSystem,
                        OwnerName: c.OwnerName))
                    .ToList()));
        }

        return new PublishOptionsDto(
            channels,
            [.. policy.PreferredChannelOrder.Select(c => (ContactChannelDto)(int)c)],
            [.. policy.BlockedChannels.Select(c => (ContactChannelDto)(int)c)],
            assignment.PublishEmailTemplateId,
            assignment.PublishSmsTemplateId);
    }
}
