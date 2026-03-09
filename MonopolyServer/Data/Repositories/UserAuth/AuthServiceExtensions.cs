using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using MonopolyServer.Infrastructure;
using MonopolyServer.Infrastructure.Auth;

namespace MonopolyServer.Data.Repositories.UserAuth;

/// <summary>
/// DI registration for authentication and authorization.
/// Wires Entra External ID JWT, the GuestAuthHandler fallback scheme,
/// a multi-scheme default authorization policy, and Cosmos claim enrichment.
/// </summary>
public static class AuthServiceExtensions
{
    /// <summary>
    /// Registers all auth concerns. Must be called after AddGamePersistence.
    /// The default authorization policy accepts either JWT or GuestScheme so that
    /// plain [Authorize] on GameHub passes for both authenticated and guest connections.
    /// AdminOnly policy still requires JWT + the Admin role.
    /// </summary>
    public static IServiceCollection AddGameAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AzureAdSettings>(configuration.GetSection(AzureAdSettings.SectionName));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(configuration.GetSection(AzureAdSettings.SectionName))
            .Services
            .AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, GuestAuthHandler>(
                GuestAuthHandler.SchemeName, _ => { });

        // CIAM issues tokens with aud = ClientId (bare), not api://ClientId.
        var clientId = configuration[$"{AzureAdSettings.SectionName}:ClientId"] ?? string.Empty;
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.TokenValidationParameters.ValidAudiences =
            [
                clientId,
                $"api://{clientId}",
            ];

            // SignalR passes the JWT as ?access_token= — browsers cannot set
            // Authorization headers on WebSocket connections.
            var existing = options.Events;

            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = ctx =>
                {
                    var logger = ctx.HttpContext.RequestServices
                        .GetRequiredService<ILogger<JwtBearerEvents>>();
                    logger.LogError("JWT auth failed: {Error}", ctx.Exception?.Message);
                    return Task.CompletedTask;
                },
                OnMessageReceived = async ctx =>
                {
                    var token = ctx.Request.Query["access_token"];
                    var path = ctx.HttpContext.Request.Path;

                    if (!string.IsNullOrEmpty(token) &&
                        (path.StartsWithSegments("/game-hub") ||
                         path.StartsWithSegments("/admin-hub")))
                    {
                        ctx.Token = token;
                    }

                    if (existing?.OnMessageReceived != null)
                    {
                        await existing.OnMessageReceived(ctx);
                    }
                }
            };
        });

        services.AddAuthorization(options =>
        {
            // Plain [Authorize] on GameHub succeeds if either scheme authenticates.
            // This is what allows guests through while keeping the attribute meaningful.
            options.DefaultPolicy = new AuthorizationPolicyBuilder(
                    JwtBearerDefaults.AuthenticationScheme,
                    GuestAuthHandler.SchemeName)
                .RequireAuthenticatedUser()
                .Build();

            // AdminHub and any [Authorize("AdminOnly")] still require a real JWT + Admin role.
            options.AddPolicy("AdminOnly", policy =>
                policy
                    .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    .RequireRole(UserClaimsTransformation.AdminRole));
        });

        // Enriches every authenticated principal with roles from Cosmos.
        // Guests are skipped — their "guest_" objectId won't exist in the users container.
        services.AddScoped<IClaimsTransformation, UserClaimsTransformation>();

        // Register IUserRepository — reuses the CosmosClient already registered by AddGamePersistence
        services.AddSingleton<IUserRepository>(sp =>
        {
            var cosmosSettings = sp.GetRequiredService<IOptions<CosmosSettings>>().Value;
            var client = sp.GetRequiredService<Microsoft.Azure.Cosmos.CosmosClient>();
            var container = client.GetContainer(cosmosSettings.DatabaseId, cosmosSettings.UsersCollectionId);
            var logger = sp.GetRequiredService<ILogger<CosmosUserRepository>>();
            return new CosmosUserRepository(container, logger);
        });

        return services;
    }
}