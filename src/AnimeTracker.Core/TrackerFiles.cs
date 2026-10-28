using System;
using System.IO;

namespace AnimeTracker {
// The host chooses the root; the core never chooses an OS-specific user folder.
public sealed class TrackerFiles {
 public string Root {get;private set;}
 public TrackerFiles(string root) {if(String.IsNullOrWhiteSpace(root))throw new ArgumentException("Укажите папку данных.");Root=Path.GetFullPath(root);}
 public string PathFor(string name) {
  if(String.IsNullOrWhiteSpace(name)||Path.IsPathRooted(name))throw new ArgumentException("Ожидался относительный путь данных.");
  var path=Path.GetFullPath(Path.Combine(Root,name));var prefix=Root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
  if(!path.StartsWith(prefix,Path.DirectorySeparatorChar=='\\'?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new ArgumentException("Путь выходит за пределы папки данных.");
  return path;
 }
 public string Library {get{return PathFor("library.xml");}}
 public string Choices {get{return PathFor("watch-choices.xml");}}
 public string View {get{return PathFor("view-settings.xml");}}
 public string Widget {get{return PathFor("widget-settings.xml");}}
 public string Order {get{return PathFor("personal-order.xml");}}
 public string Arcs {get{return PathFor("arcs.xml");}}
 public string Schedule {get{return PathFor("schedule.xml");}}
 public string Metadata {get{return PathFor("evening-cache.xml");}}
}
}
