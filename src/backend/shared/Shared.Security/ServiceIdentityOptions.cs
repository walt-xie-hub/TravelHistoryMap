namespace Shared.Security;

public sealed class ServiceIdentityOptions
{
    public const string SectionName = "ServiceIdentity";

    public string Issuer { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public TimeSpan RefreshBeforeExpiry { get; set; } = TimeSpan.FromSeconds(30);
}