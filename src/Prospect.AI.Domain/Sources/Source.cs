using Prospect.AI.Domain.Common;

namespace Prospect.AI.Domain.Sources;

/// <summary>
/// Where postings come from. Each user has their own sources (feeds, mailbox, manual entry);
/// n8n sends collected postings for a given source, which decides who owns them.
/// </summary>
public sealed class Source : IOwnedEntity
{
    public const int NameMaxLength = 200;
    public const int UrlMaxLength = 2048;

    public Guid Id { get; private set; }
    public Guid OwnerId { get; set; }
    public string Name { get; private set; } = null!;
    public SourceKind Kind { get; private set; }

    /// <summary>Feed or API address, when the source has one.</summary>
    public string? Url { get; private set; }

    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastCollectedAt { get; private set; }

    private Source()
    {
    }

    public static Source Create(string name, SourceKind kind, string? url, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Source
        {
            Id = Guid.CreateVersion7(now),
            Name = name.Trim(),
            Kind = kind,
            Url = string.IsNullOrWhiteSpace(url) ? null : url.Trim(),
            IsActive = true,
            CreatedAt = now,
        };
    }

    public void MarkCollected(DateTimeOffset now) => LastCollectedAt = now;

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
