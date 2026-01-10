namespace MineOS.Api.Middleware;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SkipApiKeyAttribute : Attribute
{
}
