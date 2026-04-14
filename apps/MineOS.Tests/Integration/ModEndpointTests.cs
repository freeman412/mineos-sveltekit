using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MineOS.Tests.Integration;

public class ModEndpointTests : IClassFixture<MineOsWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ModEndpointTests(MineOsWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", "dev-static-api-key-change-me");
    }

    private async Task<string> GetTokenAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "admin", password = "admin123!" });
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
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
    public async Task Loader_Endpoint_Returns_Ok()
    {
        var token = await GetTokenAsync();
        var name = $"loader-test-{Guid.NewGuid():N}"[..30];

        using var createReq = AuthRequest(HttpMethod.Post, "/api/v1/servers", token,
            JsonContent.Create(new { name }));
        await _client.SendAsync(createReq);

        using var loaderReq = AuthRequest(HttpMethod.Get, $"/api/v1/servers/{name}/loader", token);
        var response = await _client.SendAsync(loaderReq);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("loader").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Disable_Nonexistent_Mod_Returns_404()
    {
        var token = await GetTokenAsync();
        var name = $"mod-404-{Guid.NewGuid():N}"[..30];

        using var createReq = AuthRequest(HttpMethod.Post, "/api/v1/servers", token,
            JsonContent.Create(new { name }));
        await _client.SendAsync(createReq);

        using var disableReq = AuthRequest(HttpMethod.Post,
            $"/api/v1/servers/{name}/mods/{Uri.EscapeDataString("nonexistent.jar")}/disable", token);
        var response = await _client.SendAsync(disableReq);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Disable_Mod_On_Nonexistent_Server_Returns_404()
    {
        var token = await GetTokenAsync();

        using var req = AuthRequest(HttpMethod.Post,
            "/api/v1/servers/does-not-exist/mods/test.jar/disable", token);
        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
