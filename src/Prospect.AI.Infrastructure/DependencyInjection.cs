using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Prospect.AI.Infrastructure.Ingestion;
using Prospect.AI.Infrastructure.Persistence;

namespace Prospect.AI.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Name of the database resource declared in the AppHost; Aspire injects its connection string.
    /// </summary>
    public const string DatabaseResourceName = "prospectdb";

    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        // Not pooled: each context carries the current user for the owner query filter.
        builder.Services.AddDbContext<ProspectDbContext>(options =>
            ProspectDbContextOptions.Configure(
                options.UseNpgsql(builder.Configuration.GetConnectionString(DatabaseResourceName))));

        // Adds Aspire retries, health check and OpenTelemetry tracing to the registration above.
        builder.EnrichNpgsqlDbContext<ProspectDbContext>();

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddScoped<PostingIngestionService>();

        return builder;
    }

    /// <summary>
    /// Applies pending EF Core migrations. Run once at startup: the app runs as a single instance.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IHost host, CancellationToken cancellationToken = default)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ProspectDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
