using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using Markdig;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// Locates repo files from a test (walks up from the test binary to the directory holding
/// DocWiz.sln). One source so the cross-language parity tests don't each re-implement the
/// walk — ADR-008 applies to test code too.
/// </summary>
internal static class TestRepo
{
    public static string Root()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "DocWiz.sln")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }

    public static string At(params string[] parts) =>
        Path.Combine(new[] { Root() }.Concat(parts).ToArray());

    /// <summary>Absolute path to the sample-project fixture (for tests that need the root, not the loaded project).</summary>
    public static string DemoProjectPath => At("samples", "stockroom-demo");

    public static string Read(params string[] parts) => File.ReadAllText(At(parts));

    /// <summary>Loads the sample-project fixture and parses all chapters through the one
    /// pipeline (ADR-004), returning both so link/anchor tests share one setup.</summary>
    public static (DocProject Project, MarkdownPipeline Pipeline) LoadDemoProject()
    {
        var project = ProjectLoader.Load(DemoProjectPath);
        var pipeline = DocWizMarkdown.CreatePipeline();
        DocWizMarkdown.ParseAll(project, pipeline);
        return (project, pipeline);
    }
}
