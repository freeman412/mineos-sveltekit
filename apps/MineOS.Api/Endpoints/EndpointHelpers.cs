namespace MineOS.Api.Endpoints;

internal static class EndpointHelpers
{
    public static IResult NotImplementedFeature(string feature) =>
        Results.Problem($"Not implemented: {feature}", statusCode: StatusCodes.Status501NotImplemented);
}
