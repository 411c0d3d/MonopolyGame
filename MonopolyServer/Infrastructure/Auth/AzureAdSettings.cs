namespace MonopolyServer.Infrastructure.Auth;

/// <summary>
/// Strongly-typed settings for Microsoft Entra External ID, bound from appsettings "EntraExternalId" section.
/// </summary>
public sealed class AzureAdSettings
{
    public const string SectionName = "EntraExternalId";

    /// <summary>
    /// CIAM authority URL — https://{tenant}.ciamlogin.com/{tenantId}/v2.0
    /// </summary>
    public string Authority { get; init; } = string.Empty;

    /// <summary>
    /// App registration client ID.
    /// </summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>
    /// App registration client secret.
    /// </summary>
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>
    /// Tenant domain
    /// </summary>
    public string Domain { get; init; } = string.Empty;

    /// <summary>
    /// Email address that receives the Admin role on first login.
    /// </summary>
    public string AdminEmail { get; init; } = string.Empty;
}