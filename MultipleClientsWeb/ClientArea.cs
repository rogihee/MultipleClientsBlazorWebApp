namespace MultipleClientsWeb;

/// <summary>
/// An area of the site that is run by its own WebAssembly client. Everything outside an area is static SSR.
/// </summary>
public sealed record ClientArea(string PathPrefix, string StaticWebAssetBasePath)
{
    public static readonly ClientArea Admin = new("/admin", "admin-client");
    public static readonly ClientArea Game = new("/game", "game-client");

    public static IReadOnlyList<ClientArea> All { get; } = [Admin, Game];

    public static ClientArea? FromPath(PathString path) =>
        All.FirstOrDefault(area => path.StartsWithSegments(area.PathPrefix, StringComparison.OrdinalIgnoreCase));
}
