using Microsoft.EntityFrameworkCore;
using Prospect.AI.Domain.Sources;
using Prospect.AI.Infrastructure.Persistence;

namespace Prospect.AI.Tests.Integration;

/// <summary>
/// Proves on the real PostgreSQL database that users never see each other's data.
/// </summary>
[Collection(AppHostCollection.Name)]
public class OwnerIsolationTests(AppHostFixture fixture)
{
    private readonly Guid _alice = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();

    [Fact]
    public async Task UsersOnlySeeTheirOwnData()
    {
        var sourceId = await AddSourceAsAsync(_alice);
        try
        {
            await using var asAlice = await CreateContextAsync(_alice);
            await using var asBob = await CreateContextAsync(_bob);
            await using var anonymous = await CreateContextAsync(null);

            Assert.True(await asAlice.Sources.AnyAsync(source => source.Id == sourceId));
            Assert.False(await asBob.Sources.AnyAsync(source => source.Id == sourceId));
            Assert.False(await anonymous.Sources.AnyAsync(source => source.Id == sourceId));
            // Cross-user background jobs must opt out explicitly.
            Assert.True(await anonymous.Sources.IgnoreQueryFilters().AnyAsync(source => source.Id == sourceId));
        }
        finally
        {
            await DeleteSourceAsync(sourceId);
        }
    }

    [Fact]
    public async Task NewEntityIsOwnedByCurrentUser()
    {
        var sourceId = await AddSourceAsAsync(_alice);
        try
        {
            await using var anonymous = await CreateContextAsync(null);
            var source = await anonymous.Sources.IgnoreQueryFilters().SingleAsync(source => source.Id == sourceId);

            Assert.Equal(_alice, source.OwnerId);
        }
        finally
        {
            await DeleteSourceAsync(sourceId);
        }
    }

    [Fact]
    public async Task SavingNewEntityWithoutUserThrows()
    {
        await using var anonymous = await CreateContextAsync(null);
        anonymous.Sources.Add(Source.Create("Orphan feed", SourceKind.Rss, null, DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<InvalidOperationException>(() => anonymous.SaveChangesAsync());
    }

    private async Task<Guid> AddSourceAsAsync(Guid userId)
    {
        await using var context = await CreateContextAsync(userId);
        var source = Source.Create("Test feed", SourceKind.Rss, "https://example.com/feed", DateTimeOffset.UtcNow);
        context.Sources.Add(source);
        await context.SaveChangesAsync();
        return source.Id;
    }

    private async Task DeleteSourceAsync(Guid sourceId)
    {
        await using var context = await CreateContextAsync(null);
        await context.Sources.IgnoreQueryFilters().Where(source => source.Id == sourceId).ExecuteDeleteAsync();
    }

    private Task<ProspectDbContext> CreateContextAsync(Guid? userId) => fixture.CreateDbContextAsync(userId);
}
