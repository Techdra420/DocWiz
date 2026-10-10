using DocWiz.Core.History;
using DocWiz.Core.Loading;
using DocWiz.Core.Models;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// Stage 6 step 3: the change history. Stakeholder rules under test — kept until deleted;
/// every snapshot is a complete copy of one file, so removing any part of the history
/// breaks nothing; a chapter's timeline follows it through renames and moves (keyed by id);
/// restores are undoable; deleted files can come back.
/// </summary>
public class ProjectHistoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "docwiz-hist-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime T0 = new(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc);

    public ProjectHistoryTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "content", "010-overview"));
        Directory.CreateDirectory(Path.Combine(_root, "views"));
        File.WriteAllText(Path.Combine(_root, "docwiz.yaml"), "project: T\ncode: T-1\naudiences:\n  - id: user\n    title: User\n");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static string Chapter(string title, string body) =>
        $"---\nid: intro\ntitle: {title}\naudiences: [user]\n---\n# {title}\n\n{body}\n";

    private string ChapterPath(string file = "010-intro.md") => Path.Combine(_root, "content", "010-overview", file);

    [Fact]
    public void FirstSave_KeepsTheOriginal_ThenTheSave()
    {
        var path = ChapterPath();
        File.WriteAllText(path, Chapter("Intro", "Before DocWiz history."));

        Assert.Null(ProjectHistory.Write(_root, path, Chapter("Intro", "Edited."), now: T0));

        var entries = ProjectHistory.List(_root, path);
        Assert.Equal([ProjectHistory.Save, ProjectHistory.Original], entries.Select(e => e.Kind));
        Assert.Contains("Before DocWiz history.", ProjectHistory.Read(_root, path, entries[1].Id));
        Assert.Contains("Edited.", File.ReadAllText(path));
    }

    [Fact]
    public void SavesInOneBurst_MergeIntoOneEntry_ALaterSaveStartsANewOne()
    {
        var path = ChapterPath();
        ProjectHistory.Write(_root, path, Chapter("Intro", "v1"), now: T0);
        ProjectHistory.Write(_root, path, Chapter("Intro", "v2"), now: T0.AddMinutes(3));
        ProjectHistory.Write(_root, path, Chapter("Intro", "v3"), now: T0.AddMinutes(6));
        ProjectHistory.Write(_root, path, Chapter("Intro", "v4"), now: T0.AddMinutes(30));

        var entries = ProjectHistory.List(_root, path);
        Assert.Equal(2, entries.Count);
        Assert.Contains("v4", ProjectHistory.Read(_root, path, entries[0].Id));
        Assert.Contains("v3", ProjectHistory.Read(_root, path, entries[1].Id));
    }

    [Fact]
    public void UnchangedSave_AddsNothing()
    {
        var path = ChapterPath();
        ProjectHistory.Write(_root, path, Chapter("Intro", "same"), now: T0);
        ProjectHistory.Write(_root, path, Chapter("Intro", "same"), ProjectHistory.Rename, now: T0.AddHours(1));
        Assert.Single(ProjectHistory.List(_root, path));
    }

    [Fact]
    public void Timeline_FollowsTheChapterThroughRenamesAndMoves()
    {
        var path = ChapterPath();
        ProjectHistory.Write(_root, path, Chapter("Intro", "v1"), now: T0);
        var moved = Path.Combine(_root, "content", "020-Úvod", "010-Úvod.md");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        File.Move(path, moved);
        ProjectHistory.Write(_root, moved, Chapter("Úvod", "v2"), ProjectHistory.Rename, now: T0.AddHours(1));

        Assert.Equal(2, ProjectHistory.List(_root, moved).Count);
    }

    [Fact]
    public void Restore_PutsTheOldContentBack_AndCanItselfBeUndone()
    {
        var path = ChapterPath();
        ProjectHistory.Write(_root, path, Chapter("Intro", "first"), now: T0);
        ProjectHistory.Write(_root, path, Chapter("Intro", "second"), now: T0.AddHours(1));
        var first = ProjectHistory.List(_root, path).Last();

        ProjectHistory.RestoreEntry(_root, path, first.Id, now: T0.AddHours(2));

        Assert.Contains("first", File.ReadAllText(path));
        var entries = ProjectHistory.List(_root, path);
        Assert.Equal(ProjectHistory.Restore, entries[0].Kind);
        Assert.Equal(first.Utc, entries[0].RestoredFromUtc);
        var beforeRestore = entries[1];
        Assert.Contains("second", ProjectHistory.Read(_root, path, beforeRestore.Id));

        ProjectHistory.RestoreEntry(_root, path, beforeRestore.Id, now: T0.AddHours(3)); // undo
        Assert.Contains("second", File.ReadAllText(path));
    }

    [Fact]
    public void DeletingAnySnapshot_OrTheWholeHistory_BreaksNothing()
    {
        var path = ChapterPath();
        ProjectHistory.Write(_root, path, Chapter("Intro", "a"), now: T0);
        ProjectHistory.Write(_root, path, Chapter("Intro", "b"), now: T0.AddHours(1));
        File.Delete(ProjectHistory.List(_root, path)[0].FullPath);
        Assert.Single(ProjectHistory.List(_root, path));

        Directory.Delete(ProjectHistory.HistoryRoot(_root), recursive: true);
        Assert.Empty(ProjectHistory.List(_root, path));
        Assert.Null(ProjectHistory.Write(_root, path, Chapter("Intro", "c"), now: T0.AddHours(2)));
        Assert.Contains("c", File.ReadAllText(path));
    }

    [Fact]
    public void History_IsGitIgnoredByItsOwnFolder()
    {
        ProjectHistory.Write(_root, ChapterPath(), Chapter("Intro", "x"), now: T0);
        Assert.Contains("*", File.ReadAllText(Path.Combine(_root, ".docwiz", ".gitignore")));
    }

    [Fact]
    public void DeletedFile_ComesBackWhereItWas_EvenIfItsSectionIsGone()
    {
        var path = ChapterPath();
        File.WriteAllText(path, Chapter("Intro", "keep me"));
        ProjectHistory.MoveToDeleted(_root, path, T0);
        Directory.Delete(Path.GetDirectoryName(path)!);

        var item = Assert.Single(ProjectHistory.ListDeleted(_root));
        Assert.Equal(("chapter", "Intro", "content/010-overview/010-intro.md"), (item.Kind, item.Title, item.RelPath));

        var rel = ProjectHistory.RestoreDeleted(_root, item.Stamp, item.RelPath);
        Assert.Equal("content/010-overview/010-intro.md", rel);
        Assert.Contains("keep me", File.ReadAllText(path));
        Assert.Empty(ProjectHistory.ListDeleted(_root));
    }

    [Fact]
    public void RestoreDeleted_NeverEscapesTheBin() =>
        Assert.Throws<DocWizLoadException>(() => ProjectHistory.RestoreDeleted(_root, "20261010T080000000Z", "../../docwiz.yaml"));

    [Fact]
    public void NamedVersion_RestoresTheText_AndKeepsTheStateItReplaced()
    {
        var path = ChapterPath();
        File.WriteAllText(path, Chapter("Intro", "handover 1.0"));
        var v1 = ProjectHistory.MarkVersion(_root, "Handover to customer 1.0", T0);
        File.WriteAllText(path, Chapter("Intro", "later edits"));
        File.WriteAllText(ChapterPath("020-new.md"), "---\nid: new\ntitle: New\n---\n# New\n");

        var backup = ProjectHistory.RestoreVersion(_root, v1, T0.AddHours(1));

        Assert.Contains("handover 1.0", File.ReadAllText(path));
        Assert.False(File.Exists(ChapterPath("020-new.md")));
        var versions = ProjectHistory.ListVersions(_root);
        Assert.Equal(["Before restoring Handover to customer 1.0", "Handover to customer 1.0"], versions.Select(v => v.Name));

        ProjectHistory.RestoreVersion(_root, backup, T0.AddHours(2)); // undo the restore
        Assert.Contains("later edits", File.ReadAllText(path));
        Assert.True(File.Exists(ChapterPath("020-new.md")));
    }

    [Fact]
    public void CleanUp_RemovesOldEntries_KeepsNamedVersions()
    {
        var path = ChapterPath();
        ProjectHistory.Write(_root, path, Chapter("Intro", "old"), now: T0);
        ProjectHistory.Write(_root, path, Chapter("Intro", "new"), now: T0.AddDays(40));
        File.WriteAllText(ChapterPath("020-x.md"), "x");
        ProjectHistory.MoveToDeleted(_root, ChapterPath("020-x.md"), T0);
        ProjectHistory.MarkVersion(_root, "Kept", T0);

        var removed = ProjectHistory.CleanUp(_root, T0.AddDays(30));

        Assert.Equal(2, removed);
        Assert.Single(ProjectHistory.List(_root, path));
        Assert.Empty(ProjectHistory.ListDeleted(_root));
        Assert.Single(ProjectHistory.ListVersions(_root));
    }

    [Fact]
    public void ViewsAndTheManifest_HaveTimelinesToo()
    {
        var view = Path.Combine(_root, "views", "user-training.yaml");
        ProjectHistory.Write(_root, view, "id: user-training\ntitle: User Training\n", now: T0);
        var manifest = Path.Combine(_root, ProjectLayout.ManifestFileName);
        ProjectHistory.Write(_root, manifest, "project: T2\n", now: T0);

        Assert.Single(ProjectHistory.List(_root, view));
        Assert.Equal(2, ProjectHistory.List(_root, manifest).Count); // original + save
    }
}
