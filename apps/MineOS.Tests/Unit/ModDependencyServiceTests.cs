using Microsoft.Extensions.Logging.Abstractions;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Infrastructure.Services;
using Moq;

namespace MineOS.Tests.Unit;

/// <summary>
/// Covers single-mod dependency resolution (#21).
///
/// The behaviour worth guarding is what happens when resolution *cannot* succeed:
/// a mod installed without its hard dependency does not warn at startup, it stops
/// the server booting, so an unresolvable dependency has to refuse the whole
/// install rather than write part of it.
/// </summary>
public class ModDependencyServiceTests
{
    private static ModrinthVersionDto Version(
        string id,
        string projectId,
        string name = "mod",
        string[]? gameVersions = null,
        string[]? loaders = null,
        ModrinthDependencyDto[]? dependencies = null) =>
        new(id, projectId, name, "1.0.0", DateTimeOffset.Parse("2026-01-01T00:00:00Z"), 0,
            gameVersions ?? new[] { "1.21.1" },
            loaders ?? new[] { "fabric" },
            new[] { new ModrinthVersionFileDto($"https://cdn/{id}.jar", $"{name}-{id}.jar", 10, true) },
            dependencies ?? Array.Empty<ModrinthDependencyDto>());

    private sealed class Harness
    {
        public Mock<IModrinthService> Modrinth { get; } = new(MockBehavior.Loose);
        public Mock<IModService> Mods { get; } = new(MockBehavior.Loose);
        public Mock<IServerService> Servers { get; } = new(MockBehavior.Loose);

