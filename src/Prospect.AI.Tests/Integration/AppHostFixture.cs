using Aspire.Hosting;
using Microsoft.Extensions.Logging;

namespace Prospect.AI.Tests.Integration;

/// <summary>
/// Starts the whole Aspire application once and shares it across the tests of a class.
/// </summary>
public sealed class AppHostFixture : IAsyncLifetime
{
    // Generous timeout: the first run may need to pull container images.
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);

    public DistributedApplication App { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Prospect_AI_AppHost>();
        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddFilter(appHost.Environment.ApplicationName, LogLevel.Debug);
            logging.AddFilter("Aspire.", LogLevel.Debug);
        });
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        App = await appHost.BuildAsync().WaitAsync(DefaultTimeout);
        await App.StartAsync().WaitAsync(DefaultTimeout);
    }

    public async Task DisposeAsync()
    {
        await App.DisposeAsync();
    }
}

/// <summary>
/// All integration test classes share one running application: the AppHost uses fixed ports
/// (Keycloak, n8n, web), so two instances cannot run side by side.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AppHostCollection : ICollectionFixture<AppHostFixture>
{
    public const string Name = "AppHost";
}
