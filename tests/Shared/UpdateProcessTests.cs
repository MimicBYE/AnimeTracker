using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;

namespace AnimeTracker {
// Separate CLI-only fixture processes. These modes never create windows, tray icons or OS registrations.
public static class UpdateProcessTests {
 static void Check(bool value,string name){if(!value)throw new Exception("Update process: "+name);}
 static void Guard(UpdatePlan plan){var test=Path.GetDirectoryName(Path.GetDirectoryName(plan.Stage)??"")??"";if(!Path.GetFileName(test).StartsWith("anime-tracker-update-probe-",StringComparison.Ordinal)||!UpdatePackage.Inside(test,plan.Target)||!UpdatePackage.Inside(test,plan.Source)||plan.DataRoots.Any(r=>!UpdatePackage.Inside(test,r)))throw new InvalidDataException("Probe requires isolated temporary folders.");}
 public static bool Handle(string[] args,out int code){
  code=0;if(args.Length==2&&args[0]=="--apply-update-probe"){var plan=UpdatePlan.Load(args[1]);Guard(plan);code=UpdateInstall.Run(args[1],TimeSpan.FromSeconds(2));return true;}
  if(args.Length==2&&args[0]=="--update-probe-parent"){
   var plan=UpdatePlan.Load(args[1]);Guard(plan);var self=Process.GetCurrentProcess();plan.ParentId=self.Id;plan.ParentStart=self.StartTime.ToUniversalTime().Ticks;plan.Save();var helper=UpdateInstall.Start(UpdatePackage.Executable(plan.Source,plan.Platform),new[]{"--apply-update-probe",plan.PathName});File.WriteAllText(Path.Combine(plan.Stage,"helper.pid"),helper.Id.ToString());return true;
  }
  if(args.Length>0&&args[0].StartsWith("--update-probe-child-",StringComparison.Ordinal)){
   int at=Array.IndexOf(args,"--update-ready");if(at<0){code=2;return true;}var plan=UpdatePlan.Load(args[at+1]);Guard(plan);
   if(args[0]=="--update-probe-child-fail"){code=2;return true;}
   if(args[0]=="--update-probe-child-timeout"){Thread.Sleep(10000);return true;}
   UpdateInstall.Acknowledge(args,UpdateBuild.Read(UpdatePackage.BuildPath(plan.Target,plan.Platform)).packageVersion);
   var until=DateTime.UtcNow.AddSeconds(10);while(!File.Exists(Path.Combine(plan.Stage,"result.txt"))&&DateTime.UtcNow<until)Thread.Sleep(50);return true;
  }return false;
 }
 static void Metadata(string root,string platform,string version){var path=UpdatePackage.BuildPath(root,platform);Directory.CreateDirectory(Path.GetDirectoryName(path)??root);File.WriteAllText(path,new JsonCodec().Serialize(new{updateProtocol=1,applicationVersion="1.0.1",packageVersion=version,channel="dev",commit=new string('a',40)}));}
 public static void Run(string executable,string platform,bool framework){
  string temp=Path.GetTempPath();
#if NET10_0
  if(OperatingSystem.IsMacOS()&&temp.StartsWith("/var/",StringComparison.Ordinal))temp="/private"+temp;
#endif
  foreach(var outcome in new[]{"success","fail","timeout"}){
   var root=Path.Combine(temp,"anime-tracker-update-probe-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   try{
    string nonce=Guid.NewGuid().ToString("N"),stage=Path.Combine(root,"state","update-"+nonce),source=Path.Combine(stage,platform=="macos-arm64"?"source.app":"source"),target=Path.Combine(root,platform=="macos-arm64"?"installed.app":"installed"),data=Path.Combine(root,"personal");
    var sourceExe=UpdatePackage.Executable(source,platform);Directory.CreateDirectory(Path.GetDirectoryName(sourceExe)??root);
    if(framework){UpdatePackage.Copy(executable,sourceExe);UpdatePackage.Copy(executable+".config",sourceExe+".config");}
    else{var runtime=Path.GetDirectoryName(executable)??"";foreach(var file in Directory.GetFiles(runtime))UpdatePackage.Copy(file,Path.Combine(Path.GetDirectoryName(sourceExe)??root,Path.GetFileName(file)));UpdatePackage.Copy(executable,sourceExe);}
    if(!framework&&platform=="windows-x64")File.WriteAllText(sourceExe+".config","<configuration/>");
    if(platform=="macos-arm64")File.WriteAllText(Path.Combine(source,"Contents","Info.plist"),"<plist/>");
    Metadata(source,platform,"1.0.1-dev.9999");foreach(var name in UpdatePackage.Files(source))UpdatePackage.Copy(Path.Combine(source,name),Path.Combine(target,name));Metadata(target,platform,"1.0.1-dev.9998");Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"library.xml"),"<library><anime watched='7' score='9' status='watching'/></library>");var before=File.ReadAllBytes(Path.Combine(data,"library.xml"));
    var plan=new UpdatePlan{Stage=stage,Source=source,Target=target,Platform=platform,Version="1.0.1-dev.9999",OldVersion="1.0.1-dev.9998",Nonce=nonce,ParentId=1,ParentStart=1,DataRoots=new[]{data},Arguments=new[]{"--update-probe-child-"+outcome}};
    foreach(var name in platform=="windows-x64"?new[]{"AnimeTracker.exe","AnimeTracker.exe.config","BUILD-INFO.json"}:UpdatePackage.Files(source))plan.Files.Add(new UpdateFile{Name=name,Hash=UpdatePackage.Hash(Path.Combine(source,name))});plan.Save();
    var start=new ProcessStartInfo(UpdatePackage.Executable(target,platform)){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardError=true,Arguments=UpdateInstall.Quote("--update-probe-parent")+" "+UpdateInstall.Quote(plan.PathName)};
    using(var parent=Process.Start(start)){if(parent==null)throw new IOException("Fixture parent did not start");Check(parent.WaitForExit(30000),"fixture parent timeout");Check(parent.ExitCode==0,"fixture parent exit "+parent.ExitCode+": "+parent.StandardError.ReadToEnd());}
    var pidPath=Path.Combine(stage,"helper.pid");Check(File.Exists(pidPath),"helper not launched");try{using(var helper=Process.GetProcessById(Int32.Parse(File.ReadAllText(pidPath))))Check(helper.WaitForExit(30000),"helper timeout");}catch(ArgumentException){}
    Check(File.Exists(Path.Combine(stage,"result.txt")),"missing result");Check(UpdateBuild.Read(UpdatePackage.BuildPath(target,platform)).packageVersion==(outcome=="success"?"1.0.1-dev.9999":"1.0.1-dev.9998"),"version / rollback "+outcome);Check(File.ReadAllBytes(Path.Combine(data,"library.xml")).SequenceEqual(before),"personal bytes");
    // The successful probe child exits after the helper's result; no user app is stopped.
    Thread.Sleep(200);
   }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
  }
 }
}
}
