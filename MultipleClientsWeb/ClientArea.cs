namespace MultipleClientsWeb;

/// <summary>
/// An area of the site that is run by its own WebAssembly client. Everything outside an area is static SSR.
/// </summary>
/// <param name="RequiredRole">Role needed to download the client, or <c>null</c> for any logged-in user.
/// Keep it in line with the <c>[Authorize]</c> attributes of the client's pages.</param>
public sealed record ClientArea(string PathPrefix, string StaticWebAssetBasePath, string? RequiredRole = null)
{
    public static readonly ClientArea Admin = new("/admin", "admin-client", "Admin");
    public static readonly ClientArea Game = new("/game", "game-client");

    public static IReadOnlyList<ClientArea> All { get; } = [Admin, Game];

    public static ClientArea? FromPath(PathString path) =>
        All.FirstOrDefault(area => path.StartsWithSegments(area.PathPrefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>The area whose static web assets (<c>/admin-client/...</c>) contain the path.</summary>
    public static ClientArea? FromAssetPath(PathString path) =>
        All.FirstOrDefault(area => path.StartsWithSegments($"/{area.StaticWebAssetBasePath}", StringComparison.OrdinalIgnoreCase));
}
