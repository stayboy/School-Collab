using Microsoft.Extensions.Configuration;

namespace SchoolCollab.Students.Core.Services;

/// <summary>
/// Flag-gated strategy switch over coded-value reads
/// (adr-cross-module-calls.md Phase 1). When
/// <c>Students:UseLocalCodedValueProjection</c> is enabled, resolves from the
/// local read model — no settings-api hop, no tenant forwarding, no
/// handler-rotation race. When off (default), delegates to the HTTP client so
/// the projection can warm up behind the flag without behavior change.
///
/// <para>Registered as the <see cref="ICodedValuesApiClient"/> implementation;
/// handlers keep their existing constructor shape.</para>
/// </summary>
public sealed class FlagRoutedCodedValuesApiClient(
    ICodedValuesApiClient httpClient,
    ILocalCodedValueRepository localRepository,
    IConfiguration configuration) : ICodedValuesApiClient
{
    public async Task<StreamCodedValueDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (configuration.GetValue("Students:UseLocalCodedValueProjection", defaultValue: false))
        {
            return await localRepository.GetByIdAsync(id, ct);
        }

        return await httpClient.GetByIdAsync(id, ct);
    }
}
