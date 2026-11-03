using System.Diagnostics;
using Avalonia.Controls;

namespace AnimeTracker.Desktop;

internal static class DesktopIntegration
{
    public static string Name=>OperatingSystem.IsLinux()?"Linux":OperatingSystem.IsMacOS()?"macOS":"Desktop";
    public static bool CloseExits=>OperatingSystem.IsLinux();
    public static bool AutoStartAvailable=>OperatingSystem.IsLinux()||OperatingSystem.IsMacOS();
    static LinuxStartup Linux()
    {
        var exe=Environment.ProcessPath??throw new InvalidOperationException("Не удалось определить путь приложения.");
        if(Path.GetFileNameWithoutExtension(exe)=="dotnet")throw new InvalidOperationException("Сначала запустите собранное приложение AnimeTracker, а не dotnet run.");
        int at=Array.IndexOf(Program.Arguments,"--data-dir");
        return new LinuxStartup(exe,LinuxPaths.ConfigHome,at>=0?Program.Arguments[at+1]:null);
    }
    public static bool AutoStartEnabled=>OperatingSystem.IsLinux()?Linux().Enabled:MacIntegration.AutoStartEnabled;
    public static void SetAutoStart(bool enabled){if(OperatingSystem.IsLinux())Linux().Set(enabled);else MacIntegration.SetAutoStart(enabled);}
    public static void InstallMenu()
    {
        if(!OperatingSystem.IsLinux())throw new PlatformNotSupportedException();
        Linux().InstallMenu(LinuxPaths.Base("XDG_DATA_HOME",".local/share",Environment.GetEnvironmentVariable,Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
    }
    public static void AttachWidget(Window window){if(OperatingSystem.IsMacOS())MacIntegration.AttachToDesktop(window);}
    public static void OpenFolder(string path)=>Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
}
