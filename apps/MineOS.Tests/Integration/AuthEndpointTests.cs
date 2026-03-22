using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MineOS.Tests.Integration;

public class AuthEndpointTests : IClassFixture<MineOsWebApplicationFactory>
{
    private readonly MineOsWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthEndpointTests(MineOsWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", "dev-static-api-key-change-me");
    }

    [Fact]
    public async Task Login_With_Valid_Credentials_Returns_Token()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "admin", password = "admin123!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("accessToken", out var token));
        Assert.False(string.IsNullOrEmpty(token.GetString()));
    }

    [Fact]
    public async Task Login_With_Invalid_Credentials_Returns_401()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "admin", password = "wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Protected_Endpoint_Without_Auth_Returns_401()
    {
        var response = await _client.GetAsync("/api/v1/account");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Protected_Endpoint_With_Auth_Returns_200()
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "admin", password = "admin123!" });
        var loginJson = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = loginJson.GetProperty("accessToken").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/account");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Api-Key", "dev-static-api-key-change-me");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Protected_Endpoint_Without_Api_Key_Returns_401()
    {
        using var noKeyClient = _factory.CreateClient();
        // No X-Api-Key header — protected endpoints should reject
        var response = await noKeyClient.GetAsync("/api/v1/account");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
