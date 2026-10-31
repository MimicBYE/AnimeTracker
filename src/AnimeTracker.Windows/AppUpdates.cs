using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AnimeTracker {
public partial class Tracker {
 AppUpdates appUpdates;
 void InitializeUpdates(){appUpdates=new AppUpdates(service.Files,System.Reflection.Assembly.GetExecutingAssembly().Location,"windows-x64",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AnimeTracker.Updates"),System.Diagnostics.FileVersionInfo.GetVersionInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).ProductVersion);}
 void OpenUpdates(Func<bool> draft){
  if(appUpdates==null)return;
  if(Windows.Cast<Window>().Any(w=>w!=widget&&w!=full)){ShowMessage("Завершите работу с открытой карточкой или диалогом, затем откройте обновления.");return;}
  var owner=full;var window=new Window{Title="Обновления Anime Tracker",Width=650,Height=620,MinWidth=450,MinHeight=420,Owner=owner,WindowStartupLocation=WindowStartupLocation.CenterOwner};StyleWindow(window);
  var panel=new DockPanel{Margin=new Thickness(24)};window.Content=panel;
  var heading=new StackPanel();heading.Children.Add(Text("Обновления приложения",25,Ink));heading.Children.Add(Text("Установлена версия "+appUpdates.Build.packageVersion,13,Muted));
  var automatic=new CheckBox{Content="Проверять вместе с обновлением серий",IsChecked=appUpdates.Options.Automatic,Margin=new Thickness(0,18,0,8)};
  var development=new CheckBox{Content="Получать предварительные версии",IsChecked=appUpdates.Options.Development,Margin=new Thickness(0,0,0,8)};heading.Children.Add(automatic);heading.Children.Add(development);heading.Children.Add(Text("Предварительные версии могут содержать незавершённые изменения. Установка всегда запускается кнопкой.",12,Muted));
  heading.Children.Add(Btn("Сохранить",delegate{try{appUpdates.SetOptions(automatic.IsChecked==true,development.IsChecked==true);}catch(Exception error){ShowMessage(window,error.Message,"Не удалось сохранить");}}));DockPanel.SetDock(heading,Dock.Top);panel.Children.Add(heading);
  var footer=new StackPanel{Margin=new Thickness(0,15,0,0)};DockPanel.SetDock(footer,Dock.Bottom);panel.Children.Add(footer);var status=Text("",13,Muted);footer.Children.Add(status);var bar=new ProgressBar{Minimum=0,Maximum=100,Height=5,Margin=new Thickness(0,10,0,12),Foreground=Accent,Background=ThemeBrush("#E8D59D")};footer.Children.Add(bar);
  var actions=new WrapPanel();footer.Children.Add(actions);var notes=Text("",13,Ink);panel.Children.Add(new SmoothScrollViewer{Content=notes,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,20,0,0)});
  var check=Btn("Проверить",async delegate{await appUpdates.Check(true,episodeLifetime.Token);});actions.Children.Add(check);
  var install=Primary(Btn("Скачать и установить",async delegate{
   if(draft!=null&&draft()){ShowMessage(window,"Сначала примените или сбросьте изменения в настройках приложения.","Несохранённые настройки");return;}
   if(automatic.IsChecked!=appUpdates.Options.Automatic||development.IsChecked!=appUpdates.Options.Development){ShowMessage(window,"Сначала сохраните настройки обновлений.","Настройки обновлений");return;}
   var confirmation=MessageWindow(window,"Приложение скачает проверенный пакет, сохранит резервную копию и перезапустится. Библиотека и настройки сохранятся.","Установить "+(appUpdates.Available==null?"обновление":appUpdates.Available.Version)+"?");
   var confirm=(DockPanel)confirmation.Content;var row=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0)};bool accepted=false;row.Children.Add(Primary(Btn("Обновить",delegate{accepted=true;confirmation.Close();})));row.Children.Add(Btn("Отмена",delegate{confirmation.Close();}));confirm.Children.RemoveAt(0);DockPanel.SetDock(row,Dock.Bottom);confirm.Children.Insert(0,row);confirmation.ShowDialog();if(!accepted)return;
   try{var progress=new Progress<double>(value=>{if(window.IsLoaded){bar.Value=value;status.Text="Загрузка: "+Math.Round(value)+"%";}});var plan=await appUpdates.Prepare(progress,episodeLifetime.Token);if(!storageBlocked)service.Save();AppUpdates.Launch(plan);quitting=true;if(tray!=null)tray.Dispose();Shutdown();}
   catch(OperationCanceledException){}catch(Exception error){ShowMessage(window,error.Message,"Обновление не установлено");}
  }));actions.Children.Add(install);actions.Children.Add(Btn("Отмена загрузки",appUpdates.Cancel));actions.Children.Add(Btn("Закрыть",window.Close));
  Action refresh=delegate{if(!window.Dispatcher.CheckAccess()){window.Dispatcher.BeginInvoke(new Action(()=>{if(window.IsLoaded){status.Text=appUpdates.Status;}}));return;}status.Text=appUpdates.Status;check.IsEnabled=!appUpdates.Busy;install.IsEnabled=!appUpdates.Busy&&appUpdates.Available!=null;automatic.IsEnabled=development.IsEnabled=!appUpdates.Busy;notes.Text=appUpdates.Available==null?"Проверка и установка выполняются без ручного копирования файлов.":("Новая версия: "+appUpdates.Available.Version+"\n\n"+(appUpdates.Available.Notes??"").Substring(0,Math.Min(16000,(appUpdates.Available.Notes??"").Length)));};
  appUpdates.Changed+=refresh;window.Closed+=delegate{appUpdates.Changed-=refresh;appUpdates.Cancel();};refresh();window.ShowDialog();
 }
}
}
