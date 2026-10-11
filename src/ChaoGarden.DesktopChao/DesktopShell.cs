using System.Diagnostics;

namespace ChaoGarden.DesktopChao;

public static class DesktopShell
{
    public static void OpenFolder(string directory)
    {
        string path = Path.GetFullPath(directory);
        Directory.CreateDirectory(path);
        // Invoke Explorer explicitly: directory shell associations may do nothing on
        // some systems. ArgumentList preserves spaces without command-line concatenation.
        ProcessStartInfo start = new(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
        {
            UseShellExecute = false
        };
        start.ArgumentList.Add(path);
        using Process? process = Process.Start(start);
    }
}
