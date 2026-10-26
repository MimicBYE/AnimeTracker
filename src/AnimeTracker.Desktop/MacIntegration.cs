using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Avalonia.Controls;

namespace AnimeTracker.Desktop;

internal static class MacIntegration
{
    const string ObjC = "/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC)] static extern nint sel_registerName(string name);
    [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern nint Get(nint target,nint selector);
    [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern void Set(nint target,nint selector,nint value);
    [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern void SetBool(nint target,nint selector,[MarshalAs(UnmanagedType.I1)]bool value);
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")] static extern int CGWindowLevelForKey(int key);
    public static void AttachToDesktop(Window window)
    {
        if(!OperatingSystem.IsMacOS())return;
        var handle=window.TryGetPlatformHandle();if(handle==null)return;
        nint native=handle.HandleDescriptor=="NSView"?Get(handle.Handle,sel_registerName("window")):handle.HandleDescriptor=="NSWindow"?handle.Handle:0;
        if(native==0)return;
        Set(native,sel_registerName("setLevel:"),CGWindowLevelForKey(18)+1);
        Set(native,sel_registerName("setCollectionBehavior:"),(nint)(1|16|64));
        SetBool(native,sel_registerName("setHidesOnDeactivate:"),false);
        SetBool(native,sel_registerName("setExcludedFromWindowsMenu:"),true);
    }
    static string AgentPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Library","LaunchAgents","local.animetracker.desktop.plist");
    public static bool AutoStartEnabled => OperatingSystem.IsMacOS()&&File.Exists(AgentPath);
    public static void SetAutoStart(bool enabled)
    {
        if(!OperatingSystem.IsMacOS())throw new PlatformNotSupportedException("Автозапуск в этом порте реализован для macOS.");
        if(!enabled){if(File.Exists(AgentPath))File.Delete(AgentPath);return;}
        var executable=Environment.ProcessPath??throw new InvalidOperationException("Не удалось определить путь приложения.");
        if(!executable.Contains(".app/Contents/MacOS/",StringComparison.Ordinal))throw new InvalidOperationException("Сначала переместите Anime Tracker.app в Applications и запустите его оттуда.");
        Directory.CreateDirectory(Path.GetDirectoryName(AgentPath)!);
        var plist=new XDocument(new XDeclaration("1.0","UTF-8",null),new XElement("plist",new XAttribute("version","1.0"),new XElement("dict",new XElement("key","Label"),new XElement("string","local.animetracker.desktop"),new XElement("key","ProgramArguments"),new XElement("array",new XElement("string",executable),new XElement("string","--startup")),new XElement("key","RunAtLoad"),new XElement("true"))));
        plist.Save(AgentPath+".tmp");File.Move(AgentPath+".tmp",AgentPath,true);
    }
    public static void OpenFolder(string path)=>Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
}
