using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineOS.Application.Interfaces;
using MineOS.Infrastructure.Persistence;
using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Integration;

public class DiContainerTests : IClassFixture<MineOsWebApplicationFactory>
{
    private readonly MineOsWebApplicationFactory _factory;

    public DiContainerTests(MineOsWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public void Host_Starts_Without_DI_Errors()
    {
        // WebApplicationFactory.CreateClient triggers full host startup,
        // including DI validation in Development/Testing mode.
        using var client = _factory.CreateClient();
        Assert.NotNull(client);
    }

    [Theory]
    [InlineData(typeof(ICronService))]
    [InlineData(typeof(IFeatureUsageTracker))]
    [InlineData(typeof(IDeviceAuthService))]
    [InlineData(typeof(ITelemetryReportTrigger))]
    [InlineData(typeof(IDbContextFactory<AppDbContext>))]
    [InlineData(typeof(IServerService))]
    [InlineData(typeof(IBackupService))]
    [InlineData(typeof(IAuthService))]
    public void Can_Resolve_Service(Type serviceType)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetService(serviceType);
        Assert.NotNull(service);
    }

    [Fact]
    public void Can_Resolve_DbContextFactory_As_Singleton()
    {
        var factory1 = _factory.Services.GetService<IDbContextFactory<AppDbContext>>();
        var factory2 = _factory.Services.GetService<IDbContextFactory<AppDbContext>>();
        Assert.NotNull(factory1);
        Assert.Same(factory1, factory2);
    }

    [Fact]
    public void Can_Create_DbContext_From_Factory()
    {
        var factory = _factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var context = factory.CreateDbContext();
        Assert.NotNull(context);
    }
}
