using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.CodedValues.Core.Domain;

public sealed class TenantCodedValueAttributeOverride : BaseTenantEntity
{
    public Guid CodedValueId { get; private set; }
    public string AttributeKey { get; private set; } = default!;
    public string CustomValue { get; private set; } = default!;

    private TenantCodedValueAttributeOverride() { }

    internal TenantCodedValueAttributeOverride(Guid tenantId, Guid codedValueId, string attributeKey, string customValue)
    {
        TenantId = tenantId;
        CodedValueId = codedValueId;
        AttributeKey = attributeKey;
        CustomValue = customValue;
    }

    public void UpdateValue(string newValue)
    {
        CustomValue = newValue;
    }
}
