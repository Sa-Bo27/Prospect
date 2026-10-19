namespace Prospect.AI.Domain.Common;

/// <summary>
/// An entity that belongs to a single user. Users only ever see their own data.
/// </summary>
public interface IOwnedEntity
{
    /// <summary>
    /// Identity-provider subject ("sub" claim) of the owning user. Set automatically on insert.
    /// </summary>
    Guid OwnerId { get; set; }
}
