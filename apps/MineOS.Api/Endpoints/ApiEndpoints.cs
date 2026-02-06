namespace MineOS.Api.Endpoints;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1");

        api.MapHealthEndpoints();
        api.MapAuthEndpoints();
        api.MapHostEndpoints();
        api.MapServerEndpoints();
        api.MapWorldEndpoints();
        api.MapPerformanceEndpoints();
        api.MapPlayerEndpoints();
        api.MapProfileEndpoints();
        api.MapJobEndpoints();
        api.MapCurseForgeEndpoints();
        api.MapForgeEndpoints();
        api.MapAdminEndpoints();
        api.MapNotificationEndpoints();
        api.MapSettingsEndpoints();
        api.MapMetaEndpoints();
        api.MapAccountEndpoints();

        // Global watchdog endpoints (not per-server)
        if (app is WebApplication webApp)
        {
            webApp.MapGlobalWatchdogEndpoints();
        }

        return app;
    }
}
