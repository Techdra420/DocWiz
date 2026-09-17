using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// ADR-008 / docwiz-design rule 21 parity gate for the editor context-bar tool strips
/// (audit F1). The picture / table / in-cell-image / linked-block strips used to be hand-built
/// per node-view (five button idioms) and drifted — the IMAGE vs "Cell image" badge, Rotate on
/// the figure only, the box-placement control using text-align glyphs in the table but box
/// glyphs in the embed. The table/picture toolbar design pass collapsed all four into ONE
/// shared definition (<c>wwwroot/editor2/contexttools.js</c>): a control vocabulary, a single
/// BADGES map, a per-kind LAYOUT registry, and documented OMIT_REASONS.
///
/// These assertions pin that single source statically. The full capability-set equality
/// (each kind == the vocabulary minus its declared, reasoned omissions) runs at JS runtime via
/// <c>auditCapabilities()</c> — exercised by the <c>capabilityparity</c> scenario gate
/// (tools/fidelity/run-edit-scenario.ps1) and logged at module load.
/// </summary>
public class ToolbarParityTests
{
    private static string Editor2(string file) =>
        TestRepo.Read("src", "DocWiz.App", "wwwroot", "editor2", file);

    private static readonly string ContextTools = Editor2("contexttools.js");

    [Fact]
    public void ContextBadge_IsOneMapKeyedByNodeKind()
    {
        // The IMAGE / TABLE / LINKED context badges come from ONE map (not per-file 'Image'
        // literals + a main.js hardcode). main.js renders BADGES[kind].
        Assert.Contains("export const BADGES", ContextTools);
        Assert.Contains("image: { icon: 'image'", ContextTools);
        Assert.Contains("table: { icon: 'table'", ContextTools);
        Assert.Contains("embed: { icon: 'link'", ContextTools);
        Assert.Contains("BADGES[kind]", Editor2("main.js"));
    }

    [Fact]
    public void AllFourStrips_DeriveFromTheSharedDefinition()
    {
        // Every strip renders through buildStrip(kind, ctx); none hand-builds its own list.
        Assert.Contains("buildStrip('image'", Editor2("figureview.js"));
        Assert.Contains("buildStrip('table'", Editor2("tableview.js"));
        Assert.Contains("buildStrip('cellImage'", Editor2("tableview.js"));
        Assert.Contains("buildStrip('box'", Editor2("views.js"));
        foreach (var kind in new[] { "image:", "cellImage:", "table:", "box:" })
            Assert.Contains(kind, ContextTools);
    }

    [Fact]
    public void Figure_And_CellImage_BothOfferRotate_ViaSharedDefinition()
    {
        // The picture family (figure + in-cell image) both carry the rotate capability — it is
        // declared once in the shared LAYOUT, and both node-views wire an onRotate handler.
        Assert.Contains("'rotate'", ContextTools);
        Assert.Contains("onRotate", Editor2("figureview.js"));
        Assert.Contains("onRotate", Editor2("tableview.js"));
    }

    [Fact]
    public void BoxPlacement_Glyphs_AreDefinedOnceInTheSharedDefinition()
    {
        // boxLeft/Center/Right (block-on-page placement, shared by image-align, table-place and
        // box-place) live ONLY in the shared place control — never re-listed in a node-view, so
        // the two box-placement controls (table + embed) cannot drift apart (the F1 finding).
        foreach (var glyph in new[] { "boxLeft", "boxCenter", "boxRight" })
        {
            Assert.Contains(glyph, ContextTools);
            Assert.DoesNotContain(glyph, Editor2("tableview.js"));
            Assert.DoesNotContain(glyph, Editor2("views.js"));
            Assert.DoesNotContain(glyph, Editor2("figureview.js"));
        }
    }

    [Fact]
    public void CapabilityParity_RegistryAndAuditExist()
    {
        // Per-context differences are declared as reasoned omissions, gated by a self-check.
        Assert.Contains("const OMIT_REASONS", ContextTools);
        Assert.Contains("function auditCapabilities", ContextTools);
        Assert.Contains("window.DocWizContextTools", ContextTools);
    }

    [Fact]
    public void RetiredButtonIdioms_AreGoneFromTheStripFiles()
    {
        // The five legacy idiom classes are no longer emitted by any strip. (Quoted forms only,
        // so a prose comment naming `.e2-tools` in views.js is not a false positive.)
        foreach (var file in new[] { "figureview.js", "tableview.js", "views.js" })
        {
            var src = Editor2(file);
            foreach (var cls in new[] { "'e2-seg'", "'e2-btn-grp'", "'e2-lbtn'", "'e2-tools'", "'e2-tb-grp'", "'e2-tgl'" })
                Assert.DoesNotContain(cls, src);
        }
    }

    [Fact]
    public void RightClickMenu_DerivesFromTheSharedDefinition()
    {
        // Phase 2: the right-click menu is a SECOND presentation of the same capability model —
        // built by the shared buildMenu()/openContextMenu(), declared once in MENU_LAYOUT, and
        // its coverage is gated inside auditCapabilities (a menu item must be a real capability).
        Assert.Contains("export function buildMenu", ContextTools);
        Assert.Contains("export function openContextMenu", ContextTools);
        Assert.Contains("const MENU_LAYOUT", ContextTools);
        Assert.Contains("menu[", ContextTools); // the audit's menu-coverage problem string
        // The node-views open the shared menu — they do not hand-build a parallel one.
        Assert.Contains("buildMenu('image'", Editor2("figureview.js"));
        Assert.Contains("buildMenu('table'", Editor2("tableview.js"));
    }
}
