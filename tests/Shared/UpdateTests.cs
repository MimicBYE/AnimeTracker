using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace AnimeTracker {
public static class UpdateTests {
 static void Check(bool ok,string message){if(!ok)throw new Exception("Updater: "+message);}
 static void Reject(Action action,string message){bool failed=false;try{action();}catch(Exception e){if(e is IOException||e is InvalidDataException||e is ArgumentException||e is UnauthorizedAccessException||e is OperationCanceledException)failed=true;else throw;}Check(failed,message);}
 sealed class Http:HttpMessageHandler {
  public string Json="[]";public byte[] Bytes=new byte[0];public int Requests;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();if(request.RequestUri==null)throw new InvalidDataException("Missing request URI");Requests++;var response=new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request};response.Content=request.RequestUri.Host=="api.github.com"?(HttpContent)new StringContent(Json):new ByteArrayContent(Bytes);return Task.FromResult(response);}
 }
 static void Build(string root,string platform,string version){
  Directory.CreateDirectory(root);var exe=UpdatePackage.Executable(root,platform);Directory.CreateDirectory(Path.GetDirectoryName(exe)??root);var bytes=new byte[256];if(platform=="windows-x64"){bytes[0]=0x4d;bytes[1]=0x5a;bytes[0x3c]=0x80;bytes[0x80]=0x50;bytes[0x81]=0x45;bytes[0x84]=0x64;bytes[0x85]=0x86;File.WriteAllText(exe+".config","<configuration/>");}else if(platform=="macos-arm64"){Array.Copy(BitConverter.GetBytes(0xfeedfacfu),bytes,4);Array.Copy(BitConverter.GetBytes(0x100000cu),0,bytes,4,4);File.WriteAllText(Path.Combine(root,"Contents","Info.plist"),"<plist/>");}else{bytes[0]=0x7f;bytes[1]=0x45;bytes[2]=0x4c;bytes[3]=0x46;bytes[4]=2;bytes[5]=1;bytes[18]=62;}
  bytes[200]=(byte)(version.EndsWith("2",StringComparison.Ordinal)?2:1);File.WriteAllBytes(exe,bytes);var info=UpdatePackage.BuildPath(root,platform);Directory.CreateDirectory(Path.GetDirectoryName(info)??root);File.WriteAllText(info,new JsonCodec().Serialize(new{updateProtocol=1,applicationVersion=UpdateVersion.Parse(version).Base.ToString(),packageVersion=version,channel=version.Contains("-dev.")?"dev":"stable",commit=new string('a',40)}));
 }
 static string Release(string version,string platform,string hash,long size,string published){return new JsonCodec().Serialize(new{tag_name="v"+version,prerelease=version.Contains("-dev."),draft=false,published_at=published,body="Изменения 日本語",assets=new[]{new{name="AnimeTracker-"+version+"-"+platform+(platform=="linux-x64"?".tar.gz":".zip"),size=size,digest="sha256:"+hash,browser_download_url="https://github.com/MimicBYE/AnimeTracker/releases/download/v"+version+"/AnimeTracker-"+version+"-"+platform+(platform=="linux-x64"?".tar.gz":".zip")}}});}
 static UpdatePlan Plan(string root,string platform){
  var nonce=Guid.NewGuid().ToString("N");var stage=Path.Combine(root,"state","update-"+nonce);var source=Path.Combine(stage,"source");var target=Path.Combine(root,"installed");Build(source,platform,"1.0.2");Build(target,platform,"1.0.1");if(platform=="macos-arm64"){var app=target+".app";Directory.Move(target,app);target=app;}
  var data=Path.Combine(root,"personal");Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"library.xml"),"<library><anime status='watching' watched='7' score='8'/></library>");
  var plan=new UpdatePlan{Stage=stage,Source=source,Target=target,Platform=platform,Version="1.0.2",OldVersion="1.0.1",Nonce=nonce,ParentId=123,ParentStart=1,DataRoots=new[]{data}};foreach(var name in platform=="windows-x64"?new[]{"AnimeTracker.exe","AnimeTracker.exe.config","BUILD-INFO.json"}:UpdatePackage.Files(source))plan.Files.Add(new UpdateFile{Name=name,Hash=UpdatePackage.Hash(Path.Combine(source,name))});plan.Save();return plan;
 }
 static byte[] Archive(string root,string version){var folder=Path.Combine(root,"AnimeTracker-"+version+"-windows-x64");Build(folder,"windows-x64",version);var path=Path.Combine(root,"package.zip");using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))foreach(var name in UpdatePackage.Files(folder))zip.CreateEntryFromFile(Path.Combine(folder,name),Path.GetFileName(folder)+"/"+name);return File.ReadAllBytes(path);}
 public static async Task Run(string root){
  root=Path.Combine(root,"updates");Directory.CreateDirectory(root);var old=DateTime.Parse("2026-10-01T00:00:00Z").ToUniversalTime();var digest=new string('a',64);
  var list=UpdateRelease.Parse("["+String.Join(",",new[]{Release("1.0.1","windows-x64",digest,3,"2026-10-01T00:00:00Z"),Release("1.0.1-dev.10","windows-x64",digest,3,"2026-10-30T00:00:00Z"),Release("1.0.1-dev.11","windows-x64",digest,3,"2026-10-29T00:00:00Z")})+"]","windows-x64");
  Check(UpdateRelease.Select(list,"1.0.1",false,old)==null,"stable opted into dev");Check(UpdateRelease.Select(list,"1.0.1",true,old).Version=="1.0.1-dev.11","numeric dev order / opt in");Check(UpdateRelease.Select(list,"1.0.1-dev.11",true,old)==null,"dev downgraded to old stable/run");
  Check(UpdateRelease.Parse("["+Release("1.0.2","macos-arm64",digest,3,"2026-10-31T00:00:00Z")+"]","windows-x64").Count==0,"wrong platform accepted");
  foreach(var bad in new[]{"v1.0.1","1.0.1-beta","../../1","1.0.-1","99999999999999999999.0"})Reject(()=>UpdateVersion.Parse(bad),"invalid version");
  var hostile="["+Release("1.0.2","windows-x64",digest,3,"2026-10-31T00:00:00Z")+"]";Check(UpdateRelease.Parse(hostile.Replace("https://github.com/","https://evil.invalid/"),"windows-x64").Count==0,"hostile asset URL");Check(UpdateRelease.Parse(hostile.Replace("sha256:","md5:"),"windows-x64").Count==0,"missing SHA256");
  var archiveRoot=Path.Combine(root,"zip");Directory.CreateDirectory(archiveRoot);var bytes=Archive(archiveRoot,"1.0.2");var archive=Path.Combine(archiveRoot,"package.zip");var sha=UpdatePackage.Hash(archive);
  var http=new Http{Bytes=bytes,Json="["+Release("1.0.2","windows-x64",sha,bytes.Length,"2026-10-31T00:00:00Z")+"]"};
  using(var client=new UpdateClient(http)){
   var release=await client.Check(new UpdateBuild{packageVersion="1.0.1"},"windows-x64",false,CancellationToken.None);Check(release.Version=="1.0.2","release check");var downloaded=Path.Combine(root,"download.zip");await client.Download(release,downloaded,null,CancellationToken.None);Check(File.ReadAllBytes(downloaded).SequenceEqual(bytes),"download bytes");
   Reject(()=>client.Download(release,downloaded,null,CancellationToken.None).GetAwaiter().GetResult(),"existing destination accepted");Check(File.Exists(downloaded),"download removed a pre-existing file");
   release.Sha256=new string('0',64);var corrupt=Path.Combine(root,"corrupt.zip");Reject(()=>client.Download(release,corrupt,null,CancellationToken.None).GetAwaiter().GetResult(),"corrupt download accepted");Check(!File.Exists(corrupt),"corrupt file left behind");
   using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();Reject(()=>client.Download(release,corrupt,null,cancelled.Token).GetAwaiter().GetResult(),"cancel ignored");}
  }
  var extracted=UpdatePackage.Extract(archive,Path.Combine(root,"extracted"),"windows-x64","1.0.2",CancellationToken.None);UpdatePackage.Validate(extracted,"windows-x64","1.0.2");
  foreach(var name in new[]{"../escape","/absolute","C:/outside","a\\b","a/../b","a./b"})Reject(()=>UpdatePackage.Within(root,name),"traversal accepted");
  foreach(var kind in new[]{"traversal","link","duplicate"}){
   var evil=Path.Combine(root,kind+".zip");using(var zip=ZipFile.Open(evil,ZipArchiveMode.Create)){var entry=zip.CreateEntry(kind=="traversal"?"../escape":"same");if(kind=="link")entry.ExternalAttributes=unchecked((int)0xa1ff0000);using(var output=entry.Open())output.WriteByte(1);if(kind=="duplicate")zip.CreateEntry("same");}
   Reject(()=>UpdatePackage.Extract(evil,Path.Combine(root,kind+"-out"),"windows-x64","1.0.2",CancellationToken.None),"dangerous archive accepted");
  }
  foreach(var platform in new[]{"windows-x64","linux-x64","macos-arm64"})foreach(var outcome in new[]{"success","failure","write-failure"}){
   var p=Plan(Path.Combine(root,platform+"-"+outcome),platform);var personal=File.ReadAllBytes(Path.Combine(p.DataRoots[0],"library.xml"));string metadata=File.ReadAllText(UpdatePackage.BuildPath(p.Target,p.Platform));File.WriteAllText(Path.Combine(p.Target,"unrelated.txt"),"keep");
   p=UpdatePlan.Load(p.PathName);int writes=0;
   if(outcome=="success"){UpdateInstall.Apply(p,()=>true,null);Check(UpdateBuild.Read(UpdatePackage.BuildPath(p.Target,p.Platform)).packageVersion=="1.0.2","installed version");}
   else{Reject(()=>UpdateInstall.Apply(p,()=>false,outcome=="write-failure"?(Action<string>)(name=>{if(++writes==2||p.Platform=="macos-arm64")throw new IOException("injected write failure");}):null),"failed install accepted");Check(File.ReadAllText(UpdatePackage.BuildPath(p.Target,p.Platform))==metadata,"rollback metadata");}
   Check(File.ReadAllBytes(Path.Combine(p.DataRoots[0],"library.xml")).SequenceEqual(personal),"personal data changed");if(platform!="macos-arm64"||outcome!="success")Check(File.ReadAllText(Path.Combine(p.Target,"unrelated.txt"))=="keep","unrelated file changed");
  }
  var tampered=Plan(Path.Combine(root,"tamper"),"windows-x64");File.AppendAllText(Path.Combine(tampered.Source,"AnimeTracker.exe"),"changed");Reject(()=>tampered.Validate(),"staged tamper accepted");
  var files=new TrackerFiles(Path.Combine(root,"preferences"));var options=UpdateOptions.Load(files.PathFor("update-settings.xml"),true);Check(options.Automatic&&options.Development,"default dev stream");options.Development=false;options.Save(files.PathFor("update-settings.xml"));Check(!UpdateOptions.Load(files.PathFor("update-settings.xml"),true).Development,"stream persistence");
  var clock=DateTime.UtcNow;var fake=new Http{Json="[]"};using(var service=new AppUpdates(files,Path.Combine(root,"program","AnimeTracker.exe"),"windows-x64",Path.Combine(root,"state-service"),"1.0.1",new UpdateClient(fake),()=>clock)){
   await service.Check(false,CancellationToken.None);int requests=fake.Requests;await service.Check(false,CancellationToken.None);Check(fake.Requests==requests,"automatic check not throttled");service.SetOptions(false,false);await service.Check(false,CancellationToken.None);Check(fake.Requests==requests,"automatic opt-out ignored");await service.Check(true,CancellationToken.None);Check(fake.Requests>requests,"manual check blocked by opt-out");
  }
  Check(UpdateInstall.Quote("a b")=="\"a b\""&&UpdateInstall.Quote("a\\")=="\"a\\\\\""&&UpdateInstall.Quote("a\"b")=="\"a\\\"b\"","Windows command-line quoting");
 }
}
}
