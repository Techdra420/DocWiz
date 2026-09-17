using DocWiz.Build;
using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// The handover root holds only folders + one project-named entry html; every generated
/// page lives under _site/. These cover the entry-name derivation, the shared filename
/// sanitizer, and the on-disk output contract.
/// </summary>
public class FileNameSafeTests
{
    [Fact]
    public void CollapsesWhitespaceToDashes()
    {
        Assert.Equal("Stockroom-Inventory-System", Packager.FileNameSafe("Stockroom Inventory System"));
    }

    [Fact]
    public void CollapsesRunsOfWhitespaceAndTrims()
    {
        Assert.Equal("a-b", Packager.FileNameSafe("  a   b  "));
    }

    [Fact]
    public void ReplacesInvalidFileNameCharacters()
    {
        Assert.Equal("a-b-c", Packager.FileNameSafe("a/b:c"));
    }

    [Fact]
    public void FallsBackToHandoverWhenNothingUsableRemains()
    {
        Assert.Equal("handover", Packager.FileNameSafe("   "));
    }
}

public class EntryFileNameTests
{
    [Fact]
    public void UsesProjectTitleWhenPresent()
    {
        var m = new ProjectManifest { Project = "Stockroom Inventory System", Code = "SR-DEMO-001" };
        Assert.Equal("Stockroom-Inventory-System.html", HandoverBuilder.EntryFileName(m));
    }

    [Fact]
    public void FallsBackToCodeWhenTitleBlank()
    {
        var m = new ProjectManifest { Project = "  ", Code = "SR-DEMO-001" };
        Assert.Equal("SR-DEMO-001.html", HandoverBuilder.EntryFileName(m));
    }

    [Fact]
    public void FallsBackToHandoverWhenTitleAndCodeBlank()
    {
        var m = new ProjectManifest { Project = "", Code = "" };
        Assert.Equal("handover.html", HandoverBuilder.EntryFileName(m));
    }
}

