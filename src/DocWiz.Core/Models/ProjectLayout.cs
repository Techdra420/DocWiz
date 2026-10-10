namespace DocWiz.Core.Models;

/// <summary>
/// The on-disk layout of a DocWiz project (docs/CONTENT_MODEL.md) — the one
/// definition of the folder and file names every component composes paths from.
/// The generated handover mirrors the assets/attachments folder names under its
/// output directory (links rewrite relatively), and the editor serves them on
/// same-named URL routes, so builder and server use these constants too.
/// </summary>
public static class ProjectLayout
{
    public const string ContentFolder = "content";
    public const string AssetsFolder = "assets";
    public const string AttachmentsFolder = "attachments";
    public const string ViewsFolder = "views";
    public const string ManifestFileName = "docwiz.yaml";

    /// <summary>The editor's own data inside a project (never built, never shipped, git-ignored
    /// by a self-ignoring .gitignore inside it). Holds <see cref="HistoryFolder"/>.</summary>
    public const string EditorDataFolder = ".docwiz";

    /// <summary>Change history (Stage 6): snapshots, named versions, recently deleted files.</summary>
    public const string HistoryFolder = ".docwiz/history";

    /// <summary>The offline entry pages an external documentation package (ADR-006) may
    /// expose, in preference order: a navigable offline frame
    /// (handover.html — project tree + search + iframe) before the generic single-page
    /// convention (index.html). The one rule the validator, builder, and editor all defer
    /// to (ADR-008) — never hardcode the filename anywhere else.</summary>
    public static readonly string[] ExternalEntryCandidates = { "handover.html", "index.html" };

    /// <summary>The entry file an external doc package folder exposes (see
    /// <see cref="ExternalEntryCandidates"/>), or null when the folder has none.</summary>
    public static string? ResolveExternalEntry(string folder) =>
        ExternalEntryCandidates.FirstOrDefault(name => File.Exists(Path.Combine(folder, name)));

    /// <summary>The one rule for a valid external-doc package key (ADR-008): a plain lowercase
    /// identifier, because the key becomes a folder name (external/&lt;key&gt;/) and a generated
    /// file name (external-&lt;key&gt;.html) — anything with '..', '/', or '\' would escape the
    /// project (path traversal). Both the editor's add-package guard and the project validator
    /// defer to this single predicate.</summary>
    public static bool IsValidExternalKey(string key) =>
        !string.IsNullOrEmpty(key) && System.Text.RegularExpressions.Regex.IsMatch(key, "^[a-z0-9_]+$");
}
