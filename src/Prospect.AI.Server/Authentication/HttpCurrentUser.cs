using Prospect.AI.Domain.Common;

namespace Prospect.AI.Server.Authentication;

/// <summary>
/// Reads the signed-in user from the "sub" claim of the validated access token.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value, out var userId)
            ? userId
            : null;
}
