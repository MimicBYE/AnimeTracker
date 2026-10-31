using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace AnimeTracker {
// The host chooses the root; the core never chooses an OS-specific user folder.
public sealed class TrackerFiles {
 public string Root {get;private set;}
 public string ConfigRoot {get;private set;}
 public string CacheRoot {get;private set;}
 static readonly string[] Settings={"view-settings.xml","widget-settings.xml","appearance-settings.xml","mac-widget-position.xml","linux-widget-position.xml"};
 static readonly string[] Caches={"schedule.xml","evening-cache.xml","top-kinds.xml"};
 public TrackerFiles(string root) : this(root,root,root) {}
 public TrackerFiles(string root,string config,string cache) {Root=Absolute(root);ConfigRoot=Absolute(config);CacheRoot=Absolute(cache);}
 static string Absolute(string root) {if(String.IsNullOrWhiteSpace(root))throw new ArgumentException("Укажите папку данных.");var full=Path.GetFullPath(root);return full.Length>Path.GetPathRoot(full).Length?full.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar):full;}
 public string[] Roots {get{return new[]{Root,ConfigRoot,CacheRoot}.Distinct(Path.DirectorySeparatorChar=='\\'?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal).ToArray();}}
 public string PathFor(string name) {
  if(String.IsNullOrWhiteSpace(name)||Path.IsPathRooted(name))throw new ArgumentException("Ожидался относительный путь данных.");
  var first=name.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)[0];
  string folder=Settings.Any(n=>first==n||first.StartsWith(n+".",StringComparison.Ordinal))?ConfigRoot:Caches.Any(n=>first==n||first.StartsWith(n+".",StringComparison.Ordinal))||first=="covers"||first=="episode-flags"?CacheRoot:Root;
  var path=Path.GetFullPath(Path.Combine(folder,name));var prefix=folder.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
  if(!path.StartsWith(prefix,Path.DirectorySeparatorChar=='\\'?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new ArgumentException("Путь выходит за пределы папки данных.");
  return path;
 }
 public Dictionary<string,string> ExistingFiles() {
  var result=new Dictionary<string,string>(StringComparer.Ordinal);var roots=Roots;
  foreach(var folder in roots)if(Directory.Exists(folder))Scan(folder,folder,roots,result);
  return result;
 }
 void Scan(string root,string folder,string[] roots,Dictionary<string,string> result) {
  foreach(var file in Directory.GetFiles(folder)) {
   if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)continue;
   var name=file.Substring(root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).Length+1);
   if(name==".instance.lock"||name==".open-request"||name.EndsWith(".tmp",StringComparison.Ordinal))continue;
   if(String.Equals(PathFor(name),file,Path.DirectorySeparatorChar=='\\'?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))result[name]=file;
  }
  foreach(var child in Directory.GetDirectories(folder))if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0&&!roots.Any(r=>String.Equals(r,child,Path.DirectorySeparatorChar=='\\'?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal)))Scan(root,child,roots,result);
 }
 public string Library {get{return PathFor("library.xml");}}
 public string Choices {get{return PathFor("watch-choices.xml");}}
 public string View {get{return PathFor("view-settings.xml");}}
 public string Widget {get{return PathFor("widget-settings.xml");}}
 public string Appearance {get{return PathFor("appearance-settings.xml");}}
 public string Order {get{return PathFor("personal-order.xml");}}
 public string Arcs {get{return PathFor("arcs.xml");}}
 public string Schedule {get{return PathFor("schedule.xml");}}
 public string Metadata {get{return PathFor("evening-cache.xml");}}
}
}
