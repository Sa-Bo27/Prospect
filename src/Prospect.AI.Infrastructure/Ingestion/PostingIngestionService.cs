using Microsoft.EntityFrameworkCore;
using Prospect.AI.Domain.Missions;
using Prospect.AI.Infrastructure.Persistence;

namespace Prospect.AI.Infrastructure.Ingestion;

/// <summary>
/// Stores postings collected by n8n for one source, skipping the ones already known.
/// Runs without a signed-in user: the source decides who owns the postings.
/// </summary>
public sealed class PostingIngestionService(ProspectDbContext dbContext, TimeProvider timeProvider)
{
    public const int MaxBatchSize = 500;

    public async Task<IngestionResult> IngestAsync(
        Guid sourceId,
        IReadOnlyList<IncomingPosting> postings,
        CancellationToken cancellationToken = default)
    {
        var source = await dbContext.Sources
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(source => source.Id == sourceId, cancellationToken);

        if (source is null)
        {
            return IngestionResult.SourceNotFound;
        }

        if (!source.IsActive)
        {
            return IngestionResult.SourceInactive;
        }

        var now = timeProvider.GetUtcNow();
        var summary = new IngestionSummary { Received = postings.Count };
        var candidates = Normalize(postings, summary.Rejected);

        // Existing keys for this owner, loaded in two queries instead of one per posting.
        var urls = candidates.Select(candidate => candidate.CanonicalUrl).Distinct().ToList();
        var hashes = candidates.Select(candidate => candidate.ContentHash).Distinct().ToList();
        var knownUrls = (await dbContext.RawPostings
                .IgnoreQueryFilters()
                .Where(posting => posting.OwnerId == source.OwnerId && urls.Contains(posting.CanonicalUrl))
                .Select(posting => posting.CanonicalUrl)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var knownHashes = (await dbContext.RawPostings
                .IgnoreQueryFilters()
                .Where(posting => posting.OwnerId == source.OwnerId && hashes.Contains(posting.ContentHash))
                .Select(posting => posting.ContentHash)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            // Same page already collected (earlier run or earlier in this batch).
            if (!knownUrls.Add(candidate.CanonicalUrl))
            {
                summary.DuplicateUrls++;
                continue;
            }

            var posting = RawPosting.Create(
                source.Id,
                candidate.Posting.Url.Trim(),
                candidate.CanonicalUrl,
                candidate.ContentHash,
                candidate.Title,
                candidate.Posting.Content ?? string.Empty,
                candidate.Posting.PublishedAt,
                now);
            posting.OwnerId = source.OwnerId;
            dbContext.RawPostings.Add(posting);

            // Same text under another URL (e.g. the same offer via RSS and an e-mail alert):
            // the raw copy is kept for traceability, but it is not a new mission.
            if (!knownHashes.Add(candidate.ContentHash))
            {
                summary.DuplicateContents++;
                continue;
            }

            dbContext.Missions.Add(Mission.CreateFrom(posting, candidate.Posting.CompanyName, now));
            summary.Created++;
        }

        source.MarkCollected(now);
        await dbContext.SaveChangesAsync(cancellationToken);

        return IngestionResult.Completed(summary);
    }

    private static List<Candidate> Normalize(IReadOnlyList<IncomingPosting> postings, List<RejectedPosting> rejected)
    {
        var candidates = new List<Candidate>(postings.Count);

        for (var index = 0; index < postings.Count; index++)
        {
            var posting = postings[index];

            if (string.IsNullOrWhiteSpace(posting.Title))
            {
                rejected.Add(new RejectedPosting(index, posting.Url, "Title is required."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(posting.Url) || posting.Url.Trim().Length > RawPosting.UrlMaxLength)
            {
                rejected.Add(new RejectedPosting(index, posting.Url,
                    $"URL is required and must not exceed {RawPosting.UrlMaxLength} characters."));
                continue;
            }

            string canonicalUrl;
            try
            {
                canonicalUrl = PostingNormalizer.CanonicalizeUrl(posting.Url);
            }
            catch (ArgumentException)
            {
                rejected.Add(new RejectedPosting(index, posting.Url, "URL is not an absolute http(s) URL."));
                continue;
            }

            // Some feeds put a whole paragraph in the title: keep what fits instead of rejecting.
            var title = posting.Title.Trim();
            if (title.Length > RawPosting.TitleMaxLength)
            {
                title = title[..RawPosting.TitleMaxLength];
            }

            var contentHash = PostingNormalizer.ComputeContentHash(posting.Title, posting.Content ?? string.Empty);
            candidates.Add(new Candidate(posting, title, canonicalUrl, contentHash));
        }

        return candidates;
    }

    private sealed record Candidate(IncomingPosting Posting, string Title, string CanonicalUrl, string ContentHash);
}

/// <summary>A posting as sent by n8n (RSS item, parsed e-mail alert...).</summary>
public sealed record IncomingPosting(
    string Url,
    string Title,
    string? Content,
    DateTimeOffset? PublishedAt,
    string? CompanyName);

public sealed record RejectedPosting(int Index, string? Url, string Reason);

public sealed class IngestionSummary
{
    public int Received { get; init; }
    public int Created { get; set; }
    public int DuplicateUrls { get; set; }
    public int DuplicateContents { get; set; }
    public List<RejectedPosting> Rejected { get; } = [];
}

public enum IngestionStatus
{
    Completed,
    SourceNotFound,
    SourceInactive,
}

public sealed record IngestionResult(IngestionStatus Status, IngestionSummary? Summary)
{
    public static readonly IngestionResult SourceNotFound = new(IngestionStatus.SourceNotFound, null);
    public static readonly IngestionResult SourceInactive = new(IngestionStatus.SourceInactive, null);

    public static IngestionResult Completed(IngestionSummary summary) => new(IngestionStatus.Completed, summary);
}
