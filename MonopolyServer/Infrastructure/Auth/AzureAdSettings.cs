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
    /// Tenant domain.
    /// </summary>
    public string Domain { get; init; } = string.Empty;

    /// <summary>
    /// Real email address of the admin. Used as fallback match and stored in Cosmos
    /// when the objectId match fires — bypassing CIAM UPN placeholder noise.
    /// Keep blank in appsettings.json; set via user secrets or environment variable.
    /// </summary>
    public string AdminEmail { get; init; } = string.Empty;

    /// <summary>
    /// B2C objectId of the admin user. Primary admin match — immune to CIAM email claim inconsistencies.
    /// Find this in the Cosmos users container "id" field after the user's first login.
    /// Keep blank in appsettings.json; set via user secrets or environment variable.
    /// </summary>
    public string AdminObjectId { get; init; } = string.Empty;
}