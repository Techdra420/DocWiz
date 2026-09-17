namespace DocWiz.Core.Models;

/// <summary>The structured slide layouts and their region names — the single source of
/// truth shared by the renderer, validator, and editor. Region order is render order.</summary>
public static class SlideLayout
{
    public const string TitleContent = "title-content";
    public const string TwoColumn = "two-column";
    public const string BigMedia = "big-media";
    public const string SectionHeader = "section-header";

    /// <summary>Region names — the single source of truth shared by the renderer,
    /// validator, drafter, and the legacy-include migration, so a region key is never a
    /// bare string literal that can drift from the layout table below.</summary>
    public const string BodyRegion = "body";
    public const string MediaRegion = "media";
    public const string LeftRegion = "left";
    public const string RightRegion = "right";

    private static readonly Dictionary<string, string[]> RegionNames = new(StringComparer.Ordinal)
    {
        [TitleContent] = [BodyRegion],
        [TwoColumn] = [LeftRegion, RightRegion],
        [BigMedia] = [MediaRegion],
        [SectionHeader] = [],
    };

    public static bool IsKnown(string layout) => RegionNames.ContainsKey(layout);

    /// <summary>Valid region names for a layout, in render order. Empty for unknown layouts.</summary>
    public static IReadOnlyList<string> RegionsFor(string layout) =>
        RegionNames.TryGetValue(layout, out var names) ? names : [];
}
