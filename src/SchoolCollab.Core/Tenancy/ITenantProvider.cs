namespace SchoolCollab.Core.Tenancy;

public interface ITenantProvider
{
    Guid GetTenantId();
}
