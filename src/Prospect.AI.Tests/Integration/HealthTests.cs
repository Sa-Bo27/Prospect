namespace Prospect.AI.Tests.Integration;

[Collection(AppHostCollection.Name)]
public class HealthTests(AppHostFixture fixture)
{
    [Theory]
    [InlineData("server", "/health")]
    [InlineData("n8n", "/healthz")]
    public async Task ResourceHealthEndpointReturnsOk(string resourceName, string healthPath)
    {
        // Arrange
        await fixture.App.ResourceNotifications
            .WaitForResourceHealthyAsync(resourceName)
            .WaitAsync(AppHostFixture.DefaultTimeout);
        using var httpClient = fixture.App.CreateHttpClient(resourceName);

        // Act
        using var response = await httpClient.GetAsync(healthPath);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
