using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.CodedValues.Core.Domain;

public sealed class TenantCodedValueOverride : BaseTenantEntity
{
    public Guid CodedValueId { get; private set; }
    public string? Code { get; private set; }
    public string? Name { get; private set; }
    public bool? IsDisabled { get; private set; }

    private TenantCodedValueOverride() { }

    internal TenantCodedValueOverride(Guid tenantId, Guid codedValueId, string? code = null, string? name = null, bool? isDisabled = null)
    {
        TenantId = tenantId;
        CodedValueId = codedValueId;
        Code = code;
        Name = name;
        IsDisabled = isDisabled;
    }

    public void Update(string? code, string? name, bool? isDisabled)
    {
        Code = code;
        Name = name;
        IsDisabled = isDisabled;
    }
}
