using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Prospect.AI.Tests.Integration;

[Collection(AppHostCollection.Name)]
public class AuthenticationTests(AppHostFixture fixture)
{
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
        Assert.Equal(AppHostFixture.DevUserName, body.GetProperty("email").GetString());
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

    private Task<HttpClient> CreateServerClientAsync() => fixture.CreateServerClientAsync();

    private Task<string> GetDevUserAccessTokenAsync() => fixture.GetDevUserAccessTokenAsync();

    private Task<string> GetAutomationApiKeyAsync() => fixture.GetAutomationApiKeyAsync();
}
