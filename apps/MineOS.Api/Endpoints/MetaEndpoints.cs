using Microsoft.AspNetCore.Mvc;

namespace MineOS.Api.Endpoints;

public static class MetaEndpoints
{
    public static IEndpointRouteBuilder MapMetaEndpoints(this IEndpointRouteBuilder app)
    {
        var meta = app.MapGroup("/meta");

        meta.MapGet("", ([FromServices] IConfiguration configuration) =>
            {
                var version =
                    configuration["MINEOS_VERSION"]
                    ?? configuration["MINEOS_IMAGE_TAG"]
                    ?? "unknown";

                var installationId = configuration["MINEOS_INSTALLATION_ID"] ?? "";

                return Results.Ok(new { version, installationId });
            })
            .WithName("GetMineOSMeta")
            .WithSummary("Get MineOS version metadata");

        return app;
    }
}

