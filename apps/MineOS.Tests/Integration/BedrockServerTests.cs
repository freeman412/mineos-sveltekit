using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MineOS.Tests.Integration;

public class BedrockServerTests : IClassFixture<MineOsWebApplicationFactory>
{
    private readonly HttpClient _client;

    public BedrockServerTests(MineOsWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", "dev-static-api-key-change-me");
    }

    private static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..30];

    private async Task<string> GetTokenAsync()
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "admin", password = "admin123!" });
        var json = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("accessToken").GetString()!;
    }

    private HttpRequestMessage AuthRequest(HttpMethod method, string url, string token, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Api-Key", "dev-static-api-key-change-me");
        return request;
    }

    [Fact]
    public async Task Create_Bedrock_Server_Returns_201()
    {
        var token = await GetTokenAsync();
        var name = UniqueName("bedrock-create");
        var content = JsonContent.Create(new { name, serverType = "bedrock" });

        using var request = AuthRequest(HttpMethod.Post, "/api/v1/servers", token, content);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("bedrock", json.GetProperty("serverType").GetString());
        Assert.True(json.GetProperty("eulaAccepted").GetBoolean());
    }

    [Fact]
    public async Task Create_Bedrock_Server_Has_Correct_Config()
    {
        var token = await GetTokenAsync();
        var name = UniqueName("bedrock-cfg");
        var content = JsonContent.Create(new { name, serverType = "bedrock" });

        using var createRequest = AuthRequest(HttpMethod.Post, "/api/v1/servers", token, content);
        await _client.SendAsync(createRequest);

        using var getRequest = AuthRequest(HttpMethod.Get, $"/api/v1/servers/{name}", token);
        var response = await _client.SendAsync(getRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("bedrock", json.GetProperty("serverType").GetString());
        Assert.True(json.GetProperty("eulaAccepted").GetBoolean());
    }

    [Fact]
    public async Task Bedrock_Profiles_Are_Listed()
    {
        var token = await GetTokenAsync();

        using var request = AuthRequest(HttpMethod.Get, "/api/v1/profiles", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var profiles = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(profiles);

        var bedrockProfiles = profiles!.Where(p =>
            p.TryGetProperty("group", out var g) && g.GetString() == "bedrock-server").ToList();

        Assert.True(bedrockProfiles.Count > 0, "Should have at least one bedrock profile");
        Assert.All(bedrockProfiles, p =>
        {
            Assert.StartsWith("bedrock-server-", p.GetProperty("id").GetString());
        });
    }

    [Fact]
    public async Task Start_Bedrock_Without_Binary_Returns_Error()
    {
        var token = await GetTokenAsync();
        var name = UniqueName("bedrock-start");
        var content = JsonContent.Create(new { name, serverType = "bedrock" });

        using var createRequest = AuthRequest(HttpMethod.Post, "/api/v1/servers", token, content);
        await _client.SendAsync(createRequest);

        using var startRequest = AuthRequest(HttpMethod.Post, $"/api/v1/servers/{name}/actions/start", token);
        var response = await _client.SendAsync(startRequest);

        // Should fail — either missing screen (test host) or missing bedrock binary (container)
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = json.GetProperty("error").GetString()!;
        Assert.True(
            error.Contains("Bedrock server binary not found") || error.Contains("screen"),
            $"Expected bedrock binary or screen error, got: {error}");
    }

    [Fact]
    public async Task Create_Default_Server_Is_Java()
    {
        var token = await GetTokenAsync();
        var name = UniqueName("java-default");
        var content = JsonContent.Create(new { name });

        using var request = AuthRequest(HttpMethod.Post, "/api/v1/servers", token, content);
        var response = await _client.SendAsync(request);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("java", json.GetProperty("serverType").GetString());
        Assert.False(json.GetProperty("eulaAccepted").GetBoolean());
    }
}
