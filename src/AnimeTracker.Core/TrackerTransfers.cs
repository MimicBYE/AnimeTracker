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
 public string ImportWindowsFolder(string source) {return ImportFolder(source,false);}
 public string ImportFolder(string source,bool includeHistory) {
  source=Path.GetFullPath(source);
  var comparison=Path.DirectorySeparatorChar=='\\'?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
  var sourcePrefix=source.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;var targetPrefix=Root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
  if(sourcePrefix.StartsWith(targetPrefix,comparison)||targetPrefix.StartsWith(sourcePrefix,comparison))throw new ArgumentException("Выберите отдельную папку данных, не вложенную в текущую.");
  AnimeTracker.Library.Load(Path.Combine(source,"library.xml"));if(!File.Exists(Path.Combine(source,"library.xml")))throw new ArgumentException("В папке нет library.xml.");
  if(File.Exists(Path.Combine(source,"watch-choices.xml")))WatchChoices.Load(Path.Combine(source,"watch-choices.xml"));if(File.Exists(Path.Combine(source,"arcs.xml")))ArcData.Load(Path.Combine(source,"arcs.xml"));
  if(includeHistory||Files.Roots.Length>1||File.Exists(Path.Combine(Root,".instance.lock")))return ImportSeparated(source,includeHistory);
  var stage=Root+".import-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(stage);
  foreach(var name in new[]{"library.xml","watch-choices.xml","view-settings.xml","widget-settings.xml","personal-order.xml","arcs.xml","schedule.xml","evening-cache.xml","top-kinds.xml"}){var path=Path.Combine(source,name);if(File.Exists(path))File.Copy(path,Path.Combine(stage,name));}
  foreach(var folder in new[]{"covers","episode-flags"}){var directory=Path.Combine(source,folder);if(!Directory.Exists(directory))continue;Directory.CreateDirectory(Path.Combine(stage,folder));foreach(var file in Directory.GetFiles(directory)){var name=Path.GetFileName(file);if(System.Text.RegularExpressions.Regex.IsMatch(name,@"^\d+\.(png|xml)$")&&(File.GetAttributes(file)&FileAttributes.ReparsePoint)==0)File.Copy(file,Path.Combine(stage,folder,name));}}
  var backup=Root+".before-import-"+utcNow().ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6);
  Directory.Move(Root,backup);try{Directory.Move(stage,Root);}catch{Directory.Move(backup,Root);throw;}Reload();return backup;
 }
 string ImportSeparated(string source,bool includeHistory) {
  var incoming=new TrackerFiles(source).ExistingFiles().Where(p=>includeHistory||new[]{"library.xml","watch-choices.xml","view-settings.xml","widget-settings.xml","personal-order.xml","arcs.xml","schedule.xml","evening-cache.xml","top-kinds.xml"}.Contains(p.Key)||p.Key.StartsWith("covers"+Path.DirectorySeparatorChar,StringComparison.Ordinal)||p.Key.StartsWith("episode-flags"+Path.DirectorySeparatorChar,StringComparison.Ordinal)).ToDictionary(p=>p.Key,p=>p.Value);
  var before=Files.ExistingFiles();var backup=Root+".before-import-"+utcNow().ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6);Directory.CreateDirectory(backup);
  foreach(var pair in before){var destination=Path.Combine(backup,pair.Key);Directory.CreateDirectory(Path.GetDirectoryName(destination));File.Copy(pair.Value,destination);}
  var changed=new List<string>();
  try {
   foreach(var name in before.Keys.Union(incoming.Keys).ToArray()){changed.Add(name);var target=Files.PathFor(name);string input;if(incoming.TryGetValue(name,out input))CopyAtomic(input,target);else if(File.Exists(target))File.Delete(target);}
  } catch(Exception original) {
   try {foreach(var name in changed.AsEnumerable().Reverse()){var old=Path.Combine(backup,name);var target=Files.PathFor(name);if(File.Exists(old))CopyAtomic(old,target);else if(File.Exists(target))File.Delete(target);}}
   catch(Exception rollback){throw new AggregateException("Перенос остановлен. Восстановите файлы из резервной копии: "+backup,original,rollback);}throw;
  }
  Reload();return backup;
 }
 static void CopyAtomic(string source,string target) {
  Directory.CreateDirectory(Path.GetDirectoryName(target));var temp=target+".import-"+Guid.NewGuid().ToString("N");
  try{File.Copy(source,temp);if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);}finally{if(File.Exists(temp))File.Delete(temp);}
 }
 public void ExportBackup(string target) {
  target=Path.GetFullPath(target);if(Files.Roots.Any(root=>target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar,Path.DirectorySeparatorChar=='\\'?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal)))throw new ArgumentException("Сохраните архив за пределами папок данных, настроек и кеша.");
  using(var stream=new FileStream(target,FileMode.CreateNew))using(var zip=new ZipArchive(stream,ZipArchiveMode.Create))foreach(var pair in Files.ExistingFiles())zip.CreateEntryFromFile(pair.Value,pair.Key.Replace('\\','/'),CompressionLevel.Optimal);
 }
}
}
