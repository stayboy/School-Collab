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
/// The publish dialog's pre-select source (round notification-templates-b2a, spec §3.7 / WS5):
/// per stamped channel, the tenant's single live Publish template plus its display-only constraint
/// metadata, the effective policy's channel configuration, and the assignment's stored stamps.
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

        var channels = new List<PublishChannelOptionsDto>(StampedSlots.Length);
        foreach (var (channel, dto) in StampedSlots)
        {
            // Fail-open (§3.3): an unreadable Settings slot offers no template — the dialog then
            // shows the built-in default for that channel, which is exactly what would render.
            var slot = await templateResolver.ResolveSlotAsync(
                NotificationKind.Publish, channel, tenantId, cancellationToken);

            channels.Add(new PublishChannelOptionsDto(
                NotificationKindDto.Publish,
                dto,
                slot?.TemplateId,
                slot?.Subject,
                slot?.Constraints
                    .Select(c => new PublishTemplateConstraintDto(c.Dimension, c.Value))
                    .ToList() ?? []));
        }

        return new PublishOptionsDto(
            channels,
            [.. policy.PreferredChannelOrder.Select(c => (ContactChannelDto)(int)c)],
            [.. policy.BlockedChannels.Select(c => (ContactChannelDto)(int)c)],
            assignment.PublishEmailTemplateId,
            assignment.PublishSmsTemplateId);
    }
}
