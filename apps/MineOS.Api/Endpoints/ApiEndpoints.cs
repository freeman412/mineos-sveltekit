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
        api.MapProfileEndpoints();
        api.MapJobEndpoints();

        return app;
    }
}
