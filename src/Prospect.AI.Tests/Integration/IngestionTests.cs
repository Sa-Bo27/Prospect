using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prospect.AI.Domain.Missions;
using Prospect.AI.Domain.Sources;

namespace Prospect.AI.Tests.Integration;

/// <summary>
/// End-to-end collection: a user declares a source, n8n sends postings for it, duplicates are skipped.
/// </summary>
[Collection(AppHostCollection.Name)]
public class IngestionTests(AppHostFixture fixture) : IAsyncLifetime
{
    // Unique per test, so runs never collide with data left in the persistent database volume.
    private readonly string _run = Guid.NewGuid().ToString("N");
    private readonly List<Guid> _createdSources = [];

    [Fact]
    public async Task CreatedSourceIsListedForItsOwnerOnly()
    {
        using var user = await fixture.CreateUserClientAsync();
        var sourceId = await CreateSourceAsync(user);
        var otherUsersSourceId = await AddSourceForAnotherUserAsync();

        var sources = await user.GetFromJsonAsync<JsonElement>("/api/sources");

        var ids = sources.EnumerateArray().Select(source => source.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(sourceId, ids);
        Assert.DoesNotContain(otherUsersSourceId, ids);
        var created = sources.EnumerateArray().Single(source => source.GetProperty("id").GetGuid() == sourceId);
        Assert.Equal("Rss", created.GetProperty("kind").GetString());
        Assert.True(created.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task CreatingRssSourceWithoutUrlReturnsValidationProblem()
    {
        using var user = await fixture.CreateUserClientAsync();

        using var response = await user.PostAsJsonAsync("/api/sources", new { name = "No URL", kind = "Rss" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangingAnotherUsersSourceReturnsNotFound()
    {
        using var user = await fixture.CreateUserClientAsync();
        var otherUsersSourceId = await AddSourceForAnotherUserAsync();

        using var response = await user.PostAsync($"/api/sources/{otherUsersSourceId}/deactivate", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task IngestCreatesMissionsOwnedBySourceOwner()
    {
        using var user = await fixture.CreateUserClientAsync();
        var sourceId = await CreateSourceAsync(user);
        var userId = await GetCurrentUserIdAsync(user);

        var summary = await IngestAsync(sourceId, Posting("a"), Posting("b"));

        Assert.Equal(2, summary.GetProperty("received").GetInt32());
        Assert.Equal(2, summary.GetProperty("created").GetInt32());

        await using var asUser = await fixture.CreateDbContextAsync(userId);
        var missions = await asUser.Missions.Where(mission => mission.SourceId == sourceId).ToListAsync();
        Assert.Equal(2, missions.Count);
        Assert.All(missions, mission => Assert.Equal(MissionStatus.ToQualify, mission.Status));
        Assert.All(missions, mission => Assert.Equal(userId, mission.OwnerId));

        var source = await asUser.Sources.SingleAsync(source => source.Id == sourceId);
        Assert.NotNull(source.LastCollectedAt);
    }

    [Fact]
    public async Task IngestingSameBatchTwiceSkipsKnownUrls()
    {
        using var user = await fixture.CreateUserClientAsync();
        var sourceId = await CreateSourceAsync(user);
        await IngestAsync(sourceId, Posting("a"), Posting("b"));

        // Second run: same postings, one of them reached through a tracking link.
        var summary = await IngestAsync(
            sourceId,
            Posting("a"),
            Posting("b") with { Url = Posting("b").Url + "?utm_source=rss" });

        Assert.Equal(0, summary.GetProperty("created").GetInt32());
        Assert.Equal(2, summary.GetProperty("duplicateUrls").GetInt32());
    }

    [Fact]
    public async Task SameContentUnderAnotherUrlIsKeptButCreatesNoMission()
    {
        using var user = await fixture.CreateUserClientAsync();
        var sourceId = await CreateSourceAsync(user);
        var userId = await GetCurrentUserIdAsync(user);
        var original = Posting("a");

        var summary = await IngestAsync(
            sourceId,
            original,
            original with { Url = $"https://alerts.example.com/{_run}/a", Content = $"<p>{original.Content}</p>" });

        Assert.Equal(1, summary.GetProperty("created").GetInt32());
        Assert.Equal(1, summary.GetProperty("duplicateContents").GetInt32());

        await using var asUser = await fixture.CreateDbContextAsync(userId);
        Assert.Equal(2, await asUser.RawPostings.CountAsync(posting => posting.SourceId == sourceId));
        Assert.Equal(1, await asUser.Missions.CountAsync(mission => mission.SourceId == sourceId));
    }

    [Fact]
    public async Task InvalidPostingIsRejectedWithoutBlockingTheBatch()
    {
        using var user = await fixture.CreateUserClientAsync();
        var sourceId = await CreateSourceAsync(user);

        var summary = await IngestAsync(sourceId, Posting("a") with { Url = "not-a-url" }, Posting("b"));

        Assert.Equal(1, summary.GetProperty("created").GetInt32());
        var rejected = Assert.Single(summary.GetProperty("rejected").EnumerateArray());
        Assert.Equal(0, rejected.GetProperty("index").GetInt32());
    }

    [Fact]
    public async Task IngestForUnknownSourceReturnsNotFound()
    {
        using var automation = await fixture.CreateAutomationClientAsync();

        using var response = await automation.PostAsJsonAsync(
            "/api/automation/ingest",
            new { sourceId = Guid.NewGuid(), postings = new[] { Posting("a") } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task IngestForInactiveSourceReturnsConflictAndIsNotListedForAutomation()
    {
        using var user = await fixture.CreateUserClientAsync();
        var sourceId = await CreateSourceAsync(user);
        using var deactivate = await user.PostAsync($"/api/sources/{sourceId}/deactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        using var automation = await fixture.CreateAutomationClientAsync();

        using var response = await automation.PostAsJsonAsync(
            "/api/automation/ingest",
            new { sourceId, postings = new[] { Posting("a") } });
        var activeSources = await automation.GetFromJsonAsync<JsonElement>("/api/automation/sources");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.DoesNotContain(
            sourceId,
            activeSources.EnumerateArray().Select(source => source.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task ActiveSourceIsListedForAutomation()
    {
        using var user = await fixture.CreateUserClientAsync();
        var sourceId = await CreateSourceAsync(user);
        using var automation = await fixture.CreateAutomationClientAsync();

        var activeSources = await automation.GetFromJsonAsync<JsonElement>("/api/automation/sources");

        var source = activeSources.EnumerateArray().Single(source => source.GetProperty("id").GetGuid() == sourceId);
        Assert.Equal($"https://feeds.example.com/{_run}.xml", source.GetProperty("url").GetString());
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_createdSources.Count == 0)
        {
            return;
        }

        // Missions are removed with their raw posting (cascade); sources must go last.
        await using var context = await fixture.CreateDbContextAsync(null);
        await context.RawPostings.IgnoreQueryFilters()
            .Where(posting => _createdSources.Contains(posting.SourceId))
            .ExecuteDeleteAsync();
        await context.Sources.IgnoreQueryFilters()
            .Where(source => _createdSources.Contains(source.Id))
            .ExecuteDeleteAsync();
    }

    private PostingRequest Posting(string key) => new(
        $"https://www.jobs.example.com/{_run}/{key}",
        $"Senior .NET developer {key}",
        $"Remote assignment {_run} {key}: C#, ASP.NET Core, Angular.",
        DateTimeOffset.UtcNow.AddHours(-1),
        "Example Corp");

    private async Task<JsonElement> IngestAsync(Guid sourceId, params PostingRequest[] postings)
    {
        using var automation = await fixture.CreateAutomationClientAsync();
        using var response = await automation.PostAsJsonAsync("/api/automation/ingest", new { sourceId, postings });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<Guid> CreateSourceAsync(HttpClient user)
    {
        using var response = await user.PostAsJsonAsync(
            "/api/sources",
            new { name = $"Test feed {_run}", kind = "Rss", url = $"https://feeds.example.com/{_run}.xml" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var source = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = source.GetProperty("id").GetGuid();
        _createdSources.Add(id);
        return id;
    }

    private async Task<Guid> AddSourceForAnotherUserAsync()
    {
        await using var context = await fixture.CreateDbContextAsync(Guid.NewGuid());
        var source = Source.Create($"Someone else's feed {_run}", SourceKind.Rss, null, DateTimeOffset.UtcNow);
        context.Sources.Add(source);
        await context.SaveChangesAsync();
        _createdSources.Add(source.Id);
        return source.Id;
    }

    private static async Task<Guid> GetCurrentUserIdAsync(HttpClient user)
    {
        var me = await user.GetFromJsonAsync<JsonElement>("/api/me");
        return me.GetProperty("userId").GetGuid();
    }

    private sealed record PostingRequest(
        string Url,
        string Title,
        string Content,
        DateTimeOffset PublishedAt,
        string CompanyName);
}
