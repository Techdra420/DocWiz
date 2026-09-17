using System.Runtime.InteropServices;

namespace DocWiz.Cli;

/// <summary>docwiz.exe is a WinExe (no console window on double-click). When it IS
/// launched from a terminal, attaching to the parent console keeps check/build output
/// visible there.</summary>
internal static partial class ConsoleInterop
{
    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;
    private const uint FileTypeUnknown = 0x0000;

    public static void TryAttachParentConsole()
    {
        // When stdout is already a real handle (a pipe or file — `docwiz … | …`,
        // CI, the editor's headless checks), attaching would replace the inherited
        // redirection with the parent console and the captured output would vanish.
        // Only attach when there is no usable stdout at all (double-click or a plain
        // terminal launch, where a GUI-subsystem exe starts with null std handles).
        var stdout = GetStdHandle(StdOutputHandle);
        var redirected = stdout != IntPtr.Zero
                         && stdout != new IntPtr(-1)
                         && GetFileType(stdout) != FileTypeUnknown;
        if (redirected)
            return;

        // Fails harmlessly when there is no parent console (double-click launch).
        _ = AttachConsole(AttachParentProcess);
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetStdHandle(int nStdHandle);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetFileType(IntPtr handle);
}
