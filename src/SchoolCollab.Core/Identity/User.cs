namespace SchoolCollab.Core.Identity;

public class User
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = string.Empty; // Subject ID from Keycloak
    public string Email { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string Role { get; set; } = "User";
}
