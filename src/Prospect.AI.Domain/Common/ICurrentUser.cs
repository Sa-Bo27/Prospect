namespace Prospect.AI.Domain.Common;

/// <summary>
/// The user on whose behalf the current operation runs.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// Null when no user is signed in (anonymous request, startup, background job).
    /// </summary>
    Guid? UserId { get; }
}
