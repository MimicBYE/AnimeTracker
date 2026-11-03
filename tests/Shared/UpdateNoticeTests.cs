using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace AnimeTracker {
public static class UpdateNoticeTests {
 public sealed class Fixture:IDisposable {
  public DateTime UtcNow=new DateTime(2026,11,3,12,0,0,DateTimeKind.Utc);
  public string Version="1.1.1";public int Downloads;
  public readonly AppUpdates Updates;
  public readonly string Root,Platform;
  sealed class Http:HttpMessageHandler {
   readonly Fixture fixture;public Http(Fixture fixture){this.fixture=fixture;}
   protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
    token.ThrowIfCancellationRequested();var uri=request.RequestUri;if(uri==null)throw new IOException("Missing URI");if(uri.Host!="api.github.com"){fixture.Downloads++;throw new IOException("Tests must not download or install software.");}
    string version=fixture.Version,platform=fixture.Platform,name="AnimeTracker-"+version+"-"+platform+(platform=="linux-x64"?".tar.gz":".zip");
    var json="["+new JsonCodec().Serialize(new{tag_name="v"+version,prerelease=false,draft=false,published_at="2026-11-03T00:00:00Z",body="Описание обновления",assets=new[]{new{name=name,size=1,digest="sha256:"+new string('a',64),browser_download_url="https://github.com/MimicBYE/AnimeTracker/releases/download/v"+version+"/"+name}}})+"]";
    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new StringContent(json)});
   }
  }
  public Fixture(string root,string platform="windows-x64"){
   Root=root;Platform=platform;Updates=new AppUpdates(new TrackerFiles(root),Path.Combine(root,"program","AnimeTracker.exe"),platform,Path.Combine(root,"state"),"1.1.0",new UpdateClient(new Http(this)),()=>UtcNow);
  }
  public Task Check(){return Updates.Check(true,CancellationToken.None);}
  public void Dispose(){Updates.Dispose();}
 }
 static void Check(bool value,string name){if(!value)throw new Exception("Update notice: "+name);}
 public static async Task Run(string root){
  root=Path.Combine(root,"update-notices");Directory.CreateDirectory(root);var path=Path.Combine(root,"update-settings.xml");
  File.WriteAllText(path,"<updates automatic='true' development='false'/>");
  using(var f=new Fixture(root)){
   await f.Check();var updates=f.Updates;Check(updates.NoticeDue,"available automatic release not due");var release=updates.BeginNotice();if(release==null)throw new Exception("Missing notice");Check(updates.BeginNotice()==null&&!updates.NoticeDue,"duplicate notification");
   updates.RemindLater(release.Version);var until=f.UtcNow.AddHours(6);Check(!updates.NoticeDue&&updates.Options.RemindAfter==until,"six hour snooze");
   await f.Check();Check(!updates.NoticeDue,"recheck removed snooze");updates.SetOptions(true,true);Check(updates.Options.RemindAfter==until,"settings changed snooze");updates.SetOptions(true,false);
   using(var restart=new Fixture(root)){
    restart.UtcNow=f.UtcNow.AddHours(5);await restart.Check();Check(!restart.Updates.NoticeDue,"restart lost snooze");restart.UtcNow=until;Check(restart.Updates.NoticeDue,"expired snooze not due without new network request");
    var before=File.ReadAllText(path);var prior=restart.Updates.Options.RemindAfter;var held=restart.Updates.BeginNotice();Directory.CreateDirectory(path+".tmp");bool failed=false;try{restart.Updates.RemindLater(held.Version);}catch(Exception e){if(e is IOException||e is UnauthorizedAccessException)failed=true;else throw;}finally{Directory.Delete(path+".tmp");}
    Check(failed&&File.ReadAllText(path)==before&&restart.Updates.Options.RemindAfter==prior,"failed write changed snooze");restart.Updates.ReleaseNotice(held.Version);Check(restart.Updates.NoticeDue,"failed write permanently consumed notice");
    restart.Updates.RemindLater(held.Version);restart.Version="1.1.2";await restart.Check();Check(restart.Updates.NoticeDue&&restart.Updates.BeginNotice().Version=="1.1.2","old snooze hid a newer version");restart.Updates.ReleaseNotice("1.1.2");
    restart.Updates.SetOptions(false,false);await restart.Check();Check(restart.Updates.Available!=null&&!restart.Updates.NoticeDue,"manual check bypassed automatic opt-out");
    restart.Updates.SetOptions(true,true);Check(restart.Updates.Available==null&&!restart.Updates.NoticeDue,"channel change kept stale candidate");
   }
   Check(f.Downloads==0,"notice downloaded software");
  }
  File.WriteAllText(path,"<updates automatic='true' development='false' remindVersion='../bad' remindAfter='invalid'/>");var recovered=UpdateOptions.Load(path,false);Check(recovered.Automatic&&recovered.RemindVersion=="","invalid optional reminder broke old settings");
 }
}
}
