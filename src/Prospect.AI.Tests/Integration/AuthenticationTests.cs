using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Prospect.AI.Tests.Integration;

[Collection(AppHostCollection.Name)]
public class AuthenticationTests(AppHostFixture fixture)
{
    // Development user imported with the realm (Prospect.AI.AppHost/Realms/prospect-realm.json).
    private const string DevUserName = "dev@prospect.local";
    private const string DevUserPassword = "dev-password";

    [Fact]
    public async Task MeWithoutTokenReturnsUnauthorized()
    {
        using var client = await CreateServerClientAsync();

        using var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MeWithDevUserTokenReturnsCurrentUser()
    {
        using var client = await CreateServerClientAsync();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await GetDevUserAccessTokenAsync());

        using var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(Guid.TryParse(body.GetProperty("userId").GetString(), out _));
        Assert.Equal(DevUserName, body.GetProperty("email").GetString());
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("wrong-key", HttpStatusCode.Unauthorized)]
    public async Task AutomationPingRejectsMissingOrWrongApiKey(string? apiKey, HttpStatusCode expected)
    {
        using var client = await CreateServerClientAsync();
        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }

        using var response = await client.GetAsync("/api/automation/ping");

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task AutomationPingAcceptsConfiguredApiKey()
    {
        using var client = await CreateServerClientAsync();
        client.DefaultRequestHeaders.Add("X-Api-Key", await GetAutomationApiKeyAsync());

        using var response = await client.GetAsync("/api/automation/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AutomationPingRejectsUserToken()
    {
        using var client = await CreateServerClientAsync();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await GetDevUserAccessTokenAsync());

        using var response = await client.GetAsync("/api/automation/ping");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpClient> CreateServerClientAsync()
    {
        await fixture.App.ResourceNotifications
            .WaitForResourceHealthyAsync("server")
            .WaitAsync(AppHostFixture.DefaultTimeout);
        return fixture.App.CreateHttpClient("server", "http");
    }

    private async Task<string> GetDevUserAccessTokenAsync()
    {
        using var keycloak = fixture.App.CreateHttpClient("keycloak", "http");
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

    private async Task<string> GetAutomationApiKeyAsync()
    {
        var parameter = fixture.App.Services.GetRequiredService<DistributedApplicationModel>()
            .Resources.OfType<ParameterResource>()
            .Single(resource => resource.Name == "automation-api-key");
        return (await parameter.GetValueAsync(CancellationToken.None))!;
    }
}
