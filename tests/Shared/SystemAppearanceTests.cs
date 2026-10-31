using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace AnimeTracker {
public static class SystemAppearanceTests {
 public sealed class Source:ISystemAppearance {
  public bool Dark {get;private set;}
  public bool Disposed;
  public event Action Changed=delegate{};
  public int Subscribers {get{return Changed.GetInvocationList().Length-1;}}
  public void Change(bool dark){Dark=dark;Changed();}
  public void Dispose(){Disposed=true;}
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception("System appearance: "+message);}
 public static void Run(string root){
  var service=new TrackerService(Path.Combine(root,"system-theme"));var anime=service.AddManual("Синтетическая библиотека");service.Update(anime,anime.Title,5,12,"watching",8,"Заметка");var library=File.ReadAllBytes(service.LibraryPath);
  var source=new Source();var applied=new List<bool>();int libraryEvents=0,appearanceEvents=0;
  service.Changed+=()=>libraryEvents++;service.AppearanceChanged+=()=>appearanceEvents++;
  var binding=new AppearanceBinding(service,source,a=>a(),dark=>applied.Add(dark));
  Check(service.Appearance.Mode=="system"&&source.Subscribers==1&&applied.SequenceEqual(new[]{false}),"initial system light");
  source.Change(true);source.Change(true);Check(applied.SequenceEqual(new[]{false,true})&&!File.Exists(service.Files.Appearance)&&libraryEvents==0&&appearanceEvents==0,"system update saved/doubled/notified library");
  service.SetAppearance("light");source.Change(false);source.Change(true);Check(!applied.Last()&&applied.Count==3,"manual light overridden by OS");
  Check(new TrackerService(service.Root).Appearance.Mode=="light","existing light overwritten");
  service.SetAppearance("dark");source.Change(false);Check(applied.Last()&&applied.Count==4,"manual dark overridden by OS");
  Check(new TrackerService(service.Root).Appearance.Mode=="dark","existing dark overwritten");
  service.SetAppearance("system");var preference=File.ReadAllBytes(service.Files.Appearance);Check(!applied.Last()&&new TrackerService(service.Root).Appearance.Mode=="system","system save/restart");
  source.Change(true);Check(applied.Last()&&File.ReadAllBytes(service.Files.Appearance).SequenceEqual(preference)&&File.ReadAllBytes(service.LibraryPath).SequenceEqual(library)&&libraryEvents==0&&appearanceEvents==3,"OS change mutated persistence or events");
  Directory.CreateDirectory(service.Files.Appearance+".tmp");bool failed=false;try{service.SetAppearance("light");}catch(IOException){failed=true;}catch(UnauthorizedAccessException){failed=true;}
  Check(failed&&service.Appearance.Mode=="system"&&applied.Last(),"failed manual selection applied");Directory.Delete(service.Files.Appearance+".tmp");
  binding.Dispose();int count=applied.Count;source.Change(false);service.SetAppearance("light");Check(source.Disposed&&source.Subscribers==0&&applied.Count==count,"disposed subscription remains active");
  var queuedSource=new Source();var queue=new List<Action>();var queuedApplied=new List<bool>();service.SetAppearance("system");
  var queued=new AppearanceBinding(service,queuedSource,a=>queue.Add(a),dark=>queuedApplied.Add(dark));
  queuedSource.Change(true);service.SetAppearance("light");foreach(var action in queue.ToArray())action();queue.Clear();Check(queuedApplied.SequenceEqual(new[]{false}),"queued OS event overrode newer manual choice");
  service.SetAppearance("system");queuedSource.Change(true);queued.Dispose();foreach(var action in queue)action();Check(queuedApplied.SequenceEqual(new[]{false})&&queuedSource.Subscribers==0,"queued event after Dispose");
  var darkStartup=new Source();darkStartup.Change(true);var initial=new List<bool>();using(var startup=new AppearanceBinding(service,darkStartup,a=>a(),dark=>initial.Add(dark)))Check(initial.SequenceEqual(new[]{true}),"dark OS at startup");
  foreach(bool systemDark in new[]{false,true})foreach(var mode in AppearanceOptions.Modes){var setting=new AppearanceOptions{Mode=mode};Check(setting.ResolveDark(systemDark)==(mode=="system"?systemDark:mode=="dark"),"resolution "+mode);Check(AppearanceOptions.Modes[setting.Index]==mode,"selector mapping");}
 }
}
}
