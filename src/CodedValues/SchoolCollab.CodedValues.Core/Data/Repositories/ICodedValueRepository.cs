using SchoolCollab.CodedValues.Core.Domain;
using SchoolCollab.CodedValues.Core.DTOs;

namespace SchoolCollab.CodedValues.Core.Data.Repositories;

public interface ICodedValueRepository
{
    Task<CodedValue?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CodedValue?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<CodedValue?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task AddAsync(CodedValue codedValue, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<CodedValue> codedValues, CancellationToken cancellationToken = default);
    Task UpdateAsync(CodedValue codedValue, CancellationToken cancellationToken = default);
    Task<int> CountChildrenAsync(Guid parentId, CancellationToken cancellationToken = default);
    Task<List<string>> GetReferencingSourceCodesAsync(Guid codedValueId, CancellationToken cancellationToken = default);
    Task<CodedValueDto[]> ListDeletedAsync(CancellationToken cancellationToken = default);
}
