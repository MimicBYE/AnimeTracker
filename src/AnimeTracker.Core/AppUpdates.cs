using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace AnimeTracker {
public sealed class UpdateOptions {
 public bool Automatic=true,Development;
 public static UpdateOptions Load(string path,bool development){foreach(var file in new[]{path,path+".bak"})try{if(!File.Exists(file))continue;using(var reader=XmlReader.Create(file,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=4096})){var x=XElement.Load(reader);bool a,d;if(x.Name=="updates"&&Boolean.TryParse((string)x.Attribute("automatic"),out a)&&Boolean.TryParse((string)x.Attribute("development"),out d))return new UpdateOptions{Automatic=a,Development=d};}}catch(IOException){}catch(UnauthorizedAccessException){}catch(XmlException){}return new UpdateOptions{Development=development};}
 public void Save(string path){Directory.CreateDirectory(Path.GetDirectoryName(path));var temp=path+".tmp";try{new XElement("updates",new XAttribute("automatic",Automatic),new XAttribute("development",Development)).Save(temp);if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
}
public sealed class AppUpdates:IDisposable {
 public readonly UpdateBuild Build;public readonly string Platform,Target,StateRoot;
 readonly TrackerFiles data;readonly UpdateClient client;readonly Func<DateTime> now;
 public UpdateOptions Options {get;private set;}
 public UpdateRelease Available {get;private set;}
 public bool Busy {get;private set;}
 public string Status {get;private set;}
 public double Progress {get;private set;}
 public event Action Changed;
 DateTime checkedAt=DateTime.MinValue;CancellationTokenSource operation;
 string optionsPath {get{return data.PathFor("update-settings.xml");}}
 public AppUpdates(TrackerFiles data,string executable,string platform,string stateRoot,string fallback):this(data,executable,platform,stateRoot,fallback,new UpdateClient(),()=>DateTime.UtcNow){}
 public AppUpdates(TrackerFiles data,string executable,string platform,string stateRoot,string fallback,UpdateClient client,Func<DateTime> now){
  this.data=data;Platform=platform;StateRoot=Path.GetFullPath(stateRoot);this.client=client;this.now=now;
  Target=Path.GetDirectoryName(Path.GetFullPath(executable));if(platform=="macos-arm64")Target=Path.GetFullPath(Path.Combine(Target,"..",".."));
  Build=new UpdateBuild{applicationVersion=fallback,packageVersion=fallback,channel="local",commit="local"};
  var info=UpdatePackage.BuildPath(Target,platform);try{if(File.Exists(info))Build=UpdateBuild.Read(info);}catch(IOException){}catch(InvalidDataException){}catch(UnauthorizedAccessException){}
  Options=UpdateOptions.Load(optionsPath,Build.channel=="dev");Status="Версия "+Build.packageVersion;
  try{var latest=Directory.Exists(StateRoot)?Directory.GetDirectories(StateRoot,"update-*").OrderByDescending(p=>Directory.GetLastWriteTimeUtc(p)).FirstOrDefault():null;if(latest!=null&&File.Exists(Path.Combine(latest,"result.txt")))Status=File.ReadAllText(Path.Combine(latest,"result.txt"));}catch(IOException){}catch(UnauthorizedAccessException){}
 }
 void Notify(){if(Changed!=null)Changed();}
 public void SetOptions(bool automatic,bool development){if(Busy)throw new InvalidOperationException("Дождитесь завершения операции обновления.");var next=new UpdateOptions{Automatic=automatic,Development=development};next.Save(optionsPath);bool channelChanged=Options.Development!=development;Options=next;if(channelChanged){Available=null;checkedAt=DateTime.MinValue;}Status=Available==null?"Настройки обновлений сохранены":"Доступна версия "+Available.Version;Notify();}
 public async Task Check(bool force,CancellationToken token){
  if(Busy||(!force&&(!Options.Automatic||now()-checkedAt<TimeSpan.FromHours(6))))return;
  Busy=true;Status="Проверяем обновления…";Notify();operation=CancellationTokenSource.CreateLinkedTokenSource(token);
  try{Available=await client.Check(Build,Platform,Options.Development,operation.Token);checkedAt=now();Status=Available==null?"Обновлений в выбранном канале нет":"Доступна версия "+Available.Version;}
  catch(OperationCanceledException){Status="Проверка отменена";}
  catch(Exception error){checkedAt=now();Status="Не удалось проверить обновления: "+error.Message;}
  finally{operation.Dispose();operation=null;Busy=false;Notify();}
 }
 public async Task<UpdatePlan> Prepare(IProgress<double> progress,CancellationToken token){
  if(Busy||Available==null)throw new InvalidOperationException("Сначала проверьте наличие обновлений.");var release=Available;Busy=true;Progress=0;Status="Скачиваем "+release.Version+"…";Notify();operation=CancellationTokenSource.CreateLinkedTokenSource(token);
  var nonce=Guid.NewGuid().ToString("N");var stage=Path.Combine(StateRoot,"update-"+nonce);
  try{
   UpdatePackage.NoLinks(StateRoot);Directory.CreateDirectory(stage);
#if MODERN_DOTNET
   if(!OperatingSystem.IsWindows())File.SetUnixFileMode(stage,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
#endif
   var archive=Path.Combine(stage,release.AssetName);await client.Download(release,archive,progress,operation.Token);Status="Проверяем и распаковываем…";Notify();
   var plan=await Task.Run(()=>{
   var source=UpdatePackage.Extract(archive,Path.Combine(stage,"package"),Platform,release.Version,operation.Token);
   if(Platform=="macos-arm64")using(var codesign=UpdateInstall.Start("/usr/bin/codesign",new[]{"--verify","--deep","--strict",source})){if(!codesign.WaitForExit(60000)||codesign.ExitCode!=0)throw new InvalidDataException("Не удалось проверить подпись пакета macOS.");}
   var parent=Process.GetCurrentProcess();var prepared=new UpdatePlan{Stage=stage,Source=source,Target=Target,Platform=Platform,Version=release.Version,OldVersion=Build.packageVersion,Nonce=nonce,ParentId=parent.Id,ParentStart=parent.StartTime.ToUniversalTime().Ticks,DataRoots=data.Roots,Arguments=Platform=="windows-x64"?new[]{"--library"}:new[]{"--data-dir",data.Root}};
   if(Platform!="windows-x64"&&data.Roots.Length>1)prepared.Arguments=new string[0];
   foreach(var name in Platform=="windows-x64"?new[]{"AnimeTracker.exe","AnimeTracker.exe.config","BUILD-INFO.json"}:UpdatePackage.Files(source)){operation.Token.ThrowIfCancellationRequested();prepared.Files.Add(new UpdateFile{Name=name,Hash=UpdatePackage.Hash(Path.Combine(source,name))});}
   if(Platform=="macos-arm64"&&data.Roots.Any(r=>r==Target||UpdatePackage.Inside(Target,r)))throw new IOException("Данные внутри .app: обновление остановлено, исходная версия не изменена.");
   UpdateInstall.Probe(prepared);operation.Token.ThrowIfCancellationRequested();prepared.Save();return prepared;});Status="Обновление готово. Перезапускаем приложение…";return plan;
  }catch(OperationCanceledException){Status="Загрузка отменена. Приложение не изменено.";throw;}
  catch(Exception error){Status="Обновление не установлено: "+error.Message;throw;}
  finally{operation.Dispose();operation=null;Busy=false;Notify();}
 }
 public void Cancel(){if(operation!=null)operation.Cancel();}
 public static void Launch(UpdatePlan plan){UpdateInstall.Start(UpdatePackage.Executable(plan.Source,plan.Platform),new[]{"--apply-update",plan.PathName});}
 public void Dispose(){Cancel();client.Dispose();}
}
}
