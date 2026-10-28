using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AnimeTracker {
public static class AutoStart {
 const string RunKey="Software\\Microsoft\\Windows\\CurrentVersion\\Run";
 static StartupRegistration Registration { get { return new StartupRegistration(Environment.GetFolderPath(Environment.SpecialFolder.Startup,Environment.SpecialFolderOption.DoNotVerify),Assembly.GetExecutingAssembly().Location,RunKey); } }
 public static bool Enabled { get { return Registration.Enabled; } }
 public static void Set(bool enabled) { Registration.Set(enabled); }

 internal sealed class Shortcut {
  public string Target,Arguments,Directory;
 }
 internal static Shortcut ReadShortcut(string path) { return WithShortcut(path,delegate(object link) { return new Shortcut {Target=(string)Property(link,"TargetPath"),Arguments=(string)Property(link,"Arguments"),Directory=(string)Property(link,"WorkingDirectory")}; }); }
 static object Property(object link,string name) { return link.GetType().InvokeMember(name,BindingFlags.GetProperty,null,link,null); }
 static void Property(object link,string name,object value) {try{link.GetType().InvokeMember(name,BindingFlags.SetProperty,null,link,new[]{value});}catch(TargetInvocationException ex){throw new InvalidOperationException("Не удалось установить свойство ярлыка: "+name,ex);}}
 static T WithShortcut<T>(string path,Func<object,T> action) {
  object shell=null,link=null;
  try {var type=Type.GetTypeFromProgID("WScript.Shell",true);shell=Activator.CreateInstance(type);link=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});return action(link);}
  finally {if(link!=null && Marshal.IsComObject(link))Marshal.FinalReleaseComObject(link);if(shell!=null && Marshal.IsComObject(shell))Marshal.FinalReleaseComObject(shell);}
 }
 static void WriteShortcut(string path,string executable) {
  WithShortcut(path,delegate(object link) {Property(link,"TargetPath",executable);Property(link,"Arguments","--startup");Property(link,"WorkingDirectory",Path.GetDirectoryName(executable));Property(link,"Description","Anime Tracker");Property(link,"IconLocation",executable+",0");Property(link,"WindowStyle",1);link.GetType().InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);return true;});
 }
 internal sealed class StartupRegistration {
  readonly string folder,executable,runKey;
  const string Name="AnimeTracker";
  public string LinkPath { get { return Path.Combine(folder,"Anime Tracker.lnk"); } }
  public string LegacyCommand { get { return "\""+executable+"\" --startup"; } }
  public StartupRegistration(string folder,string executable,string runKey) {if(String.IsNullOrWhiteSpace(folder))throw new IOException("Windows не предоставила папку автозагрузки.");this.folder=Path.GetFullPath(folder);this.executable=Path.GetFullPath(executable);this.runKey=runKey;}
  bool Matches(string path) {var link=ReadShortcut(path);return String.Equals(link.Target,executable,StringComparison.OrdinalIgnoreCase) && String.Equals(link.Arguments.Trim(),"--startup",StringComparison.Ordinal) && String.Equals(link.Directory,Path.GetDirectoryName(executable),StringComparison.OrdinalIgnoreCase);}
  public bool Enabled { get {
   // A damaged shortcut must not prevent the application/tray from starting.
   if(File.Exists(LinkPath))try {if(Matches(LinkPath))return true;}catch(IOException){}catch(UnauthorizedAccessException){}catch(COMException){}catch(TargetInvocationException){}
   using(var key=Registry.CurrentUser.OpenSubKey(runKey)){return key!=null && String.Equals(key.GetValue(Name) as string,LegacyCommand,StringComparison.OrdinalIgnoreCase);}
  } }
  public void Set(bool enabled) {
   if(enabled) {
    if(!File.Exists(executable))throw new FileNotFoundException("Не найден EXE для автозапуска.",executable);
    Directory.CreateDirectory(folder);string temporary=Path.Combine(folder,".AnimeTracker-"+Guid.NewGuid().ToString("N")+".lnk");
    try {WriteShortcut(temporary,executable);if(!Matches(temporary))throw new IOException("Не удалось проверить ярлык автозапуска.");if(File.Exists(LinkPath))File.Replace(temporary,LinkPath,null);else File.Move(temporary,LinkPath);}
    finally {if(File.Exists(temporary))File.Delete(temporary);}
    // Keep the legacy registration until its replacement is written and verified.
   } else if(File.Exists(LinkPath))File.Delete(LinkPath);
   using(var key=Registry.CurrentUser.OpenSubKey(runKey,true)){if(key!=null)key.DeleteValue(Name,false);}
  }
 }
 internal static void Test(string root) {
  string folder=Path.Combine(root,"startup"),program=Path.Combine(root,"Программа с пробелами"),keyPath="Software\\AnimeTracker.Tests\\Startup-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(program);Directory.CreateDirectory(folder);string exe=Path.Combine(program,"Anime Tracker.exe");File.WriteAllText(exe,"fixture");
  var registration=new StartupRegistration(folder,exe,keyPath);
  try {
   if(registration.Enabled)throw new Exception("Unexpected startup registration");var missingFolder=Path.Combine(root,"missing-startup");var fresh=new StartupRegistration(missingFolder,exe,keyPath);if(fresh.Enabled || Directory.Exists(missingFolder))throw new Exception("Reading startup created a missing folder");fresh.Set(true);if(!fresh.Enabled)throw new Exception("Missing startup folder was not created");fresh.Set(false);
   using(var key=Registry.CurrentUser.CreateSubKey(keyPath))key.SetValue("AnimeTracker",registration.LegacyCommand);
   if(!registration.Enabled)throw new Exception("Legacy startup was lost");registration.Set(true);
   var link=ReadShortcut(registration.LinkPath);if(!registration.Enabled || link.Target!=exe || link.Arguments!="--startup" || link.Directory!=program)throw new Exception("Startup shortcut target/arguments/directory");
   using(var key=Registry.CurrentUser.OpenSubKey(keyPath))if(key.GetValue("AnimeTracker")!=null)throw new Exception("Duplicate legacy startup after migration");
   registration.Set(true);if(Directory.GetFiles(folder,"*.lnk").Length!=1)throw new Exception("Startup registration is not idempotent");
   string moved=Path.Combine(program,"Moved.exe");File.WriteAllText(moved,"fixture");var other=new StartupRegistration(folder,moved,keyPath);other.Set(true);if(registration.Enabled || !other.Enabled)throw new Exception("Stale startup target accepted");registration.Set(true);
   using(var key=Registry.CurrentUser.CreateSubKey(keyPath))key.SetValue("AnimeTracker",registration.LegacyCommand);registration.Set(false);if(registration.Enabled || File.Exists(registration.LinkPath))throw new Exception("Startup was not disabled");
   using(var key=Registry.CurrentUser.CreateSubKey(keyPath))key.SetValue("AnimeTracker",registration.LegacyCommand);Directory.CreateDirectory(registration.LinkPath);bool failed=false;try{registration.Set(true);}catch(IOException){failed=true;}
   if(!failed || !registration.Enabled || Directory.GetFiles(folder,"*.lnk").Length!=0)throw new Exception("Failed registration lost legacy entry or leaked temporary link");Directory.Delete(registration.LinkPath);
  } finally {if(!keyPath.StartsWith("Software\\AnimeTracker.Tests\\Startup-",StringComparison.Ordinal))throw new Exception("Unsafe startup test cleanup");Registry.CurrentUser.DeleteSubKeyTree(keyPath,false);}
 }
}
public static class StartupDiagnostics {
 public static string LogPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AnimeTracker","diagnostics","startup.log"); } }
 public static void Write(string stage,Exception error=null) {try{Append(LogPath,stage,error);}catch{} }
 internal static void Append(string path,string stage,Exception error) {
  Directory.CreateDirectory(Path.GetDirectoryName(path));if(File.Exists(path) && new FileInfo(path).Length>=65536){File.Copy(path,path+".previous",true);File.WriteAllText(path,"");}
  string detail=error==null?"":("; "+error.GetType().Name+": "+error.Message);if(detail.Length>512)detail=detail.Substring(0,512);
  File.AppendAllText(path,DateTime.UtcNow.ToString("o")+" pid="+System.Diagnostics.Process.GetCurrentProcess().Id+" "+(stage+detail).Replace('\r',' ').Replace('\n',' ')+Environment.NewLine);
 }
 internal static void Test(string root) {string path=Path.Combine(root,"diagnostics","startup.log");Append(path,"starting startup",null);Append(path,"failed",new IOException("test\r\nerror"));if(File.ReadAllLines(path).Length!=2)throw new Exception("Startup diagnostics line boundaries");File.WriteAllText(path,new string('x',65536));Append(path,"running",null);if(!File.Exists(path+".previous") || new FileInfo(path).Length>1024)throw new Exception("Startup diagnostics rotation");}
}
}
