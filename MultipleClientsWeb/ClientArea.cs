namespace MultipleClientsWeb;

/// <summary>
/// An area of the site that is run by its own WebAssembly client. Everything outside an area is static SSR.
/// </summary>
/// <param name="RequiredRole">Role needed to download the client, or <c>null</c> for any logged-in user.
/// Keep it in line with the <c>[Authorize]</c> attributes of the client's pages.</param>
public sealed record ClientArea(string PathPrefix, string StaticWebAssetBasePath, string? RequiredRole = null)
{
    public static readonly ClientArea Admin = new("/admin", "admin-client", "Admin") { Libraries = ["AdminOnlyLib"] };
    public static readonly ClientArea Game = new("/game", "game-client") { Libraries = ["GameOnlyLib"] };

    public static IReadOnlyList<ClientArea> All { get; } = [Admin, Game];

    /// <summary>
    /// Razor class libraries that only this client uses. Their <c>_content/{library}</c> files belong to the area like
    /// the client's own files. Keep the list in line with <c>AreaOnlyLibrary</c> in <c>MultipleClientsWeb.csproj</c>.
    /// </summary>
    public IReadOnlyList<string> Libraries { get; init; } = [];

    /// <summary>Url prefixes of the static web assets that belong to the area: the client and its own libraries.</summary>
    public IEnumerable<string> AssetBasePaths => [StaticWebAssetBasePath, .. Libraries.Select(library => $"_content/{library}")];

    /// <summary>
    /// Scoped CSS bundles of the area's libraries. The host's bundle leaves them out, so an area page links them itself.
    /// </summary>
    public IEnumerable<string> LibraryCssBundles => Libraries.Select(library => $"_content/{library}/{library}.bundle.scp.css");

    public static ClientArea? FromPath(PathString path) =>
        All.FirstOrDefault(area => path.StartsWithSegments(area.PathPrefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The area whose static web assets contain the path: <c>/admin-client/...</c> or <c>/_content/AdminOnlyLib/...</c>.
    /// </summary>
    public static ClientArea? FromAssetPath(PathString path) =>
        All.FirstOrDefault(area => area.AssetBasePaths.Any(basePath => path.StartsWithSegments($"/{basePath}", StringComparison.OrdinalIgnoreCase)));
}
