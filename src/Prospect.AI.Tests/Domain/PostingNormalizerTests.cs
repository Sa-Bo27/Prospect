using Prospect.AI.Domain.Missions;

namespace Prospect.AI.Tests.Domain;

public class PostingNormalizerTests
{
    [Theory]
    // Scheme forced to https, host lowercased, "www." removed.
    [InlineData("http://WWW.Example.com/jobs/42", "https://example.com/jobs/42")]
    // Default ports removed, custom port kept.
    [InlineData("https://example.com:443/jobs/42", "https://example.com/jobs/42")]
    [InlineData("http://example.com:8080/jobs/42", "https://example.com:8080/jobs/42")]
    // Fragment and trailing slash removed.
    [InlineData("https://example.com/jobs/42/#apply", "https://example.com/jobs/42")]
    [InlineData("https://example.com/", "https://example.com")]
    // Tracking parameters removed, meaningful ones kept and sorted.
    [InlineData("https://example.com/jobs?utm_source=mail&id=42&utm_campaign=x", "https://example.com/jobs?id=42")]
    [InlineData("https://example.com/search?q=dotnet&page=2", "https://example.com/search?page=2&q=dotnet")]
    [InlineData("https://example.com/jobs?gclid=abc&fbclid=def", "https://example.com/jobs")]
    // LinkedIn job alert links.
    [InlineData("https://www.linkedin.com/jobs/view/4012345678/?trk=eml-email_job_alert&refId=abc&trackingId=xyz",
        "https://linkedin.com/jobs/view/4012345678")]
    // Path case is preserved: it can be significant on the server.
    [InlineData("https://example.com/Jobs/ABC", "https://example.com/Jobs/ABC")]
    public void CanonicalizeUrlNormalizesEquivalentUrls(string input, string expected)
    {
        Assert.Equal(expected, PostingNormalizer.CanonicalizeUrl(input));
    }

    [Fact]
    public void CanonicalizeUrlGivesSameResultForTheSamePosting()
    {
        var fromRss = PostingNormalizer.CanonicalizeUrl("https://www.freelancermap.com/project/senior-net-developer-12345?utm_source=rss");
        var fromEmail = PostingNormalizer.CanonicalizeUrl("http://freelancermap.com/project/senior-net-developer-12345/");

        Assert.Equal(fromRss, fromEmail);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/jobs/42")]
    [InlineData("ftp://example.com/jobs/42")]
    [InlineData("mailto:recruiter@example.com")]
    public void CanonicalizeUrlRejectsInvalidUrls(string input)
    {
        Assert.ThrowsAny<ArgumentException>(() => PostingNormalizer.CanonicalizeUrl(input));
    }

    [Fact]
    public void ContentHashIsA64CharacterLowercaseHexString()
    {
        var hash = PostingNormalizer.ComputeContentHash("Senior .NET Developer", "Remote, 6 months");

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void ContentHashIgnoresCosmeticDifferences()
    {
        var plainText = PostingNormalizer.ComputeContentHash(
            "Senior .NET Developer",
            "C# & Angular, remote from Belgium.");
        var html = PostingNormalizer.ComputeContentHash(
            "  SENIOR .NET DEVELOPER ",
            "<p>C# &amp; Angular,\n\n  <b>remote</b> from Belgium.</p><script>track()</script>");

        Assert.Equal(plainText, html);
    }

    [Fact]
    public void ContentHashChangesWhenContentChanges()
    {
        var sixMonths = PostingNormalizer.ComputeContentHash("Senior .NET Developer", "Duration: 6 months");
        var twelveMonths = PostingNormalizer.ComputeContentHash("Senior .NET Developer", "Duration: 12 months");

        Assert.NotEqual(sixMonths, twelveMonths);
    }

    [Fact]
    public void ContentHashDistinguishesTitleFromContent()
    {
        // Without a separator, "AB" + "C" and "A" + "BC" would collide.
        Assert.NotEqual(
            PostingNormalizer.ComputeContentHash("Senior .NET", "Developer"),
            PostingNormalizer.ComputeContentHash("Senior", ".NET Developer"));
    }
}