        public Harness(params ModrinthVersionDto[] catalogue)
        {
            Servers.Setup(s => s.DetectLoaderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ServerLoaderDto("fabric", "0.19.3", "1.21.1"));

            Mods.Setup(m => m.ListModsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<InstalledModDto>());

            Modrinth.Setup(m => m.GetVersionsByFileHashesAsync(
                    It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<string, ModrinthVersionDto>());

            Modrinth.Setup(m => m.GetProjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string id, CancellationToken _) =>
                    new ModrinthProjectDto(
                        id, id, id, "", null, "mod", 0,
                        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                        null, null, null));

            foreach (var version in catalogue)
            {
                var captured = version;
                Modrinth.Setup(m => m.GetVersionAsync(captured.Id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(captured);
                Modrinth.Setup(m => m.GetProjectVersionsAsync(
                        captured.ProjectId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new[] { captured });
            }
        }

        public ModDependencyService Build() => new(
            Modrinth.Object, Mods.Object, Servers.Object,
            NullLogger<ModDependencyService>.Instance);
    }

    private static ModrinthDependencyDto Required(string projectId) => new(projectId, null, "required");
    private static ModrinthDependencyDto Optional(string projectId) => new(projectId, null, "optional");

    [Fact]
    public async Task A_Mod_With_No_Dependencies_Plans_Only_Itself()
    {
        var harness = new Harness(Version("v1", "root"));

        var plan = await harness.Build().PlanInstallAsync("srv", "v1", CancellationToken.None);

        Assert.Empty(plan.Required);
        Assert.Empty(plan.Optional);
        Assert.Empty(plan.Problems);
    }

    [Fact]
    public async Task Hard_Dependencies_Are_Planned_Including_Their_Own()
    {
        // root -> api -> core: the nested case, which is the one that actually
        // breaks servers when it is missed.
        var harness = new Harness(
            Version("v1", "root", dependencies: new[] { Required("api") }),
            Version("v2", "api", "fabric-api", dependencies: new[] { Required("core") }),
            Version("v3", "core", "core-lib"));

        var plan = await harness.Build().PlanInstallAsync("srv", "v1", CancellationToken.None);

        Assert.Equal(new[] { "api", "core" }, plan.Required.Select(r => r.ProjectId));
        Assert.Empty(plan.Problems);
    }

    [Fact]
    public async Task A_Dependency_Cycle_Terminates()
    {
        var harness = new Harness(
            Version("v1", "root", dependencies: new[] { Required("a") }),
            Version("v2", "a", "a", dependencies: new[] { Required("b") }),
            Version("v3", "b", "b", dependencies: new[] { Required("a") }));

        var plan = await harness.Build().PlanInstallAsync("srv", "v1", CancellationToken.None);

        Assert.Equal(new[] { "a", "b" }, plan.Required.Select(r => r.ProjectId));
    }

    [Fact]
    public async Task Optional_Dependencies_Are_Offered_But_Not_Planned_For_Install()
    {
        var harness = new Harness(
            Version("v1", "root", dependencies: new[] { Required("api"), Optional("extras") }),
            Version("v2", "api", "fabric-api"),
            Version("v3", "extras", "extras"));

        var plan = await harness.Build().PlanInstallAsync("srv", "v1", CancellationToken.None);

        Assert.Equal(new[] { "api" }, plan.Required.Select(r => r.ProjectId));
        Assert.Equal(new[] { "extras" }, plan.Optional.Select(o => o.ProjectId));
    }

    [Fact]
    public async Task An_Already_Installed_Dependency_Is_Reported_Not_Reinstalled()
    {
        var harness = new Harness(
            Version("v1", "root", dependencies: new[] { Required("api") }),
            Version("v2", "api", "fabric-api"));

        // A real file on disk: identification works by hashing the jar, so a
        // path that does not exist would skip the lookup entirely and test nothing.
        var jarPath = Path.Combine(Path.GetTempPath(), $"mineos-test-{Guid.NewGuid():N}.jar");
        await File.WriteAllBytesAsync(jarPath, new byte[] { 1, 2, 3 });

        harness.Mods.Setup(m => m.ListModsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new InstalledModDto("fabric-api.jar", 10, DateTimeOffset.UtcNow, false) });
        harness.Mods.Setup(m => m.GetModPathAsync("srv", "fabric-api.jar", It.IsAny<CancellationToken>()))
            .ReturnsAsync(jarPath);

        // The hash lookup is what identifies an installed jar; simulate a match.
        harness.Modrinth.Setup(m => m.GetVersionsByFileHashesAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ModrinthVersionDto>
            {
                ["deadbeef"] = Version("v2", "api", "fabric-api")
            });

        try
        {
            var plan = await harness.Build().PlanInstallAsync("srv", "v1", CancellationToken.None);

            Assert.Empty(plan.Required);
            Assert.Equal(new[] { "api" }, plan.AlreadyInstalled.Select(a => a.ProjectId));
        }
        finally
        {
            File.Delete(jarPath);
        }
    }

    [Fact]
    public async Task An_Unresolvable_Hard_Dependency_Becomes_A_Problem()
    {
        var harness = new Harness(Version("v1", "root", dependencies: new[] { Required("missing") }));
        harness.Modrinth.Setup(m => m.GetProjectVersionsAsync(
                "missing", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ModrinthVersionDto>());

        var plan = await harness.Build().PlanInstallAsync("srv", "v1", CancellationToken.None);

        Assert.Single(plan.Problems);
        Assert.Contains("fabric 1.21.1", plan.Problems[0]);
    }

    [Fact]
    public async Task Installing_Refuses_Entirely_When_A_Hard_Dependency_Cannot_Be_Resolved()
    {
        // The important one: a partial install is what leaves a server unable to
        // boot, and it is harder to diagnose than an outright refusal.
        var harness = new Harness(Version("v1", "root", dependencies: new[] { Required("missing") }));
        harness.Modrinth.Setup(m => m.GetProjectVersionsAsync(
                "missing", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ModrinthVersionDto>());

        var service = harness.Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.InstallWithDependenciesAsync("srv", "v1", Array.Empty<string>(), CancellationToken.None));

        harness.Mods.Verify(
            m => m.SaveModAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Dependencies_Are_Written_Before_The_Mod_That_Needs_Them()
    {
        var harness = new Harness(
            Version("v1", "root", dependencies: new[] { Required("api") }),
            Version("v2", "api", "fabric-api"));

        var order = new List<string>();
        harness.Mods.Setup(m => m.SaveModAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string file, Stream _, CancellationToken _) => order.Add(file))
            .Returns(Task.CompletedTask);
        harness.Modrinth.Setup(m => m.OpenDownloadStreamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(new byte[] { 1 }));

        await harness.Build().InstallWithDependenciesAsync("srv", "v1", Array.Empty<string>(), CancellationToken.None);

        Assert.Equal(2, order.Count);
        Assert.StartsWith("fabric-api-", order[0]);
        Assert.StartsWith("mod-v1", order[1]);
    }

    [Fact]
    public async Task An_Optional_Dependency_Is_Installed_Only_When_Approved()
    {
        var harness = new Harness(
            Version("v1", "root", dependencies: new[] { Optional("extras") }),
            Version("v3", "extras", "extras"));

        var saved = new List<string>();
        harness.Mods.Setup(m => m.SaveModAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string file, Stream _, CancellationToken _) => saved.Add(file))
            .Returns(Task.CompletedTask);
        harness.Modrinth.Setup(m => m.OpenDownloadStreamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(new byte[] { 1 }));

        var service = harness.Build();

        await service.InstallWithDependenciesAsync("srv", "v1", Array.Empty<string>(), CancellationToken.None);
        Assert.Single(saved);

        saved.Clear();
        await service.InstallWithDependenciesAsync("srv", "v1", new[] { "extras" }, CancellationToken.None);
        Assert.Equal(2, saved.Count);
    }

    [Fact]
    public async Task An_Optional_Project_The_Plan_Never_Offered_Is_Ignored()
    {
        // Approvals are checked against the plan so an arbitrary project id in the
        // request body cannot cause an unrelated download.
        var harness = new Harness(Version("v1", "root"));
        harness.Mods.Setup(m => m.SaveModAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        harness.Modrinth.Setup(m => m.OpenDownloadStreamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(new byte[] { 1 }));

        var result = await harness.Build()
            .InstallWithDependenciesAsync("srv", "v1", new[] { "totally-unrelated" }, CancellationToken.None);

        Assert.Single(result.InstalledFiles);
    }

    [Theory]
    [InlineData("1.21.1", "fabric", true)]
    [InlineData("1.20.1", "fabric", false)]
    [InlineData("1.21.1", "forge", false)]
    public void Compatibility_Checks_Both_Game_Version_And_Loader(
        string gameVersion, string loader, bool expected)
    {
        var version = Version("v", "p", gameVersions: new[] { "1.21.1" }, loaders: new[] { "fabric" });

        Assert.Equal(expected, ModDependencyService.IsCompatible(version, loader, gameVersion));
    }

    [Fact]
    public void A_Version_Declaring_Nothing_Is_Treated_As_Compatible()
    {
        // Modrinth leaves these empty for datapack-style projects; rejecting them
        // would block legitimate dependencies.
        var version = Version("v", "p", gameVersions: Array.Empty<string>(), loaders: Array.Empty<string>());

        Assert.True(ModDependencyService.IsCompatible(version, "fabric", "1.21.1"));
    }
}
