namespace MineOS.Api.Authorization;

public enum ServerPermission
{
    View,
    Control,
    Console
}

public sealed class ServerAccessRequirement
{
    public ServerAccessRequirement(ServerPermission permission)
    {
        Permission = permission;
    }

    public ServerPermission Permission { get; }
}
