using Microsoft.EntityFrameworkCore;
using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data;
using SchoolCollab.CodedValues.Core.DTOs;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;

namespace SchoolCollab.CodedValues.Core.Queries.GetCodedValueById;

public sealed class GetCodedValueByIdHandler(CodedValuesDbContext db)
    : IQueryHandler<GetCodedValueById, CodedValueDto>
{
    public async Task<CodedValueDto> HandleAsync(
        GetCodedValueById query,
        CancellationToken cancellationToken = default)
    {
        var cv = await db.CodedValues
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == query.Id, cancellationToken)
            ?? throw new CodedValueNotFoundException(query.Id);

        return new CodedValueDto(
            cv.Id,
            cv.Code,
            cv.Name,
            cv.Description,
            cv.ParentId,
            cv.IsDisabled,
            cv.DisplayOrder,
            cv.CreatedAt,
            cv.UpdatedAt,
            cv.Attributes.Select(a => new CodedValueAttributeDto(a.Key, a.Value)).ToArray(),
            cv.AttributeDefinitions.Select(d => new CodedValueAttributeDefinitionDto(d.Key, d.DisplayName, d.DataType, d.SourceCode, d.IsRequired)).ToArray());
    }
}
