using Prospect.AI.Domain.Common;

namespace Prospect.AI.Domain.Missions;

/// <summary>
/// A posting exactly as collected, kept untouched for traceability and re-analysis.
/// One row per (owner, canonical URL): collecting the same posting again does not add a row.
/// </summary>
public sealed class RawPosting : IOwnedEntity
{
    public const int UrlMaxLength = 2048;
    public const int TitleMaxLength = 500;
    public const int ContentHashLength = 64;

    public Guid Id { get; private set; }
    public Guid OwnerId { get; set; }
    public Guid SourceId { get; private set; }

    /// <summary>URL as received from the source (may contain tracking parameters).</summary>
    public string OriginalUrl { get; private set; } = null!;

    /// <summary>Normalized URL used for deduplication.</summary>
    public string CanonicalUrl { get; private set; } = null!;

    /// <summary>SHA-256 (hex) of the normalized title + content, to spot the same posting under another URL.</summary>
    public string ContentHash { get; private set; } = null!;

    public string Title { get; private set; } = null!;
    public string Content { get; private set; } = null!;
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset CollectedAt { get; private set; }

    private RawPosting()
    {
    }

    public static RawPosting Create(
        Guid sourceId,
        string originalUrl,
        string canonicalUrl,
        string contentHash,
        string title,
        string content,
        DateTimeOffset? publishedAt,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new RawPosting
        {
            Id = Guid.CreateVersion7(now),
            SourceId = sourceId,
            OriginalUrl = originalUrl,
            CanonicalUrl = canonicalUrl,
            ContentHash = contentHash,
            Title = title.Trim(),
            Content = content,
            PublishedAt = publishedAt,
            CollectedAt = now,
        };
    }
}
