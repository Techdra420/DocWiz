using DocWiz.App;
using DocWiz.Core.Loading;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// ADR-008 parity gate (audit F2): every chapter-creation path must emit frontmatter in the
/// ONE canonical form the editor's save produces (block-style YAML via
/// <see cref="ChapterYaml.Compose"/>), so a freshly-created chapter is byte-stable on its first
/// save instead of silently restyling (the "frontmatter restyles on save" the L1 fidelity scan
/// counts). Each test creates a chapter, then asserts a no-edit editor save rewrites the exact
/// same bytes.
/// </summary>
public class FrontmatterEmissionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "docwiz-fm-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>Exactly what the editor writes on a no-edit save: it serves and re-saves the
    /// body as <c>rawBody.TrimStart('\n')</c> (EditorServer.cs — the body the surface edits and
    /// the save composes back).</summary>
    private static string ResaveViaEditor(string fileContent)
    {
        var (yaml, rawBody) = FrontmatterParser.Split(fileContent);
        return ChapterYaml.Compose(ChapterYaml.Parse(yaml), (rawBody ?? "").TrimStart('\n'));
    }

    [Fact]
    public void ChapterTemplatesFile_IsByteStableThroughEditorSave()
    {
        var created = ChapterTemplates.ChapterFile(
            "system-overview", "System Overview", "overview",
            new[] { "user", "support", "admin" });
        Assert.Equal(created, ResaveViaEditor(created));
    }

    [Fact]
    public void ImportedChapter_IsByteStableThroughEditorSave()
    {
        var path = ChapterFactory.CreateImported(_root, "010-overview", "intro", "Intro",
            "overview", new[] { "operator" }, "# Intro\n\nImported body.\n");
        var created = File.ReadAllText(path);
        Assert.Equal(created, ResaveViaEditor(created));
    }

    [Fact]
    public void ScaffoldedSeedChapter_IsByteStableThroughEditorSave()
    {
        ProjectScaffolder.CreateNew(_root);
        var seed = Path.Combine(_root, "content", "010-overview", "010-introduction.md");
        var created = File.ReadAllText(seed);
        // Body markdown must land at column 0 (guards the raw-string indentation when the
        // frontmatter was split out into ChapterComposer).
        Assert.Contains("\n## What this system does {#what-it-does}\n", created);
        Assert.Equal(created, ResaveViaEditor(created));
    }
}
