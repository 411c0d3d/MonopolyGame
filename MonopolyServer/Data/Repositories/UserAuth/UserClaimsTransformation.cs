using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using MonopolyServer.Infrastructure.Auth;

namespace MonopolyServer.Data.Repositories.UserAuth;

/// <summary>
/// Enriches the authenticated ClaimsPrincipal with roles stored in Cosmos.
/// Called by ASP.NET Core on every authenticated request.
/// Creates the UserDocument on first login. Admin is identified by objectId or email from config —
/// when objectId matches, the known AdminEmail from config is stored directly, bypassing CIAM claim noise.
/// </summary>
public sealed class UserClaimsTransformation : IClaimsTransformation
{
    private readonly IUserRepository _userRepository;
    private readonly AzureAdSettings _settings;
    private readonly ILogger<UserClaimsTransformation> _logger;

    public const string ObjectIdClaimType =
        "http://schemas.microsoft.com/identity/claims/objectidentifier";

    public const string AdminRole = "Admin";

    /// <summary>Constructor with dependency injection.</summary>
    public UserClaimsTransformation(
        IUserRepository userRepository,
        IOptions<AzureAdSettings> settings,
        ILogger<UserClaimsTransformation> logger)
    {
        _userRepository = userRepository;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Looks up or creates the UserDocument for the authenticated user.
    /// Admin is matched by objectId first, email second.
    /// When objectId matches AdminObjectId, the real AdminEmail from config overwrites whatever CIAM sent.
    /// </summary>
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var objectId = principal.FindFirstValue(ObjectIdClaimType)
                       ?? principal.FindFirstValue("oid")
                       ?? principal.FindFirstValue("sub");

        if (string.IsNullOrEmpty(objectId))
        {
            _logger.LogWarning("UserClaimsTransformation: no objectId claim found — skipping enrichment");
            return principal;
        }

        // Raw token email — may be a CIAM UPN placeholder, not the real address.
        var tokenEmail = (principal.FindFirstValue("email")
                          ?? principal.FindFirstValue("preferred_username")
                          ?? principal.FindFirstValue(ClaimTypes.Email)
                          ?? principal.FindFirstValue(ClaimTypes.Upn)
                          ?? string.Empty).ToLowerInvariant();

        var displayName = principal.FindFirstValue("name")
                          ?? principal.FindFirstValue(ClaimTypes.Name)
                          ?? "Player";

        // When objectId matches AdminObjectId we know exactly who this is — use the configured
        // real email instead of whatever CIAM put in the token.
        var isAdminByObjectId = IsAdminObjectId(objectId);
        var resolvedEmail = isAdminByObjectId && !string.IsNullOrEmpty(_settings.AdminEmail)
            ? _settings.AdminEmail.Trim().ToLowerInvariant()
            : tokenEmail;

        _logger.LogInformation(
            "UserClaimsTransformation: objectId={ObjectId} isAdminByObjectId={IsAdminById}",
            objectId, isAdminByObjectId);

        try
        {
            var user = await _userRepository.GetAsync(objectId);

            if (user == null)
            {
                user = new UserDocument
                {
                    Id = objectId,
                    Email = resolvedEmail,
                    DisplayName = displayName,
                    IsAdmin = isAdminByObjectId || IsAdminEmail(resolvedEmail),
                    CreatedAt = DateTime.UtcNow,
                    LastLoginAt = DateTime.UtcNow
                };

                await _userRepository.UpsertAsync(user);
                _logger.LogInformation("New user registered: {DisplayName} objectId={ObjectId} email={Email} isAdmin={IsAdmin}",
                    displayName, objectId, resolvedEmail, user.IsAdmin);
            }
            else
            {
                // Promote if not yet admin but now matches config
                if (!user.IsAdmin && (isAdminByObjectId || IsAdminEmail(resolvedEmail)))
                {
                    user.IsAdmin = true;
                    _logger.LogInformation("User {ObjectId} promoted to Admin", objectId);
                }

                // Fix stored email if it is still a CIAM UPN placeholder
                if (IsUpnPlaceholder(user.Email) && !string.IsNullOrEmpty(resolvedEmail) &&
                    !IsUpnPlaceholder(resolvedEmail))
                {
                    user.Email = resolvedEmail;
                }

                user.LastLoginAt = DateTime.UtcNow;
                user.DisplayName = displayName;
                await _userRepository.UpsertAsync(user);
            }

            if (!user.IsAdmin)
            {
                return principal;
            }

            // Avoid adding a duplicate role claim on subsequent requests in the same pipeline cycle
            if (principal.IsInRole(AdminRole))
            {
                return principal;
            }

            var identity = new ClaimsIdentity(principal.Identity);
            identity.AddClaim(new Claim(ClaimTypes.Role, AdminRole));
            return new ClaimsPrincipal(identity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UserClaimsTransformation failed for objectId={ObjectId}", objectId);
            return principal;
        }
    }

    /// <summary>
    /// Matches the objectId against AdminObjectId config.
    /// </summary>
    private bool IsAdminObjectId(string objectId) =>
        !string.IsNullOrEmpty(_settings.AdminObjectId) &&
        string.Equals(objectId, _settings.AdminObjectId.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Matches the resolved email against AdminEmail config.
    /// </summary>
    private bool IsAdminEmail(string email) =>
        !string.IsNullOrEmpty(_settings.AdminEmail) &&
        !string.IsNullOrEmpty(email) &&
        string.Equals(email, _settings.AdminEmail.Trim().ToLowerInvariant(), StringComparison.Ordinal);

    /// <summary>
    /// Detects CIAM-generated UPN placeholders: {guid}@{tenant}.onmicrosoft.com
    /// </summary>
    private static bool IsUpnPlaceholder(string email) =>
        !string.IsNullOrEmpty(email) &&
        email.Contains('@') &&
        email.Split('@')[0].Length == 36 &&
        email.EndsWith(".onmicrosoft.com", StringComparison.OrdinalIgnoreCase);
}