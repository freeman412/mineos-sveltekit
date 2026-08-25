using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MineOS.Tests.Integration;

/// <summary>
/// Integration tests for the mutable display name (issue #180). The backend
/// name is the on-disk identity and never changes; the display name is a label
/// stored in server.config that falls back to the backend name when unset.
/// </summary>
public class ServerDisplayNameTests : IClassFixture<MineOsWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ServerDisplayNameTests(MineOsWebApplicationFactory factory)
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

    private async Task<string> CreateServerAsync(string token, string name)
    {
        var content = JsonContent.Create(new { name, serverType = "bedrock" });
        using var request = AuthRequest(HttpMethod.Post, "/api/v1/servers", token, content);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        // The directory is a slug derived from the label, not the label itself, so every
        // later call has to address the server by what the API actually created.
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("name").GetString()!;
    }

    [Fact]
    public async Task Fresh_Server_Keeps_The_Label_It_Was_Created_With()
    {
        // This used to assert displayName was null on a fresh server, because the
        // directory WAS the label and repeating it would have been redundant.
        // The directory is now a slug ("my-server-7f3a"), so the label has nowhere else
        // to live: a new server must carry it or the UI can only show the slug.
        //
        // Legacy servers are unaffected and still report null — nothing backfills them,
        // and null continues to mean "show the backend name".
        var token = await GetTokenAsync();
        var label = UniqueName("dn-legacy");
        var name = await CreateServerAsync(token, label);

        Assert.NotEqual(label, name);

        using var request = AuthRequest(HttpMethod.Get, $"/api/v1/servers/{name}", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(label, json.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Put_Display_Name_Returns_204_And_Persists()
    {
        var token = await GetTokenAsync();
        var name = await CreateServerAsync(token, UniqueName("dn-set"));

        using var putRequest = AuthRequest(HttpMethod.Put, $"/api/v1/servers/{name}/display-name", token,
            JsonContent.Create(new { displayName = "My Fancy Server" }));
        var putResponse = await _client.SendAsync(putRequest);
        Assert.Equal(HttpStatusCode.NoContent, putResponse.StatusCode);

        using var getRequest = AuthRequest(HttpMethod.Get, $"/api/v1/servers/{name}", token);
        var getResponse = await _client.SendAsync(getRequest);
        var json = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("My Fancy Server", json.GetProperty("displayName").GetString());
        // The backend identity is untouched.
        Assert.Equal(name, json.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Host_Servers_Summary_Includes_Display_Name()
    {
        var token = await GetTokenAsync();
        var name = await CreateServerAsync(token, UniqueName("dn-sum"));

        using var putRequest = AuthRequest(HttpMethod.Put, $"/api/v1/servers/{name}/display-name", token,
            JsonContent.Create(new { displayName = "Summary Label" }));
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(putRequest)).StatusCode);

        using var listRequest = AuthRequest(HttpMethod.Get, "/api/v1/host/servers", token);
        var listResponse = await _client.SendAsync(listRequest);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var servers = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var match = servers.EnumerateArray()
            .FirstOrDefault(s => s.GetProperty("name").GetString() == name);
        Assert.False(match.ValueKind == JsonValueKind.Undefined, $"server '{name}' missing from host summary");
        Assert.Equal("Summary Label", match.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Clear_Display_Name_With_Null_Returns_Null()
    {
        var token = await GetTokenAsync();
        var name = await CreateServerAsync(token, UniqueName("dn-clear"));

        using var setRequest = AuthRequest(HttpMethod.Put, $"/api/v1/servers/{name}/display-name", token,
            JsonContent.Create(new { displayName = (string?)"Temporary" }));
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(setRequest)).StatusCode);

        using var clearRequest = AuthRequest(HttpMethod.Put, $"/api/v1/servers/{name}/display-name", token,
            JsonContent.Create(new { displayName = (string?)null }));
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(clearRequest)).StatusCode);

        using var getRequest = AuthRequest(HttpMethod.Get, $"/api/v1/servers/{name}", token);
        var json = await (await _client.SendAsync(getRequest)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, json.GetProperty("displayName").ValueKind);
    }

    [Fact]
    public async Task Empty_Display_Name_Clears_Display_Name()
    {
        var token = await GetTokenAsync();
        var name = await CreateServerAsync(token, UniqueName("dn-empty"));

        using var setRequest = AuthRequest(HttpMethod.Put, $"/api/v1/servers/{name}/display-name", token,
            JsonContent.Create(new { displayName = "Temporary" }));
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(setRequest)).StatusCode);

        // Empty (and whitespace-only) strings clear the label rather than erroring.
        using var clearRequest = AuthRequest(HttpMethod.Put, $"/api/v1/servers/{name}/display-name", token,
            JsonContent.Create(new { displayName = "   " }));
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(clearRequest)).StatusCode);

        using var getRequest = AuthRequest(HttpMethod.Get, $"/api/v1/servers/{name}", token);
        var json = await (await _client.SendAsync(getRequest)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, json.GetProperty("displayName").ValueKind);
    }

    [Fact]
    public async Task Display_Name_Is_Trimmed()
    {
        var token = await GetTokenAsync();
        var name = await CreateServerAsync(token, UniqueName("dn-trim"));

        using var putRequest = AuthRequest(HttpMethod.Put, $"/api/v1/servers/{name}/display-name", token,
            JsonContent.Create(new { displayName = "  Padded Name  " }));
        var response = await _client.SendAsync(putRequest);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var getRequest = AuthRequest(HttpMethod.Get, $"/api/v1/servers/{name}", token);
        var json = await (await _client.SendAsync(getRequest)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Padded Name", json.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Unknown_Server_Returns_404()
    {
        var token = await GetTokenAsync();
        using var request = AuthRequest(HttpMethod.Put, "/api/v1/servers/does-not-exist-xyz/display-name", token,
            JsonContent.Create(new { displayName = "Anything" }));
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Too_Long_Display_Name_Rejected_400()
    {
        var token = await GetTokenAsync();
        var name = await CreateServerAsync(token, UniqueName("dn-long"));

        using var request = AuthRequest(HttpMethod.Put, $"/api/v1/servers/{name}/display-name", token,
            JsonContent.Create(new { displayName = new string('a', 65) }));
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Control_Character_Display_Name_Rejected_400()
    {
        var token = await GetTokenAsync();
        var name = await CreateServerAsync(token, UniqueName("dn-ctrl"));

        using var request = AuthRequest(HttpMethod.Put, $"/api/v1/servers/{name}/display-name", token,
            JsonContent.Create(new { displayName = "bad\u0007bell" }));
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Display_Name_Survives_Server_Config_Update()
    {
        // server.config is written full-replace by the config editor; the
        // [display] section must round-trip through it or the label is wiped
        // the first time someone touches their Java settings.
        var token = await GetTokenAsync();
        var name = await CreateServerAsync(token, UniqueName("dn-round"));

        using var putRequest = AuthRequest(HttpMethod.Put, $"/api/v1/servers/{name}/display-name", token,
            JsonContent.Create(new { displayName = "Survives Updates" }));
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(putRequest)).StatusCode);

        using var getConfig = AuthRequest(HttpMethod.Get, $"/api/v1/servers/{name}/server-config", token);
        var config = await _client.SendAsync(getConfig);
        Assert.Equal(HttpStatusCode.OK, config.StatusCode);
        using var putConfig = AuthRequest(HttpMethod.Put, $"/api/v1/servers/{name}/server-config", token, config.Content);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(putConfig)).StatusCode);

        using var getRequest = AuthRequest(HttpMethod.Get, $"/api/v1/servers/{name}", token);
        var json = await (await _client.SendAsync(getRequest)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Survives Updates", json.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Clone_Does_Not_Inherit_Display_Name()
    {
        var token = await GetTokenAsync();
        var source = await CreateServerAsync(token, UniqueName("dn-src"));

        using var setRequest = AuthRequest(HttpMethod.Put, $"/api/v1/servers/{source}/display-name", token,
            JsonContent.Create(new { displayName = "Original Label" }));
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(setRequest)).StatusCode);

        var cloneName = UniqueName("dn-clone");
        using var cloneRequest = AuthRequest(HttpMethod.Post, $"/api/v1/servers/{source}/clone", token,
            JsonContent.Create(new { newName = cloneName }));
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(cloneRequest)).StatusCode);

        using var getRequest = AuthRequest(HttpMethod.Get, $"/api/v1/servers/{cloneName}", token);
        var json = await (await _client.SendAsync(getRequest)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, json.GetProperty("displayName").ValueKind);
    }
}
