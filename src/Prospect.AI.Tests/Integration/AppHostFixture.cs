using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prospect.AI.Domain.Common;
using Prospect.AI.Infrastructure;
using Prospect.AI.Infrastructure.Persistence;

namespace Prospect.AI.Tests.Integration;

/// <summary>
/// Starts the whole Aspire application once and shares it across the integration tests,
/// with helpers to call the server as a user or as n8n.
/// </summary>
public sealed class AppHostFixture : IAsyncLifetime
{
    // Generous timeout: CI runners pull every container image (Keycloak, n8n, Postgres...) on each run.
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

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

    // Development user imported with the realm (Prospect.AI.AppHost/Realms/prospect-realm.json).
    public const string DevUserName = "dev@prospect.local";
    public const string DevUserPassword = "dev-password";

    /// <summary>Plain HTTP client for the server (the HTTPS dev certificate is not trusted on CI).</summary>
    public async Task<HttpClient> CreateServerClientAsync()
    {
        await App.ResourceNotifications
            .WaitForResourceHealthyAsync("server")
            .WaitAsync(DefaultTimeout);
        return App.CreateHttpClient("server", "http");
    }

    /// <summary>Server client signed in as the development user.</summary>
    public async Task<HttpClient> CreateUserClientAsync()
    {
        var client = await CreateServerClientAsync();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await GetDevUserAccessTokenAsync());
        return client;
    }

    /// <summary>Server client authenticated like n8n, with the automation API key.</summary>
    public async Task<HttpClient> CreateAutomationClientAsync()
    {
        var client = await CreateServerClientAsync();
        client.DefaultRequestHeaders.Add("X-Api-Key", await GetAutomationApiKeyAsync());
        return client;
    }

    public async Task<string> GetDevUserAccessTokenAsync()
    {
        using var keycloak = App.CreateHttpClient("keycloak", "http");
        using var response = await keycloak.PostAsync(
            "/realms/prospect/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "prospect-dev-cli",
                ["username"] = DevUserName,
                ["password"] = DevUserPassword,
                ["scope"] = "openid email profile",
            }));
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<JsonElement>();
        return token.GetProperty("access_token").GetString()!;
    }

    public async Task<string> GetAutomationApiKeyAsync()
    {
        var parameter = App.Services.GetRequiredService<DistributedApplicationModel>()
            .Resources.OfType<ParameterResource>()
            .Single(resource => resource.Name == "automation-api-key");
        return (await parameter.GetValueAsync(CancellationToken.None))!;
    }

    /// <summary>
    /// Direct access to the real database, acting as the given user (null = no user).
    /// </summary>
    public async Task<ProspectDbContext> CreateDbContextAsync(Guid? userId)
    {
        // The server applies migrations at startup: wait for it so the tables exist.
        await App.ResourceNotifications
            .WaitForResourceHealthyAsync("server")
            .WaitAsync(DefaultTimeout);

        var connectionString = await App.GetConnectionStringAsync(DependencyInjection.DatabaseResourceName);
        var options = new DbContextOptionsBuilder<ProspectDbContext>();
        ProspectDbContextOptions.Configure(options.UseNpgsql(connectionString));

        return new ProspectDbContext(options.Options, new FixedCurrentUser(userId));
    }

    private sealed class FixedCurrentUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId => userId;
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
