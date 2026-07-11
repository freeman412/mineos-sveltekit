using System;
using System.Linq;
using System.Reflection;
using MineOS.Api.Endpoints;
using MineOS.Application.Interfaces;
using MineOS.Domain.Entities;
using MineOS.Infrastructure.Services;
using Xunit;

namespace MineOS.Tests.Architecture;

/// <summary>
/// Enforces the Clean Architecture dependency rule at build time: dependencies point
/// INWARD only. Domain (innermost) knows nothing of the outer layers or of any
/// delivery/persistence framework; Application depends only on Domain; Infrastructure
/// and Api implement and compose the inner layers.
///
/// This is the rule a contributor's AI agent must never violate. Unlike a written
/// guideline, a violation fails these tests red — the ".NET tests must pass locally"
/// gate then blocks it from being called done.
///
/// Reflection-only by design (no NetArchTest dependency — a test that enforces
/// dependency hygiene should not add one): each layer is anchored by a real public
/// type, and assertions read Assembly.GetReferencedAssemblies(), which reflects the
/// actual compiled metadata references, i.e. both project references and framework
/// package references that are genuinely used.
/// </summary>
public class LayerDependencyTests
{
    private static readonly Assembly Domain = typeof(MinecraftServer).Assembly;
    private static readonly Assembly Application = typeof(IServerService).Assembly;
    private static readonly Assembly Infrastructure = typeof(ServerService).Assembly;
    private static readonly Assembly Api = typeof(WorldEndpoints).Assembly;

    private static void AssertDoesNotReference(Assembly assembly, params string[] forbiddenPrefixes)
    {
        var violations = assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => forbiddenPrefixes.Any(prefix =>
                name.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .OrderBy(name => name)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"{assembly.GetName().Name} must not depend on [{string.Join(", ", forbiddenPrefixes)}] " +
            $"but references: {string.Join(", ", violations)}. " +
            "Dependencies must point inward: Api -> Infrastructure -> Application -> Domain.");
    }

    [Fact]
    public void Domain_is_pure_and_depends_on_nothing_outward()
    {
        // The innermost layer: no outer MineOS layer, and no web/persistence framework.
        AssertDoesNotReference(Domain,
            "MineOS.Application", "MineOS.Infrastructure", "MineOS.Api",
            "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", "Npgsql");
    }

    [Fact]
    public void Application_depends_only_on_Domain()
    {
        // Business rules and the interfaces they define never reach out to Infrastructure
        // or Api, and never bind to a delivery (ASP.NET) or persistence (EF/Npgsql) framework.
        AssertDoesNotReference(Application,
            "MineOS.Infrastructure", "MineOS.Api",
            "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", "Npgsql");
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_Api()
    {
        // Infrastructure implements Application's interfaces; the composition root (Api)
        // depends on Infrastructure, never the reverse.
        AssertDoesNotReference(Infrastructure, "MineOS.Api");
    }
}
