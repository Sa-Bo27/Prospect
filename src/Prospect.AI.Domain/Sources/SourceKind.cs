namespace Prospect.AI.Domain.Sources;

public enum SourceKind
{
    /// <summary>RSS or Atom feed (e.g. freelancermap, Free-Work searches).</summary>
    Rss,

    /// <summary>Job-alert emails read from a dedicated mailbox (LinkedIn, Malt...).</summary>
    Email,

    /// <summary>Official platform API.</summary>
    Api,

    /// <summary>Posting added by hand from its URL (web or mobile share sheet).</summary>
    Manual,
}
