using SchoolCollab.CodedValues.Core.Domain;

namespace SchoolCollab.CodedValues.Core.Data.Repositories;

public interface ICodedValueRepository
{
    Task<CodedValue?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task AddAsync(CodedValue codedValue, CancellationToken cancellationToken = default);
    Task UpdateAsync(CodedValue codedValue, CancellationToken cancellationToken = default);
}
