using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using MonopolyServer.Infrastructure;
using MonopolyServer.Infrastructure.Auth;

namespace MonopolyServer.Data.Repositories.UserAuth;

/// <summary>
/// DI registration for authentication and authorization.
/// Wires Microsoft Entra External ID JWT validation, SignalR token extraction,
/// role-based authorization, and claim enrichment from Cosmos.
/// </summary>
public static class AuthServiceExtensions
{
    /// <summary>
    /// Registers JWT auth against Entra External ID, authorization policies,
    /// and the UserClaimsTransformation that enriches principals with roles from Cosmos.
    /// Must be called after AddGamePersistence so the Cosmos client is available.
    /// </summary>
    public static IServiceCollection AddGameAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AzureAdSettings>(configuration.GetSection(AzureAdSettings.SectionName));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(configuration.GetSection(AzureAdSettings.SectionName));

        // CIAM issues tokens with aud = ClientId (bare), not api://ClientId.
        // Accept both so the validator doesn't reject the token.
        var clientId = configuration[$"{AzureAdSettings.SectionName}:ClientId"] ?? string.Empty;
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.TokenValidationParameters.ValidAudiences =
            [
                clientId,
                $"api://{clientId}",
            ];

            // SignalR passes the JWT as ?access_token= in the query string since
            // browsers cannot set Authorization headers on WebSocket connections.
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
            options.AddPolicy("AdminOnly", policy => policy.RequireRole(UserClaimsTransformation.AdminRole));
        });

        // Enriches every authenticated principal with roles from Cosmos
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