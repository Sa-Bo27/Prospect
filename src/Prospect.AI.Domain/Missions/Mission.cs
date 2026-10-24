using Prospect.AI.Domain.Common;

namespace Prospect.AI.Domain.Missions;

/// <summary>
/// A unique freelance assignment for a user, created from a raw posting.
/// Extracted details (technologies, day rate, remote policy...) and the score are added in phase 2.
/// </summary>
public sealed class Mission : IOwnedEntity
{
    public const int CompanyNameMaxLength = 200;

    public Guid Id { get; private set; }
    public Guid OwnerId { get; set; }
    public Guid SourceId { get; private set; }
    public Guid RawPostingId { get; private set; }

    public string Title { get; private set; } = null!;
    public string Url { get; private set; } = null!;
    public string? CompanyName { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public MissionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Mission()
    {
    }

    public static Mission CreateFrom(RawPosting posting, string? companyName, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(posting);

        return new Mission
        {
            Id = Guid.CreateVersion7(now),
            OwnerId = posting.OwnerId,
            SourceId = posting.SourceId,
            RawPostingId = posting.Id,
            Title = posting.Title,
            Url = posting.CanonicalUrl,
            CompanyName = string.IsNullOrWhiteSpace(companyName) ? null : companyName.Trim(),
            PublishedAt = posting.PublishedAt,
            Status = MissionStatus.ToQualify,
            CreatedAt = now,
        };
    }

    public void Dismiss() => Status = MissionStatus.Dismissed;
}
