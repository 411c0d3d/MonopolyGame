using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MonopolyServer.Data.Repositories.UserAuth;

/// <summary>
/// Authentication scheme for guest (unauthenticated) players.
/// Reads guest_id and guest_name from the query string and synthesizes a ClaimsPrincipal.
/// Only activates on /game-hub connections — /admin-hub remains JWT-only.
/// guest_id MUST start with "guest_" to prevent impersonation of real B2C objectIds.
/// </summary>
public sealed class GuestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "GuestScheme";
    private const string GuestIdPrefix = "guest_";

    /// <inheritdoc/>
    public GuestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <summary>
    /// Validates the guest_id query param and returns an authenticated result when valid,
    /// or NoResult to let the next scheme (JWT) take over.
    /// </summary>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Only activate for game-hub — admin-hub must remain JWT-only.
        if (!Request.Path.StartsWithSegments("/game-hub"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var guestId = Request.Query["guest_id"].FirstOrDefault();

        if (string.IsNullOrEmpty(guestId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Reject any value that doesn't carry the guest prefix — this closes the
        // path where a client supplies a real B2C objectId to impersonate an authenticated user.
        if (!guestId.StartsWith(GuestIdPrefix, StringComparison.Ordinal))
        {
            Logger.LogWarning("GuestAuth rejected guest_id that lacks required prefix: {GuestId}", guestId);
            return Task.FromResult(AuthenticateResult.Fail("guest_id must start with 'guest_'"));
        }

        var guestName = Request.Query["guest_name"].FirstOrDefault();
        var displayName = string.IsNullOrWhiteSpace(guestName) ? "Guest" : guestName.Trim();

        var claims = new[]
        {
            new Claim(UserClaimsTransformation.ObjectIdClaimType, guestId),
            new Claim(ClaimTypes.Name, displayName),
            new Claim("guest", "true"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        Logger.LogDebug("GuestAuth authenticated: guestId={GuestId} name={Name}", guestId, displayName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}