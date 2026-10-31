using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace AnimeTracker {
public sealed class UpdateFile {public string Name,Hash;}
public sealed class UpdatePlan {
 public string Stage,Source,Target,Platform,Version,OldVersion,Nonce;
 public int ParentId;public long ParentStart;
 public string[] DataRoots=new string[0],Arguments=new string[0];public List<UpdateFile> Files=new List<UpdateFile>();
 public string PathName {get{return Path.Combine(Stage,"plan.xml");}}
 public string Ready {get{return Path.Combine(Stage,"ready");}}
 public string Backup {get{return Path.Combine(Stage,"rollback");}}
 public void Save(){
  new XElement("update",new XAttribute("stage",Stage),new XAttribute("source",Source),new XAttribute("target",Target),new XAttribute("platform",Platform),new XAttribute("version",Version),new XAttribute("old",OldVersion),new XAttribute("nonce",Nonce),new XAttribute("pid",ParentId),new XAttribute("started",ParentStart),new XElement("data",DataRoots.Select(p=>new XElement("path",p))),new XElement("args",Arguments.Select(p=>new XElement("arg",p))),new XElement("files",Files.Select(f=>new XElement("file",new XAttribute("name",f.Name),new XAttribute("sha256",f.Hash))))).Save(PathName);
 }
 public static UpdatePlan Load(string path){
  using(var reader=XmlReader.Create(path,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=4*1024*1024})){
   var x=XElement.Load(reader);if(x.Name!="update")throw new InvalidDataException("Некорректный план обновления.");
   var p=new UpdatePlan{Stage=(string)x.Attribute("stage"),Source=(string)x.Attribute("source"),Target=(string)x.Attribute("target"),Platform=(string)x.Attribute("platform"),Version=(string)x.Attribute("version"),OldVersion=(string)x.Attribute("old"),Nonce=(string)x.Attribute("nonce"),ParentId=(int)x.Attribute("pid"),ParentStart=(long)x.Attribute("started"),DataRoots=x.Element("data").Elements("path").Select(e=>e.Value).ToArray(),Arguments=x.Element("args").Elements("arg").Select(e=>e.Value).ToArray(),Files=x.Element("files").Elements("file").Select(e=>new UpdateFile{Name=(string)e.Attribute("name"),Hash=(string)e.Attribute("sha256")}).ToList()};
   p.Validate();if(Path.GetFullPath(path)!=p.PathName)throw new InvalidDataException("План находится вне папки обновления.");return p;
  }
 }
 public void Validate(){
  if(!System.Text.RegularExpressions.Regex.IsMatch(Nonce??"",@"^[a-f0-9]{32}$")||!Path.IsPathRooted(Stage??"")||!Path.IsPathRooted(Target??"")||!Path.IsPathRooted(Source??""))throw new InvalidDataException("Некорректные пути обновления.");
  if(!Path.GetFileName(Stage).Equals("update-"+Nonce,StringComparison.Ordinal)||!UpdatePackage.Inside(Stage,Source)||UpdatePackage.Inside(Target,Stage)||UpdatePackage.Inside(Stage,Target)||Target.TrimEnd(Path.DirectorySeparatorChar)==Path.GetPathRoot(Target).TrimEnd(Path.DirectorySeparatorChar))throw new InvalidDataException("Опасная папка установки.");
  UpdatePackage.NoLinks(Stage);UpdatePackage.NoLinks(Target);UpdateVersion.Parse(OldVersion);UpdatePackage.Validate(Source,Platform,Version);
  if(Files.Count==0||Files.Count>20000||Files.Select(f=>f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=Files.Count)throw new InvalidDataException("Некорректный список файлов обновления.");
  var expected=Platform=="windows-x64"?new[]{"AnimeTracker.exe","AnimeTracker.exe.config","BUILD-INFO.json"}:UpdatePackage.Files(Source);
  if(!expected.OrderBy(s=>s).SequenceEqual(Files.Select(f=>f.Name).OrderBy(s=>s)))throw new InvalidDataException("Состав обновления изменился.");
  foreach(var f in Files){var from=UpdatePackage.Within(Source,f.Name.Replace(Path.DirectorySeparatorChar,'/'));var to=UpdatePackage.Within(Target,f.Name.Replace(Path.DirectorySeparatorChar,'/'));UpdatePackage.NoLinks(to);if(UpdatePackage.Hash(from)!=f.Hash)throw new InvalidDataException("Файл обновления изменён после проверки.");}
  foreach(var root in DataRoots){UpdatePackage.NoLinks(root);if(UpdatePackage.Inside(root,Stage)||UpdatePackage.Inside(Stage,root))throw new InvalidDataException("Папки данных и обновления пересекаются.");}
 }
}
public static class UpdateInstall {
 public static void Probe(UpdatePlan plan){plan.Validate();Directory.CreateDirectory(plan.Target);var probe=Path.Combine(plan.Platform=="macos-arm64"?Path.GetDirectoryName(plan.Target):plan.Target,".AnimeTracker-write-"+plan.Nonce);try{using(File.Create(probe)){};}finally{if(File.Exists(probe))File.Delete(probe);}}
 public static void Apply(UpdatePlan plan,Func<bool> healthy,Action<string> beforeWrite){
  Probe(plan);Directory.CreateDirectory(plan.Backup);var changed=new List<UpdateFile>();var existing=new HashSet<string>(StringComparer.Ordinal);bool bundleSwapped=false;
  var bundleBackup=plan.Target+".previous-"+plan.Nonce;var incoming=plan.Target+".incoming-"+plan.Nonce;
  try{
   // Parent is already gone. Snapshot personal files separately; never restore them over newer progress.
   int index=0;foreach(var root in plan.DataRoots.Distinct()){if(!Directory.Exists(root))continue;foreach(var name in UpdatePackage.Files(root)){if(name.EndsWith(".tmp",StringComparison.Ordinal)||name==".instance.lock"||name==".open-request")continue;UpdatePackage.Copy(Path.Combine(root,name),Path.Combine(plan.Backup,"data-"+index,name));}index++;}
   if(plan.Platform=="macos-arm64"){
    if(!plan.Target.EndsWith(".app",StringComparison.Ordinal)||!File.Exists(Path.Combine(plan.Target,"Contents","Info.plist")))throw new IOException("Не найден установленный пакет .app.");
    foreach(var name in UpdatePackage.Files(plan.Source))UpdatePackage.Copy(Path.Combine(plan.Source,name),Path.Combine(incoming,name));
    if(beforeWrite!=null)beforeWrite("bundle");Directory.Move(plan.Target,bundleBackup);bundleSwapped=true;Directory.Move(incoming,plan.Target);
   }else{
    foreach(var file in plan.Files){var target=UpdatePackage.Within(plan.Target,file.Name.Replace(Path.DirectorySeparatorChar,'/'));if(File.Exists(target)){existing.Add(file.Name);UpdatePackage.Copy(target,Path.Combine(plan.Backup,"program",file.Name));}}
    new XElement("rollback",plan.Files.Select(f=>new XElement("file",new XAttribute("name",f.Name),new XAttribute("existed",existing.Contains(f.Name))))).Save(Path.Combine(plan.Backup,"journal.xml"));
    // Runtime first, apphost last. No unrelated files/directories are moved or deleted.
    foreach(var file in plan.Files.OrderBy(f=>Path.GetFileName(f.Name)=="AnimeTracker"||Path.GetFileName(f.Name)=="AnimeTracker.exe"?1:0)){
     if(beforeWrite!=null)beforeWrite(file.Name);Replace(Path.Combine(plan.Source,file.Name),UpdatePackage.Within(plan.Target,file.Name.Replace(Path.DirectorySeparatorChar,'/')),plan.Nonce);changed.Add(file);
    }
   }
   if(!healthy())throw new IOException("Новая версия не подтвердила запуск. Выполняется откат.");
   File.WriteAllText(Path.Combine(plan.Stage,"result.txt"),"Обновлено до "+plan.Version+". Резервная копия: "+plan.Backup);
  }catch(Exception original){
   try{
    if(bundleSwapped){if(Directory.Exists(plan.Target))Directory.Move(plan.Target,plan.Target+".failed-"+plan.Nonce);Directory.Move(bundleBackup,plan.Target);}
    else foreach(var file in changed.AsEnumerable().Reverse()){var target=UpdatePackage.Within(plan.Target,file.Name.Replace(Path.DirectorySeparatorChar,'/'));if(existing.Contains(file.Name))Replace(Path.Combine(plan.Backup,"program",file.Name),target,plan.Nonce);else if(File.Exists(target))File.Delete(target);}
   }catch(Exception rollback){throw new AggregateException("Ошибка отката. Резервная копия: "+plan.Backup,original,rollback);}throw;
  }
 }
 static void Replace(string source,string target,string nonce){var temporary=target+".update-"+nonce;try{UpdatePackage.Copy(source,temporary);if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);}finally{if(File.Exists(temporary))File.Delete(temporary);}}
 public static string Quote(string argument){var text=new System.Text.StringBuilder("\"");int slashes=0;foreach(char c in argument){if(c=='\\'){slashes++;continue;}text.Append('\\',c=='"'?slashes*2+1:slashes);text.Append(c);slashes=0;}text.Append('\\',slashes*2);return text.Append('"').ToString();}
 public static Process Start(string executable,IEnumerable<string> arguments,bool background=true){
  var info=new ProcessStartInfo(executable){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(executable),CreateNoWindow=background,WindowStyle=background?ProcessWindowStyle.Hidden:ProcessWindowStyle.Normal};
#if MODERN_DOTNET
  foreach(var argument in arguments)info.ArgumentList.Add(argument);
#else
  info.Arguments=String.Join(" ",arguments.Select(Quote));
#endif
  return Process.Start(info);
 }
 public static int Run(string planPath){return Run(planPath,TimeSpan.FromSeconds(60));}
 public static int Run(string planPath,TimeSpan readyTimeout){
  UpdatePlan plan=null;bool parentExited=false;try{
   plan=UpdatePlan.Load(planPath);var own=Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);if(own!=Path.GetFullPath(UpdatePackage.Executable(plan.Source,plan.Platform)))throw new InvalidDataException("Установщик запущен не из проверенного пакета.");
   using(File.Open(Path.Combine(plan.Stage,"install-once"),FileMode.CreateNew,FileAccess.Write,FileShare.None)){}
   Process parent=null;try{parent=Process.GetProcessById(plan.ParentId);if(parent.HasExited)parent=null;else if(parent.StartTime.ToUniversalTime().Ticks!=plan.ParentStart||Path.GetFullPath(parent.MainModule.FileName)!=Path.GetFullPath(UpdatePackage.Executable(plan.Target,plan.Platform)))throw new IOException("Процесс приложения изменился.");}catch(ArgumentException){parent=null;}catch(InvalidOperationException){if(parent!=null&&parent.HasExited)parent=null;else throw;}
   if(parent!=null&&!parent.WaitForExit(60000))throw new IOException("Приложение не завершилось. Обновление отменено.");
   parentExited=true;
   Apply(plan,()=>{
    var args=plan.Arguments.Concat(new[]{"--update-ready",plan.PathName,plan.Nonce}).ToArray();using(var child=Start(UpdatePackage.Executable(plan.Target,plan.Platform),args,false)){
     bool confirmed=false;try{var until=DateTime.UtcNow+readyTimeout;while(DateTime.UtcNow<until){if(File.Exists(plan.Ready)&&File.ReadAllText(plan.Ready)==plan.Nonce+"\n"+plan.Version){confirmed=true;return true;}if(child.HasExited)return false;Thread.Sleep(100);}return false;}
     finally{if(!confirmed&&!child.HasExited){child.Kill();child.WaitForExit(10000);}}
    }
   },null);return 0;
  }catch(Exception error){
   if(plan!=null){try{File.WriteAllText(Path.Combine(plan.Stage,"result.txt"),"Обновление не установлено: "+error.Message+"\nРезервная копия: "+plan.Backup);}catch{}if(parentExited)try{Start(UpdatePackage.Executable(plan.Target,plan.Platform),plan.Arguments,false);}catch{}}
   return 1;
  }
 }
 public static void Acknowledge(string[] args,string version){int at=Array.IndexOf(args,"--update-ready");if(at<0)return;if(at+2>=args.Length)throw new ArgumentException("Некорректное подтверждение обновления.");var plan=UpdatePlan.Load(args[at+1]);if(plan.Nonce!=args[at+2]||plan.Version!=version||Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName)!=Path.GetFullPath(UpdatePackage.Executable(plan.Target,plan.Platform)))throw new InvalidDataException("Неожиданная версия после обновления.");var temp=plan.Ready+".tmp";File.WriteAllText(temp,plan.Nonce+"\n"+version);File.Move(temp,plan.Ready);}
}
}