/// <summary>The on-disk output contract: one project-named html in the root, everything
/// else under _site/, and chrome links prefixed correctly for each context.</summary>
public class HandoverLayoutBuildTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "docwiz-clean-root-" + Guid.NewGuid().ToString("N"));
    private readonly string _outDir;
    private const string EntryFile = "Stockroom-Inventory-System.html";

    public HandoverLayoutBuildTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "content", "010-system"));
        Directory.CreateDirectory(Path.Combine(_root, "views"));
        Directory.CreateDirectory(Path.Combine(_root, "assets"));
        Directory.CreateDirectory(Path.Combine(_root, "attachments"));
        File.WriteAllText(Path.Combine(_root, "attachments", "datasheet.pdf"), "stub");

        File.WriteAllText(Path.Combine(_root, "docwiz.yaml"),
            """
            project: Stockroom Inventory System
            code: SR-DEMO-001
            audiences:
              - id: user
                title: User
            """);
        File.WriteAllText(Path.Combine(_root, "assets", "present.png"), "stub");
        File.WriteAllText(Path.Combine(_root, "content", "010-system", "010-system.md"),
            """
            ---
            id: system
            title: System
            audiences: [user]
            ---

            # System

            ## Procedure {#procedure}

            Sign out of the station.

            ![Screen](assets/present.png){width=320}
            """);
        File.WriteAllText(Path.Combine(_root, "views", "training.yaml"),
            """
            id: training
            title: User Training
            audience: user
            mode: slides
            slides:
              - layout: title-content
                title: Step
                regions:
                  body: |
                    ::: embed dw:system#procedure
                    :::
            """);

        var project = ProjectLoader.Load(_root);
        var pipeline = DocWizMarkdown.CreatePipeline();
        DocWizMarkdown.ParseAll(project, pipeline);
        _outDir = Path.Combine(_root, "output", "handover");
        HandoverBuilder.Build(project, pipeline, _outDir);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Root_HoldsExactlyOneHtml_NamedAfterProject()
    {
        var rootHtml = Directory.GetFiles(_outDir, "*.html");
        Assert.Single(rootHtml);
        Assert.Equal(EntryFile, Path.GetFileName(rootHtml[0]));
    }

    [Fact]
    public void GeneratedPagesLiveUnderSite_notRoot()
    {
        Assert.True(File.Exists(Path.Combine(_outDir, "_site", "system.html")));
        Assert.True(File.Exists(Path.Combine(_outDir, "_site", "view-training.html")));
        Assert.False(File.Exists(Path.Combine(_outDir, "system.html")));
        Assert.False(File.Exists(Path.Combine(_outDir, "index.html")));
        Assert.False(File.Exists(Path.Combine(_outDir, "_site", "index.html")));
    }

    [Fact]
    public void AssetsAndDwCopiedUnderSite()
    {
        Assert.True(File.Exists(Path.Combine(_outDir, "_site", "_dw", "tokens.css")));
        Assert.True(File.Exists(Path.Combine(_outDir, "_site", "_dw", "search-index.js")));
        Assert.True(File.Exists(Path.Combine(_outDir, "_site", "assets", "present.png")));
    }

    [Fact]
    public void EntryPage_LinksDownIntoSite()
    {
        var html = File.ReadAllText(Path.Combine(_outDir, EntryFile));
        Assert.Contains("_site/_dw/tokens.css", html);          // own chrome resources
        Assert.Contains("_site/system.html", html);               // contents/nav into a chapter
        Assert.Contains("_site/view-training.html", html);      // view tab
        Assert.Contains("window.__dwBase = \"_site/\"", html);  // search base
    }

    [Fact]
    public void SitePage_LinksSideways_andHomeGoesUp()
    {
        var html = File.ReadAllText(Path.Combine(_outDir, "_site", "system.html"));
        Assert.Contains("\"_dw/tokens.css\"", html);            // sibling chrome resources
        Assert.Contains("assets/present.png", html);            // sibling asset (LinkRewriter untouched)
        Assert.Contains($"\"../{EntryFile}\"", html);           // Manual/home tab points up to the entry
        Assert.Contains("window.__dwBase = \"\"", html);        // search base empty
        Assert.DoesNotContain("href=\"_site/", html);           // a site page never prefixes links with _site/
        Assert.DoesNotContain("src=\"_site/", html);
    }

    [Fact]
    public void AttachmentsPage_RendersInSiteContext()
    {
        var path = Path.Combine(_outDir, "_site", "attachments.html");
        Assert.True(File.Exists(path));
        var html = File.ReadAllText(path);
        Assert.Contains("attachments/datasheet.pdf", html);     // bare sibling attachment link
        Assert.Contains($"\"../{EntryFile}\"", html);           // Manual/home tab points up
        Assert.DoesNotContain("href=\"_site/", html);
    }

    [Fact]
    public void NoUnresolvedBaseTokensRemain()
    {
        foreach (var f in Directory.GetFiles(_outDir, "*.html", SearchOption.AllDirectories))
        {
            var html = File.ReadAllText(f);
            Assert.DoesNotContain("{{BASE}}", html);
            Assert.DoesNotContain("{{HOME}}", html);
        }
    }
}

/// <summary>The editor opens a bundled external doc at /handover/&lt;_site&gt;/external/...;
/// that JS path literal must track HandoverBuilder.SiteFolder (ADR-008 — pin, don't comment).</summary>
public class EditorSiteFolderParityTests
{
    [Fact]
    public void ExternalDocOpenUsesBuildSiteFolder()
    {
        var appJs = TestRepo.Read("src", "DocWiz.App", "wwwroot", "app.js");
        Assert.Contains($"/handover/{HandoverBuilder.SiteFolder}/external/", appJs);
    }
}

public class ZipNameSharesSanitizerTests
{
    [Fact]
    public void ExistingDashedCodeUnchanged()
    {
        Assert.Equal("SR-DEMO-001-v0.1.0.zip", Packager.ZipName("SR-DEMO-001", "0.1.0"));
    }

    [Fact]
    public void CodeWithSpacesNowCollapsesToDashes()
    {
        Assert.Equal("A-B-v1.0.0.zip", Packager.ZipName("A B", "1.0.0"));
    }

    [Fact]
    public void BlankCodeFallsBackToHandover()
    {
        Assert.Equal("handover-v1.0.0.zip", Packager.ZipName("   ", "1.0.0"));
    }
}
