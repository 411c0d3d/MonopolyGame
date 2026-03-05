using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using MonopolyServer.Infrastructure.Auth;

namespace MonopolyServer.Data.Repositories.UserAuth;

/// <summary>
/// Enriches the authenticated ClaimsPrincipal with roles stored in Cosmos.
/// Called by ASP.NET Core on every authenticated request.
/// Creates the UserDocument on first login and promotes to Admin if email matches AdminEmail config.
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
    /// Assigns Admin role if the user's email matches AdminEmail in config.
    /// Returns an enriched ClaimsPrincipal with role claims added.
    /// </summary>
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var objectId = principal.FindFirstValue(ObjectIdClaimType);
        if (string.IsNullOrEmpty(objectId))
        {
            return principal;
        }

        // Skip if Admin role already added this request cycle
        if (principal.IsInRole(AdminRole))
        {
            return principal;
        }

        var email = (principal.FindFirstValue("email")
                     ?? principal.FindFirstValue(ClaimTypes.Email)
                     ?? string.Empty).ToLowerInvariant();

        var displayName = principal.FindFirstValue("name")
                          ?? principal.FindFirstValue(ClaimTypes.Name)
                          ?? "Player";

        var user = await _userRepository.GetAsync(objectId);

        if (user == null)
        {
            user = new UserDocument
            {
                Id = objectId,
                Email = email,
                DisplayName = displayName,
                IsAdmin = IsAdminEmail(email),
                CreatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow
            };

            await _userRepository.UpsertAsync(user);
            _logger.LogInformation("New user registered: {DisplayName} ({Email}) isAdmin={IsAdmin}",
                displayName, email, user.IsAdmin);
        }
        else
        {
            // Promote to admin if email now matches config (e.g. config was updated)
            if (!user.IsAdmin && IsAdminEmail(email))
            {
                user.IsAdmin = true;
                _logger.LogInformation("User {Email} promoted to Admin", email);
            }

            user.LastLoginAt = DateTime.UtcNow;
            user.DisplayName = displayName;
            await _userRepository.UpsertAsync(user);
        }

        if (!user.IsAdmin)
        {
            return principal;
        }

        // Clone the identity and add the Admin role claim
        var identity = new ClaimsIdentity(principal.Identity);
        identity.AddClaim(new Claim(ClaimTypes.Role, AdminRole));

        return new ClaimsPrincipal(identity);
    }

    private bool IsAdminEmail(string email) =>
        !string.IsNullOrEmpty(_settings.AdminEmail) &&
        string.Equals(email, _settings.AdminEmail.ToLowerInvariant(), StringComparison.Ordinal);
}