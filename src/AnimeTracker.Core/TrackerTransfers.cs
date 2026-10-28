using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace AnimeTracker {
public partial class TrackerService {
 public string ImportWindowsFolder(string source) {
  source=Path.GetFullPath(source);
  var comparison=Path.DirectorySeparatorChar=='\\'?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
  var sourcePrefix=source.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;var targetPrefix=Root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
  if(sourcePrefix.StartsWith(targetPrefix,comparison)||targetPrefix.StartsWith(sourcePrefix,comparison))throw new ArgumentException("Выберите отдельную папку данных, не вложенную в текущую.");
  AnimeTracker.Library.Load(Path.Combine(source,"library.xml"));if(!File.Exists(Path.Combine(source,"library.xml")))throw new ArgumentException("В папке нет library.xml.");
  if(File.Exists(Path.Combine(source,"watch-choices.xml")))WatchChoices.Load(Path.Combine(source,"watch-choices.xml"));if(File.Exists(Path.Combine(source,"arcs.xml")))ArcData.Load(Path.Combine(source,"arcs.xml"));
  var stage=Root+".import-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(stage);
  foreach(var name in new[]{"library.xml","watch-choices.xml","view-settings.xml","widget-settings.xml","personal-order.xml","arcs.xml","schedule.xml","evening-cache.xml","top-kinds.xml"}){var path=Path.Combine(source,name);if(File.Exists(path))File.Copy(path,Path.Combine(stage,name));}
  foreach(var folder in new[]{"covers","episode-flags"}){var directory=Path.Combine(source,folder);if(!Directory.Exists(directory))continue;Directory.CreateDirectory(Path.Combine(stage,folder));foreach(var file in Directory.GetFiles(directory)){var name=Path.GetFileName(file);if(System.Text.RegularExpressions.Regex.IsMatch(name,@"^\d+\.(png|xml)$")&&(File.GetAttributes(file)&FileAttributes.ReparsePoint)==0)File.Copy(file,Path.Combine(stage,folder,name));}}
  var backup=Root+".before-import-"+utcNow().ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6);
  Directory.Move(Root,backup);try{Directory.Move(stage,Root);}catch{Directory.Move(backup,Root);throw;}Reload();return backup;
 }
 public void ExportBackup(string target) {if(Path.GetFullPath(target).StartsWith(Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Сохраните архив за пределами папки данных.");ZipFile.CreateFromDirectory(Root,target,CompressionLevel.Optimal,false);}
}
}
