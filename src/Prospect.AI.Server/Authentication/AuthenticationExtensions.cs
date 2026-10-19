using Microsoft.AspNetCore.Authentication.JwtBearer;
using Prospect.AI.Domain.Common;

namespace Prospect.AI.Server.Authentication;

public static class AuthenticationExtensions
{
    /// <summary>
    /// Policy for endpoints called by n8n with the shared API key.
    /// </summary>
    public const string AutomationPolicy = "Automation";

    /// <summary>
    /// Users authenticate with standard OpenID Connect access tokens (JWT). The API only knows the
    /// issuer (Auth:Authority) and audience (Auth:Audience), so the identity provider can be swapped
    /// (Keycloak today, Cognito or another provider later) through configuration alone.
    /// </summary>
    public static IHostApplicationBuilder AddProspectAuthentication(this IHostApplicationBuilder builder)
    {
        var configuration = builder.Configuration;

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = configuration["Auth:Authority"];
                options.Audience = configuration["Auth:Audience"];
                options.RequireHttpsMetadata = configuration.GetValue("Auth:RequireHttpsMetadata", true);
                // Keep the original claim names ("sub", "email") instead of the legacy SOAP URIs.
                options.MapInboundClaims = false;
            })
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.SchemeName,
                options => options.ApiKey = configuration["Automation:ApiKey"]);

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(AutomationPolicy, policy => policy
                .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser());

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

        return builder;
    }
}
