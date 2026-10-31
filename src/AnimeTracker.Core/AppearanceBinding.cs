using System;

namespace AnimeTracker {
public interface ISystemAppearance:IDisposable {
 bool Dark {get;}
 event Action Changed;
}
// Hosts provide the OS signal, UI dispatcher and renderer. OS changes never save preferences.
public sealed class AppearanceBinding:IDisposable {
 readonly TrackerService service;
 readonly ISystemAppearance source;
 readonly Action<Action> dispatch;
 readonly Action<bool> apply;
 volatile bool disposed;
 bool? lastDark;
 public AppearanceBinding(TrackerService service,ISystemAppearance source,Action<Action> dispatch,Action<bool> apply){
  this.service=service;this.source=source;this.dispatch=dispatch;this.apply=apply;
  service.AppearanceChanged+=Changed;service.Changed+=Changed;source.Changed+=Changed;
  try{Refresh();}catch{Dispose();throw;}
 }
 void Changed(){if(!disposed)dispatch(Refresh);}
 void Refresh(){
  if(disposed)return;
  bool systemDark=service.Appearance.Mode=="system"&&source.Dark;
  bool dark=service.Appearance.ResolveDark(systemDark);
  if(lastDark==dark)return;apply(dark);lastDark=dark;
 }
 public void Dispose(){
  if(disposed)return;disposed=true;
  service.AppearanceChanged-=Changed;service.Changed-=Changed;source.Changed-=Changed;source.Dispose();
 }
}
}
