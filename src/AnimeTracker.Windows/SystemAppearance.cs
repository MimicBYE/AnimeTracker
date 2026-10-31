using System;
using System.IO;
using System.Security;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Threading;

namespace AnimeTracker {
sealed class WindowsSystemAppearance:ISystemAppearance {
 public event Action Changed;
 public WindowsSystemAppearance(){SystemEvents.UserPreferenceChanged+=PreferencesChanged;}
 public bool Dark {get{
  try{using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))return IsDark(key==null?null:key.GetValue("AppsUseLightTheme"));}
  catch(SecurityException){return false;}catch(UnauthorizedAccessException){return false;}catch(IOException){return false;}
 }}
 internal static bool IsDark(object value){return value is int&&(int)value==0;}
 void PreferencesChanged(object sender,UserPreferenceChangedEventArgs args){var changed=Changed;if(changed!=null)changed();}
 public void Dispose(){SystemEvents.UserPreferenceChanged-=PreferencesChanged;}
}
public partial class Tracker {
 AppearanceBinding appearanceBinding;
 void BindAppearance(ISystemAppearance source){
  appearanceBinding=new AppearanceBinding(service,source,action=>{
   if(Dispatcher.HasShutdownStarted)return;if(Dispatcher.CheckAccess())action();else Dispatcher.BeginInvoke(action,DispatcherPriority.Background);
  },ApplyAppearance);
 }
}
}
