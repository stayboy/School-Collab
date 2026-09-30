using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Configurable hermetic <see cref="ICodedValuesApiClient"/> for the bridge
/// suites: single-value reads come from <see cref="ById"/>, the
/// override-resolving <c>by-parent</c> read from <see cref="Catalogue"/>.
/// </summary>
internal sealed class StubCodedValuesApi : ICodedValuesApiClient
{
    public Dictionary<Guid, StreamCodedValueDto> ById { get; } = new();
    public StreamCodedValueDto[] Catalogue { get; set; } = [];

    /// <summary>Records every <c>by-parent</c> hop so a test can assert the read path.</summary>
    public List<string> ByParentCalls { get; } = [];

    public StubCodedValuesApi WithStream(StreamCodedValueDto dto)
    {
        ById[dto.Id] = dto;
        return this;
    }

    public Task<StreamCodedValueDto?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(ById.TryGetValue(id, out var dto) ? dto : null);

    public Task<StreamCodedValueDto[]> GetChildrenByParentCodeAsync(
        string parentCode, CancellationToken ct = default)
    {
        ByParentCalls.Add(parentCode);
        return Task.FromResult(Catalogue);
    }
}

/// <summary>Builds the stream coded-value DTO shapes the bridge handlers consume.</summary>
internal static class StreamDtoFactory
{
    /// <param name="gradeLevelAttributeValue">
    /// When non-null, the DTO carries the LEGACY <c>gradeLevel</c> attribute — the
    /// discriminator the pre-fix validation read. The bridge read path ignores it.
    /// </param>
    /// <param name="displayOrder">
    /// The coded value's own display order — the cross-grade GRSTREAMS catalogue order.
    /// It is deliberately independent of the grade's list order, which the bridge row
    /// owns; the bridge read must not project this value.
    /// </param>
    public static StreamCodedValueDto Stream(
        Guid id,
        string code,
        string name,
        string? version = null,
        bool isOverridden = false,
        string? defaultName = null,
        string? gradeLevelAttributeValue = null,
        int displayOrder = 0)
    {
        var attributes = new List<StreamAttributeDto>();
        if (gradeLevelAttributeValue is not null)
            attributes.Add(new StreamAttributeDto("gradeLevel", gradeLevelAttributeValue));
        if (version is not null)
            attributes.Add(new StreamAttributeDto("streamVersion", version));

        return new StreamCodedValueDto(
            id, code, name, null, null, "GRSTREAMS", false, displayOrder,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            attributes, isOverridden, defaultName);
    }
}
