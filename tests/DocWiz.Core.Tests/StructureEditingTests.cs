using DocWiz.App;
using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// Stage 6 step 2: sections and chapters are edited where the author sees them. The folder
/// name IS the section name (accents, spaces, capitals), a chapter file follows its title,
/// and the title IS the H1. Ids never change (ADR-005), so links survive every rename/move.
/// </summary>
public class StructureEditingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "docwiz-struct-" + Guid.NewGuid().ToString("N"));
    private string Content => Path.Combine(_root, "content");

    public StructureEditingTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "views"));
        File.WriteAllText(Path.Combine(_root, "docwiz.yaml"), "project: T\ncode: T-1\naudiences:\n  - id: user\n    title: User\n");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Chapter(string section, string file, string id, string title)
    {
        var dir = Path.Combine(Content, section);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, file);
        File.WriteAllText(path, $"---\nid: {id}\ntitle: {title}\naudiences: [user]\n---\n# {title}\n\nBody of {id}.\n");
        return path;
    }

    private string[] Names(string dir) =>
        Directory.GetFileSystemEntries(dir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!;

    [Theory]
    [InlineData("020-setup", "Setup")]
    [InlineData("050-api-reference", "API Reference")]
    [InlineData("020-Instalace a síť", "Instalace a síť")]
    [InlineData("030-Q&A", "Q&A")]
    [InlineData("040-Einführung", "Einführung")]
    public void SectionTitle_IsTheFolderName_LegacySlugsKeepTheirDerivedTitle(string folder, string title) =>
        Assert.Equal(title, ContentNames.SectionTitle(folder));

    [Theory]
    [InlineData("Návod: krok?")]
    [InlineData("a/b")]
    [InlineData("   ")]
    [InlineData("Konec.")]
    public void SectionName_WindowsCantStore_IsRefusedWithAReason(string name) =>
        Assert.NotNull(ContentNames.SectionNameProblem(name));

    [Fact]
    public void SectionName_AccentsSpacesAndCapitals_AreFine() =>
        Assert.Null(ContentNames.SectionNameProblem("Instalace a síť"));

    [Theory]
    [InlineData("FAQ: Login?", "FAQ Login")]
    [InlineData("  Daily   operations  ", "Daily operations")]
    [InlineData("Přehled systému", "Přehled systému")]
    [InlineData("...", "chapter")]
    public void FileSafe_KeepsTheTitleReadable(string title, string expected) =>
        Assert.Equal(expected, ContentNames.FileSafe(title));

    [Fact]
    public void FileSafe_CapsLength() =>
        Assert.Equal(ContentNames.MaxNameLength, ContentNames.FileSafe(new string('x', 200)).Length);

    [Fact]
    public void CreateSection_UsesTheNextPrefix_AndRefusesANameAlreadyShown()
    {
        Directory.CreateDirectory(Path.Combine(Content, "010-overview"));
        Directory.CreateDirectory(Path.Combine(Content, "020-setup"));

        Assert.Equal("030-Údržba", ContentOrganizer.CreateSection(_root, " Údržba "));
        Assert.True(Directory.Exists(Path.Combine(Content, "030-Údržba")));
        // "Setup" is what 020-setup shows — a second one would be indistinguishable.
        Assert.Throws<DocWizLoadException>(() => ContentOrganizer.CreateSection(_root, "setup"));
    }

    [Fact]
    public void RenameSection_RenamesTheFolder_ChaptersKeepTheirIds()
    {
        Chapter("020-setup", "010-installation.md", "installation", "Installation");

        var folder = ContentOrganizer.RenameSection(_root, "020-setup", "Instalace a síť");

        Assert.Equal("020-Instalace a síť", folder);
        var chapter = Assert.Single(ProjectLoader.Load(_root).Chapters);
        Assert.Equal("Instalace a síť", chapter.SectionTitle);
        Assert.Equal("installation", chapter.Id);
    }

    [Fact]
    public void RenameSection_CaseOnly_Works()
    {
        Directory.CreateDirectory(Path.Combine(Content, "010-Manuál"));
        Assert.Equal("010-MANUÁL", ContentOrganizer.RenameSection(_root, "010-Manuál", "MANUÁL"));
        Assert.Equal(["010-MANUÁL"], Names(Content));
    }

    [Fact]
    public void DeleteSection_RefusesWhileItHasChapters_RemovesWhenEmpty()
    {
        Chapter("010-overview", "010-a.md", "a", "A");
        Directory.CreateDirectory(Path.Combine(Content, "020-Empty"));

        var ex = Assert.Throws<DocWizLoadException>(() => ContentOrganizer.DeleteSection(_root, "010-overview"));
        Assert.Contains("1 chapter", ex.Message);
        ContentOrganizer.DeleteSection(_root, "020-Empty");
        Assert.Equal(["010-overview"], Names(Content));
    }

    [Fact]
    public void MoveChapterTo_AnotherSection_LandsAtTheIndex_AndRenumbers()
    {
        var moving = Chapter("010-overview", "020-b.md", "b", "B");
        Chapter("010-overview", "010-a.md", "a", "A");
        Chapter("020-setup", "010-x.md", "x", "X");
        Chapter("020-setup", "020-y.md", "y", "Y");
        var body = File.ReadAllText(moving);

        var moved = ContentOrganizer.MoveChapterTo(moving, Path.Combine(Content, "020-setup"), 1);

        Assert.Equal("020-b.md", Path.GetFileName(moved));
        Assert.Equal(["010-x.md", "020-b.md", "030-y.md"], Names(Path.Combine(Content, "020-setup")));
        Assert.Equal(["010-a.md"], Names(Path.Combine(Content, "010-overview")));
        Assert.Equal(body, File.ReadAllText(moved));
    }

    [Fact]
    public void RenameChapterFile_FollowsTheTitle_AndNeverOverwrites()
    {
        var path = Chapter("010-overview", "010-system-overview.md", "system-overview", "System Overview");
        Chapter("010-overview", "020-Přehled systému úvod.md", "other", "Other");

        var renamed = ContentOrganizer.RenameChapterFile(path, "Přehled systému: úvod");

        Assert.Equal("010-Přehled systému úvod.md", Path.GetFileName(renamed));
        var again = ContentOrganizer.RenameChapterFile(
            Chapter("010-overview", "030-c.md", "c", "C"), "Přehled systému: úvod");
        Assert.Equal("030-Přehled systému úvod.md", Path.GetFileName(again));
    }

    [Fact]
    public void RenameChapterFile_SameNameInTheSamePrefix_GetsANumber()
    {
        var a = Chapter("010-overview", "010-a.md", "a", "A");
        File.WriteAllText(Path.Combine(Content, "010-overview", "010-Target.md"), "x");
        Assert.Equal("010-Target (2).md", Path.GetFileName(ContentOrganizer.RenameChapterFile(a, "Target")));
    }

    [Theory]
    [InlineData("# **Bold** title {#x}\n\nText.\n", "Bold title")]
    [InlineData("```\n# not a heading\n```\n# Real\n", "Real")]
    [InlineData("# 1. Intro\n", "1. Intro")]
    [InlineData("No heading here.\n", null)]
    public void Title_IsTheH1(string body, string? title) =>
        Assert.Equal(title, ChapterTitle.FromBody(body));

    [Fact]
    public void SetH1_ReplacesTheText_KeepsItsAnchorAndTheRest()
    {
        Assert.Equal("# New {#keep}\n\nBody.\n", ChapterTitle.SetH1("# Old {#keep}\n\nBody.\n", "New"));
        Assert.Equal("No heading.\n", ChapterTitle.SetH1("No heading.\n", "New"));
    }

    [Fact]
    public void NewChapter_NewSectionByName_CreatesFolderAndTitleNamedFile()
    {
        Directory.CreateDirectory(Path.Combine(Content, "010-overview"));
        var dto = new NewChapterDto("Údržba a čištění", "", "cleaning", "Čištění skeneru", "", []);

        var path = ChapterFactory.Create(_root, dto, ["user"]);

        Assert.Equal(Path.Combine(Content, "020-Údržba a čištění", "010-Čištění skeneru.md"), path);
        Assert.Equal("Údržba a čištění", Assert.Single(ProjectLoader.Load(_root).Chapters).SectionTitle);
    }

    [Fact]
    public void NewChapter_TypedNameOfAnExistingSection_ReusesIt()
    {
        Directory.CreateDirectory(Path.Combine(Content, "020-setup"));
        var dto = new NewChapterDto("Setup", "", "printer", "Printer", "", []);

        var path = ChapterFactory.Create(_root, dto, ["user"]);

        Assert.Equal(Path.Combine(Content, "020-setup"), Path.GetDirectoryName(path));
        Assert.Equal(["020-setup"], Names(Content));
    }

    [Fact]
    public void NewChapter_SectionNameWithSeparators_IsRefused() =>
        Assert.Throws<DocWizLoadException>(() => ChapterFactory.Create(_root,
            new NewChapterDto("../outside", "", "x", "X", "", []), ["user"]));

    [Fact]
    public void UniqueId_DerivesFromTheTitle_AndAvoidsTakenIds()
    {
        Chapter("010-overview", "010-a.md", "daily-operations", "Daily Operations");
        var project = ProjectLoader.Load(_root);
        Assert.Equal("daily-operations-2", ChapterFactory.UniqueId(project, "Daily Operations"));
        Assert.Equal("reports", ChapterFactory.UniqueId(project, "Reports"));
    }
}
