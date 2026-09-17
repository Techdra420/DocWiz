using System.Security.Cryptography;
using System.Text;
using DocWiz.App;
using DocWiz.Core.Loading;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// /api/editor-doc payload builder: pipeline-rendered per-block HTML for the live
/// surface — asset/attachment URLs absolute for the editor server, dw: links left
/// untouched (they are editor UI, not navigation, until M4), missing images as
/// placeholders, body hash for the M2 save conflict guard.
/// </summary>
public class EditorDocTests
{
    private static (DocWiz.Core.Models.DocProject Project, string File) LoadDemoProject()
    {
        var root = FindRepoRoot();
        var projectRoot = Path.Combine(root, "samples", "stockroom-demo");
        var project = ProjectLoader.Load(projectRoot);
        return (project, "content/010-overview/010-system-overview.md");
    }

    [Fact]
    public void Build_SegmentsAndRendersChapter()
    {
        var (project, file) = LoadDemoProject();
        var dto = EditorDoc.Build(project, file);

        Assert.Equal("system-overview", dto.Id);
        Assert.True(dto.Numbering);
        Assert.Equal(1, dto.ChapterNumber);
        Assert.NotEmpty(dto.Segments);

        // Heading html carries its anchor id (pipeline output, ADR-005 anchors).
        var heading = dto.Segments.First(s => s.BlockType == "heading" && s.Src.Contains("{#what-it-does}"));
        Assert.Contains("id=\"what-it-does\"", heading.Html);
    }

    [Fact]
    public void Build_RewritesAssetsForTheEditorServer_LeavesDwLinksAlone()
    {
        var (project, file) = LoadDemoProject();
        var dto = EditorDoc.Build(project, file);
        var html = string.Concat(dto.Segments.Where(s => s.Html is not null).Select(s => s.Html));

        // dashboard-placeholder.png now exists (added as a placeholder gate fixture so
        // the figure gate renders via FigureView, not AtomHtmlView). It must render
        // as a real /assets/ img, not a placeholder. The editor server rewrites the path.
        Assert.Contains("/assets/dashboard-placeholder.png", html);
        Assert.DoesNotContain("img-placeholder", html);
        // dw: links stay dw: (resolved popovers are M4; LinkRewriter's handover
        // hrefs would be wrong inside the editor).
        Assert.Contains("href=\"dw:installation#network\"", html);
    }

    [Fact]
    public void Build_BodyHash_MatchesServedBody()
    {
        var (project, file) = LoadDemoProject();
        var dto = EditorDoc.Build(project, file);

        var chapterPath = Path.Combine(project.Root, file);
        var (_, rawBody) = FrontmatterParser.Split(File.ReadAllText(chapterPath));
        var body = rawBody.TrimStart('\n');
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

        Assert.Equal(expected, dto.BodyHash);
        Assert.Equal(body, string.Concat(dto.Segments.Select(s => s.Src)));
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "DocWiz.sln")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }
}
