using System.Security.Claims;
using Prospect.AI.Domain.Common;

namespace Prospect.AI.Server.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/me", GetCurrentUser)
            .RequireAuthorization()
            .WithName("GetCurrentUser")
            .WithTags("Account");

        return routes;
    }

    private static CurrentUserResponse GetCurrentUser(ClaimsPrincipal user, ICurrentUser currentUser) =>
        new(
            currentUser.UserId,
            user.FindFirst("email")?.Value,
            user.FindFirst("name")?.Value);

    public sealed record CurrentUserResponse(Guid? UserId, string? Email, string? Name);
}
