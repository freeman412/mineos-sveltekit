using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Infrastructure.Utilities;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace MineOS.Tests.Integration;

/// <summary>
/// Server software update detection and apply (issue #83). The real
/// IProfileService talks to upstream APIs, so every test here swaps in a fake
/// with a fixed catalog; the API under test must do its detection math against
/// whatever ListProfilesAsync reports.
/// </summary>
public class ServerUpdateTests : IClassFixture<MineOsWebApplicationFactory>
{
    private const string ApiKey = "dev-static-api-key-change-me";
    private readonly MineOsWebApplicationFactory _factory;

    public ServerUpdateTests(MineOsWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
    }

    private readonly HttpClient _client;

    private static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..30];

    // Fixed upstream catalog the fake profile service serves. Paper 1.21.11 has
    // build 140 available (newer than the 132 most tests plant), 1.22.0 exists
    // as a version jump, and each other supported family has one entry.
    public static readonly List<ProfileDto> Catalog = new()
    {
        new ProfileDto("paper-1.21.11", "paper", "release", "1.21.11",
            "2026-01-01T00:00:00Z", "https://example.test/paper-1.21.11-140.jar", "paper-1.21.11-140.jar", false, null),
        new ProfileDto("paper-1.22.0", "paper", "release", "1.22.0",
            "2026-02-01T00:00:00Z", "https://example.test/paper-1.22.0-10.jar", "paper-1.22.0-10.jar", false, null),
        new ProfileDto("vanilla-1.21.11", "vanilla", "release", "1.21.11",
            "2026-01-01T00:00:00Z", "https://example.test/vanilla-1.21.11.jar", "vanilla-1.21.11.jar", false, null),
        new ProfileDto("velocity-3.4.0", "velocity", "release", "3.4.0",
            "2026-01-01T00:00:00Z", "https://example.test/velocity-3.4.0-570.jar", "velocity-3.4.0-570.jar", false, null),
        new ProfileDto("bungeecord-build-2131", "bungeecord", "release", "build-2131",
            "2026-01-01T00:00:00Z", "https://example.test/bungeecord-build-2131.jar", "bungeecord-build-2131.jar", false, null),
        new ProfileDto("bedrock-server-1.21.50.10", "bedrock-server", "release", "1.21.50.10",
            "2026-01-01T00:00:00Z", "https://example.test/bedrock-server-1.21.50.10.zip", "bedrock-server-1.21.50.10.zip", false, null)
    };

    /// <summary>
    /// Stands in for the real ProfileService: serves the catalog, materializes a
    /// real file for DownloadProfileAsync (a zip with a bedrock_server entry for
    /// bedrock profiles, plain bytes otherwise). CopyProfileToServerAsync throws:
    /// update applies are expected to own their swap so backups happen.
    /// </summary>
    private sealed class FakeProfileService : IProfileService
    {
        public FakeProfileService()
        {
            Profiles = new List<ProfileDto>(Catalog);
        }

        public List<ProfileDto> Profiles { get; }

        public string DownloadDir => Path.Combine(Path.GetTempPath(), $"mineos-fake-profiles-{Guid.NewGuid():N}");

        public Task<IReadOnlyList<ProfileDto>> ListProfilesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProfileDto>>(Profiles);

        public Task<ProfileDto?> GetProfileAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(Profiles.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase)));

        public async Task<string> DownloadProfileAsync(string id, CancellationToken cancellationToken)
        {
            var profile = Profiles.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Profile '{id}' not found");
            var dir = DownloadDir;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, profile.Filename);

            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
                var entry = archive.CreateEntry("bedrock_server");
                await using var writer = new StreamWriter(entry.Open());
                await writer.WriteAsync("#!/bin/sh\necho fake-bedrock\n");
            }
            else
            {
                await File.WriteAllTextAsync(path, "// fake jar", cancellationToken);
            }

            return path;
        }

        public Task CopyProfileToServerAsync(string profileId, string serverName, CancellationToken cancellationToken) =>
            throw new NotSupportedException("UpdateService must perform its own backup-then-swap");

        public IAsyncEnumerable<ProfileDownloadProgressDto> StreamDownloadProgressAsync(string id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BuildToolsRunDto> StartBuildToolsAsync(string group, string version, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<BuildToolsRunDto>> ListBuildToolsRunsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BuildToolsRunDto>>(Array.Empty<BuildToolsRunDto>());

        public Task<BuildToolsRunDto?> GetBuildToolsRunAsync(string runId, CancellationToken cancellationToken) =>
            Task.FromResult<BuildToolsRunDto?>(null);

        public IAsyncEnumerable<BuildToolsLogEntryDto> StreamBuildToolsLogAsync(string runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteBuildToolsAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private HttpClient NewClient(FakeProfileService profiles, Mock<IProcessManager>? processManager = null)
    {
        var derived = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IProfileService));
                if (existing != null) services.Remove(existing);
                services.AddSingleton<IProfileService>(profiles);

                if (processManager != null)
                {
                    var pmDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IProcessManager));
                    if (pmDescriptor != null) services.Remove(pmDescriptor);
                    services.AddSingleton(processManager.Object);
                }
            }));

        var client = derived.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return client;
    }

    private async Task<string> GetTokenAsync(HttpClient client)
    {
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "admin", password = "admin123!" });
        var json = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("accessToken").GetString()!;
    }

    private HttpRequestMessage AuthRequest(HttpMethod method, string url, string token, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Api-Key", ApiKey);
        return request;
    }

    private async Task<string> CreateJavaServerAsync(HttpClient client, string name)
    {
        var token = await GetTokenAsync(client);
        using var request = AuthRequest(HttpMethod.Post, "/api/v1/servers", token,
            JsonContent.Create(new { name, ownerUid = 1000, ownerGid = 1000, serverType = "java" }));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return token;
    }

    private async Task<string> CreateBedrockServerAsync(HttpClient client, string name)
    {
        var token = await GetTokenAsync(client);
        using var request = AuthRequest(HttpMethod.Post, "/api/v1/servers", token,
            JsonContent.Create(new { name, ownerUid = 1000, ownerGid = 1000, serverType = "bedrock" }));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return token;
    }

    private string ServerDir(string name) => Path.Combine(_factory.HostRoot, "servers", name);

    private async Task SetConfigSectionAsync(string name, string section, IReadOnlyDictionary<string, string> values)
    {
        var configPath = Path.Combine(ServerDir(name), "server.config");
        var sections = File.Exists(configPath)
            ? IniParser.ParseWithSections(await File.ReadAllTextAsync(configPath))
            : new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        sections[section] = new Dictionary<string, string>(values);
        await File.WriteAllTextAsync(configPath, IniParser.WriteWithSections(sections));
    }

    private void PlantJar(string name, string filename)
    {
        Directory.CreateDirectory(ServerDir(name));
        File.WriteAllText(Path.Combine(ServerDir(name), filename), "// planted");
    }

    private async Task<JsonElement> GetUpdatesJsonAsync(HttpClient client, string token, string name)
    {
        using var request = AuthRequest(HttpMethod.Get, $"/api/v1/servers/{name}/updates", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ------------------------------------------------------------------
    // Detection
    // ------------------------------------------------------------------

    [Fact]
    public async Task Unknown_Server_Returns_404()
    {
        var client = NewClient(new FakeProfileService());
        var token = await GetTokenAsync(client);

        using var request = AuthRequest(HttpMethod.Get, "/api/v1/servers/does-not-exist/updates", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        // Distinguishes the real handler's 404 from a missing-route 404.
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Fresh_Java_Server_Without_Jar_Is_Unsupported()
    {
        var client = NewClient(new FakeProfileService());
        var name = UniqueName("upd-fresh");
        var token = await CreateJavaServerAsync(client, name);

        var json = await GetUpdatesJsonAsync(client, token, name);

        Assert.False(json.GetProperty("supported").GetBoolean());
        Assert.False(json.GetProperty("updateAvailable").GetBoolean());
    }

    [Fact]
    public async Task Paper_Server_With_Older_Build_Shows_Update_Available()
    {
        var client = NewClient(new FakeProfileService());
        var name = UniqueName("upd-paper-old");
        var token = await CreateJavaServerAsync(client, name);
        PlantJar(name, "paper-1.21.11-132.jar");
        await SetConfigSectionAsync(name, "java", new Dictionary<string, string>
        {
            ["java_binary"] = "",
            ["jarfile"] = "paper-1.21.11-132.jar"
        });

        var json = await GetUpdatesJsonAsync(client, token, name);

        Assert.True(json.GetProperty("supported").GetBoolean());
        Assert.Equal("paper", json.GetProperty("family").GetString());
        Assert.Equal("1.21.11", json.GetProperty("currentVersion").GetString());
        Assert.Equal(132, json.GetProperty("currentBuild").GetInt32());
        Assert.True(json.GetProperty("updateAvailable").GetBoolean());
        Assert.Equal(140, json.GetProperty("latestBuildNumber").GetInt32());
        Assert.Equal("paper-1.21.11", json.GetProperty("latestBuildProfileId").GetString());
        // Version jumps are surfaced separately, never folded into the badge.
        Assert.True(json.GetProperty("jumpAvailable").GetBoolean());
        Assert.Equal("1.22.0", json.GetProperty("jumpVersion").GetString());
        Assert.Equal("notify", json.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task Paper_Server_On_Latest_Build_And_Version_Has_No_Update()
    {
        var client = NewClient(new FakeProfileService());
        var name = UniqueName("upd-paper-new");
        var token = await CreateJavaServerAsync(client, name);
        PlantJar(name, "paper-1.21.11-140.jar");
        await SetConfigSectionAsync(name, "java", new Dictionary<string, string>
        {
            ["jarfile"] = "paper-1.21.11-140.jar"
        });

        var json = await GetUpdatesJsonAsync(client, token, name);

        Assert.True(json.GetProperty("supported").GetBoolean());
        // No badge — but the newer Minecraft version still exists as an opt-in
        // jump offer (1.22.0 is in the catalog), it just never badges on its own.
        Assert.False(json.GetProperty("updateAvailable").GetBoolean());
        Assert.True(json.GetProperty("jumpAvailable").GetBoolean());
    }

    [Fact]
    public async Task Legacy_Jar_Without_Config_Entry_Is_Detected_By_Scan()
    {
        var client = NewClient(new FakeProfileService());
        var name = UniqueName("upd-paper-scan");
        var token = await CreateJavaServerAsync(client, name);
        PlantJar(name, "paper-1.21.11-100.jar");

        var json = await GetUpdatesJsonAsync(client, token, name);

        Assert.True(json.GetProperty("supported").GetBoolean());
        Assert.Equal("paper", json.GetProperty("family").GetString());
        Assert.True(json.GetProperty("updateAvailable").GetBoolean());
    }

    [Fact]
    public async Task Modded_Server_Is_Not_Supported()
    {
        var client = NewClient(new FakeProfileService());
        var name = UniqueName("upd-forge");
        var token = await CreateJavaServerAsync(client, name);
        PlantJar(name, "forge-1.21.1-52.0.24.jar");
        await SetConfigSectionAsync(name, "java", new Dictionary<string, string>
        {
            ["jarfile"] = "forge-1.21.1-52.0.24.jar"
        });

        var json = await GetUpdatesJsonAsync(client, token, name);

        Assert.False(json.GetProperty("supported").GetBoolean());
        var reason = json.GetProperty("reason").GetString();
        Assert.Contains("manually", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Velocity_Server_Detects_Newer_Build()
    {
        var client = NewClient(new FakeProfileService());
        var name = UniqueName("upd-velocity");
        var token = await CreateJavaServerAsync(client, name);
        PlantJar(name, "velocity-3.4.0-566.jar");
        await SetConfigSectionAsync(name, "java", new Dictionary<string, string>
        {
            ["jarfile"] = "velocity-3.4.0-566.jar"
        });

        var json = await GetUpdatesJsonAsync(client, token, name);

        Assert.True(json.GetProperty("supported").GetBoolean());
        Assert.Equal("velocity", json.GetProperty("family").GetString());
        Assert.True(json.GetProperty("updateAvailable").GetBoolean());
        Assert.Equal(570, json.GetProperty("latestBuildNumber").GetInt32());
    }

    [Fact]
    public async Task BungeeCord_Server_On_Latest_Build_Has_No_Update()
    {
        var client = NewClient(new FakeProfileService());
        var name = UniqueName("upd-bungee");
        var token = await CreateJavaServerAsync(client, name);
        PlantJar(name, "bungeecord-build-2131.jar");
        await SetConfigSectionAsync(name, "java", new Dictionary<string, string>
        {
            ["jarfile"] = "bungeecord-build-2131.jar"
        });

        var json = await GetUpdatesJsonAsync(client, token, name);

        Assert.True(json.GetProperty("supported").GetBoolean());
        Assert.False(json.GetProperty("updateAvailable").GetBoolean());
    }

    [Fact]
    public async Task Bedrock_Server_With_Recorded_Version_Detects_Update()
    {
        var client = NewClient(new FakeProfileService());
        var name = UniqueName("upd-bedrock");
        var token = await CreateBedrockServerAsync(client, name);
        // The version a guided apply installed is recorded in [updates]; this is
        // how detection works for servers whose binary carries no version marker.
        await SetConfigSectionAsync(name, "updates", new Dictionary<string, string>
        {
            ["applied_version"] = "1.21.40.9"
        });

        var json = await GetUpdatesJsonAsync(client, token, name);

        Assert.True(json.GetProperty("supported").GetBoolean());
        Assert.Equal("bedrock", json.GetProperty("family").GetString());
        Assert.Equal("1.21.40.9", json.GetProperty("currentVersion").GetString());
        Assert.True(json.GetProperty("updateAvailable").GetBoolean());
        Assert.Equal("bedrock-server-1.21.50.10", json.GetProperty("latestBuildProfileId").GetString());
    }

    [Fact]
    public async Task Bedrock_Without_Version_Info_Allows_Manual_Apply()
    {
        var client = NewClient(new FakeProfileService());
        var name = UniqueName("upd-bedrock-x");
        var token = await CreateBedrockServerAsync(client, name);

        var json = await GetUpdatesJsonAsync(client, token, name);

        // Still supported (the user may force the latest zip), but no badge:
        // we cannot honestly claim an update exists when current is unknown.
        Assert.True(json.GetProperty("supported").GetBoolean());
        Assert.Null(json.GetProperty("currentVersion").GetString());
        Assert.False(json.GetProperty("updateAvailable").GetBoolean());
    }

    [Fact]
    public async Task Vanilla_Server_On_Older_Version_Badges_Version_Jump()
    {
        // Vanilla has no per-version builds: the only offer IS a version jump,
        // so the badge must fire on it (unlike paper, whose badge stays on
        // same-MC-version builds until the user opts into a jump).
        var client = NewClient(new FakeProfileService());
        var name = UniqueName("upd-vanilla");
        var token = await CreateJavaServerAsync(client, name);
        PlantJar(name, "vanilla-1.21.10.jar");
        await SetConfigSectionAsync(name, "java", new Dictionary<string, string>
        {
            ["jarfile"] = "vanilla-1.21.10.jar"
        });

        var json = await GetUpdatesJsonAsync(client, token, name);

        Assert.True(json.GetProperty("supported").GetBoolean());
        Assert.Equal("vanilla", json.GetProperty("family").GetString());
        Assert.Equal("1.21.10", json.GetProperty("currentVersion").GetString());
        Assert.True(json.GetProperty("updateAvailable").GetBoolean());
        Assert.Equal("1.21.11", json.GetProperty("jumpVersion").GetString());
    }

    [Fact]
    public async Task Clone_Does_Not_Inherit_Update_Settings()
    {
        var client = NewClient(new FakeProfileService());
        var name = UniqueName("upd-clone-src");
        var token = await CreateJavaServerAsync(client, name);
        PlantJar(name, "paper-1.21.11-132.jar");
        await SetConfigSectionAsync(name, "java", new Dictionary<string, string> { ["jarfile"] = "paper-1.21.11-132.jar" });
        using var putOff = AuthRequest(HttpMethod.Put, $"/api/v1/servers/{name}/updates/mode", token,
            JsonContent.Create(new { mode = "off" }));
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(putOff)).StatusCode);

        var cloneName = UniqueName("upd-clone-dst");
        using var cloneRequest = AuthRequest(HttpMethod.Post, $"/api/v1/servers/{name}/clone", token,
            JsonContent.Create(new { newName = cloneName }));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(cloneRequest)).StatusCode);

        var cloneToken = token;
        var cloneUpdates = await GetUpdatesJsonAsync(client, cloneToken, cloneName);
        Assert.Equal("notify", cloneUpdates.GetProperty("mode").GetString());
    }

    // ------------------------------------------------------------------
    // Modes / dismissal
    // ------------------------------------------------------------------

    [Fact]
    public async Task Put_Mode_Off_Persists_And_Suppresses_Badge()
    {
        var client = NewClient(new FakeProfileService());
        var name = UniqueName("upd-mode-off");
        var token = await CreateJavaServerAsync(client, name);
        PlantJar(name, "paper-1.21.11-132.jar");
        await SetConfigSectionAsync(name, "java", new Dictionary<string, string> { ["jarfile"] = "paper-1.21.11-132.jar" });

        using var putRequest = AuthRequest(HttpMethod.Put, $"/api/v1/servers/{name}/updates/mode", token,
            JsonContent.Create(new { mode = "off" }));
        var putResponse = await client.SendAsync(putRequest);
        Assert.Equal(HttpStatusCode.NoContent, putResponse.StatusCode);

        var json = await GetUpdatesJsonAsync(client, token, name);
        Assert.Equal("off", json.GetProperty("mode").GetString());
        Assert.False(json.GetProperty("updateAvailable").GetBoolean());
    }

    [Fact]
    public async Task Ignore_Current_Suppresses_Until_Newer_Build_Arrives()
    {
        var profiles = new FakeProfileService();
        var client = NewClient(profiles);
        var name = UniqueName("upd-ignore");
        var token = await CreateJavaServerAsync(client, name);
        PlantJar(name, "paper-1.21.11-132.jar");
        await SetConfigSectionAsync(name, "java", new Dictionary<string, string> { ["jarfile"] = "paper-1.21.11-132.jar" });

        using var putRequest = AuthRequest(HttpMethod.Put, $"/api/v1/servers/{name}/updates/mode", token,
            JsonContent.Create(new { mode = "ignore-current" }));
        using var putResponse = await client.SendAsync(putRequest);
        Assert.Equal(HttpStatusCode.NoContent, putResponse.StatusCode);

        var dismissed = await GetUpdatesJsonAsync(client, token, name);
        Assert.False(dismissed.GetProperty("updateAvailable").GetBoolean());

        // A newer build lands upstream — the badge must come back.
        profiles.Profiles[0] = profiles.Profiles[0] with { Filename = "paper-1.21.11-141.jar" };
        var afterNewer = await GetUpdatesJsonAsync(client, token, name);
        Assert.True(afterNewer.GetProperty("updateAvailable").GetBoolean());
        Assert.Equal(141, afterNewer.GetProperty("latestBuildNumber").GetInt32());
    }

    [Fact]
    public async Task Ignore_Current_Without_Known_Update_Returns_400()
    {
        var client = NewClient(new FakeProfileService());
        var name = UniqueName("upd-ignore-none");
        var token = await CreateJavaServerAsync(client, name); // fresh, unsupported → nothing to ignore

        using var putRequest = AuthRequest(HttpMethod.Put, $"/api/v1/servers/{name}/updates/mode", token,
            JsonContent.Create(new { mode = "ignore-current" }));
        var response = await client.SendAsync(putRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_Mode_Invalid_Value_Returns_400()
    {
        var client = NewClient(new FakeProfileService());
        var name = UniqueName("upd-mode-bad");
        var token = await CreateJavaServerAsync(client, name);

        using var putRequest = AuthRequest(HttpMethod.Put, $"/api/v1/servers/{name}/updates/mode", token,
            JsonContent.Create(new { mode = "auto-update-everything" }));
        var response = await client.SendAsync(putRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------------
    // Apply
    // ------------------------------------------------------------------

    [Fact]
    public async Task Apply_While_Running_Returns_409()
    {
        var name = UniqueName("upd-apply-run");
        var processManager = new Mock<IProcessManager>();
        processManager.Setup(pm => pm.GetServerProcess(name)).Returns(new ServerProcessInfo(1234, 5678));
        var client = NewClient(new FakeProfileService(), processManager);
        var token = await CreateJavaServerAsync(client, name);
        PlantJar(name, "paper-1.21.11-132.jar");
        await SetConfigSectionAsync(name, "java", new Dictionary<string, string> { ["jarfile"] = "paper-1.21.11-132.jar" });

        using var request = AuthRequest(HttpMethod.Post, $"/api/v1/servers/{name}/updates/apply", token,
            JsonContent.Create(new { profileId = "paper-1.21.11" }));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Apply_Build_Bump_Swaps_Jar_Updates_Config_And_Backups_Old()
    {
        var client = NewClient(new FakeProfileService());
        var name = UniqueName("upd-apply-jar");
        var token = await CreateJavaServerAsync(client, name);
        PlantJar(name, "paper-1.21.11-132.jar");
        await SetConfigSectionAsync(name, "java", new Dictionary<string, string> { ["jarfile"] = "paper-1.21.11-132.jar" });
        File.Delete(Path.Combine(ServerDir(name), ".mineos-restart-required"));

        using var request = AuthRequest(HttpMethod.Post, $"/api/v1/servers/{name}/updates/apply", token,
            JsonContent.Create(new { profileId = "paper-1.21.11" }));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // New jar is on disk under its own build-numbered filename.
        Assert.True(File.Exists(Path.Combine(ServerDir(name), "paper-1.21.11-140.jar")));
        // The previously configured jar was kept as a one-generation rollback.
        Assert.True(File.Exists(Path.Combine(ServerDir(name), "paper-1.21.11-132.jar.bak")));
        Assert.False(File.Exists(Path.Combine(ServerDir(name), "paper-1.21.11-132.jar")));
        // Config now points at the new jar.
        var configText = await File.ReadAllTextAsync(Path.Combine(ServerDir(name), "server.config"));
        var sections = IniParser.ParseWithSections(configText);
        Assert.Equal("paper-1.21.11-140.jar", sections["java"]["jarfile"]);
        // And the restart flag tells the user the change needs a restart.
        Assert.True(File.Exists(Path.Combine(ServerDir(name), ".mineos-restart-required")));
    }

    [Fact]
    public async Task Apply_Cross_Family_Profile_Returns_400()
    {
        var client = NewClient(new FakeProfileService());
        var name = UniqueName("upd-apply-xfam");
        var token = await CreateJavaServerAsync(client, name);
        PlantJar(name, "paper-1.21.11-132.jar");
        await SetConfigSectionAsync(name, "java", new Dictionary<string, string> { ["jarfile"] = "paper-1.21.11-132.jar" });

        using var request = AuthRequest(HttpMethod.Post, $"/api/v1/servers/{name}/updates/apply", token,
            JsonContent.Create(new { profileId = "velocity-3.4.0" }));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Apply_Bedrock_Zip_Extracts_And_Records_Version()
    {
        var client = NewClient(new FakeProfileService());
        var name = UniqueName("upd-apply-br");
        var token = await CreateBedrockServerAsync(client, name);
        await SetConfigSectionAsync(name, "updates", new Dictionary<string, string>
        {
            ["applied_version"] = "1.21.40.9"
        });

        using var request = AuthRequest(HttpMethod.Post, $"/api/v1/servers/{name}/updates/apply", token,
            JsonContent.Create(new { profileId = "bedrock-server-1.21.50.10" }));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The zip's contents landed in the server directory.
        Assert.True(File.Exists(Path.Combine(ServerDir(name), "bedrock_server")));
        // The applied version is recorded so future detection keeps working.
        var configText = await File.ReadAllTextAsync(Path.Combine(ServerDir(name), "server.config"));
        var sections = IniParser.ParseWithSections(configText);
        Assert.Equal("1.21.50.10", sections["updates"]["applied_version"]);
        Assert.True(File.Exists(Path.Combine(ServerDir(name), ".mineos-restart-required")));
    }

    [Fact]
    public async Task Apply_Unknown_Profile_Returns_400()
    {
        var client = NewClient(new FakeProfileService());
        var name = UniqueName("upd-apply-unk");
        var token = await CreateJavaServerAsync(client, name);
        PlantJar(name, "paper-1.21.11-132.jar");
        await SetConfigSectionAsync(name, "java", new Dictionary<string, string> { ["jarfile"] = "paper-1.21.11-132.jar" });

        using var request = AuthRequest(HttpMethod.Post, $"/api/v1/servers/{name}/updates/apply", token,
            JsonContent.Create(new { profileId = "paper-9.9.9" }));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------------
    // Persistence safety
    // ------------------------------------------------------------------

    [Fact]
    public async Task Updates_Settings_Survive_Server_Config_Round_Trip()
    {
        var client = NewClient(new FakeProfileService());
        var name = UniqueName("upd-roundtrip");
        var token = await CreateJavaServerAsync(client, name);
        PlantJar(name, "paper-1.21.11-132.jar");
        await SetConfigSectionAsync(name, "java", new Dictionary<string, string> { ["jarfile"] = "paper-1.21.11-132.jar" });

        using var putMode = AuthRequest(HttpMethod.Put, $"/api/v1/servers/{name}/updates/mode", token,
            JsonContent.Create(new { mode = "off" }));
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(putMode)).StatusCode);

        // A normal config edit (full-replace writer) must not wipe [updates].
        using var getConfig = AuthRequest(HttpMethod.Get, $"/api/v1/servers/{name}/server-config", token);
        var configJson = await (await client.SendAsync(getConfig)).Content.ReadFromJsonAsync<JsonElement>();
        using var putConfig = AuthRequest(HttpMethod.Put, $"/api/v1/servers/{name}/server-config", token,
            JsonContent.Create(configJson));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(putConfig)).StatusCode);

        var updates = await GetUpdatesJsonAsync(client, token, name);
        Assert.Equal("off", updates.GetProperty("mode").GetString());
    }
}
