using DocWiz.Core.History;

namespace DocWiz.App;

/// <summary>
/// Every editor write of a chapter, view, or the manifest — and every delete of a project
/// file — goes through here, so the change history (Stage 6 step 3) sees all of them: a write
/// records a snapshot, a delete moves the file to "Recently deleted". A history problem never
/// blocks the save; it is logged (not swallowed).
/// </summary>
public static class ProjectWrites
{
    public static void Write(string root, string path, string text, string kind = ProjectHistory.Save)
    {
        if (ProjectHistory.Write(root, path, text, kind) is { } warning)
            Console.Error.WriteLine("WARN " + warning);
    }

    public static void Delete(string root, string path) => ProjectHistory.MoveToDeleted(root, path);
}
