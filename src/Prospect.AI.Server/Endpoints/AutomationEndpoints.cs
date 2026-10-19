using Prospect.AI.Server.Authentication;

namespace Prospect.AI.Server.Endpoints;

/// <summary>
/// Endpoints called by n8n workflows, authenticated with the shared API key (X-Api-Key header).
/// </summary>
public static class AutomationEndpoints
{
    public static IEndpointRouteBuilder MapAutomationEndpoints(this IEndpointRouteBuilder routes)
    {
        var automation = routes.MapGroup("/automation")
            .RequireAuthorization(AuthenticationExtensions.AutomationPolicy)
            .WithTags("Automation");

        // Lets an n8n workflow check its URL and API key before doing real work.
        automation.MapGet("/ping", () => TypedResults.Ok(new { Status = "ok" }))
            .WithName("AutomationPing");

        return routes;
    }
}
