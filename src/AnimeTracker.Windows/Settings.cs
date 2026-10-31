using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace AnimeTracker {
public partial class Tracker {
 TabControl libraryTabs;
 TabItem settingsTab;

 void OpenSettings() {
  clickTimer.Stop();OpenLibrary();
  if(libraryTabs!=null && settingsTab!=null)libraryTabs.SelectedItem=settingsTab;
 }

 TabItem BuildSettingsTab(Window owner) {
  var gear=new TextBlock {Text="⚙",FontFamily=new FontFamily("Segoe UI Symbol"),FontSize=17};
  var tab=new TabItem {Header=gear,ToolTip="Настройки",Padding=new Thickness(16,7,16,7)};
  AutomationProperties.SetName(tab,"Настройки");DockPanel.SetDock(tab,Dock.Right);
  var body=new StackPanel {MaxWidth=700,HorizontalAlignment=HorizontalAlignment.Left};
  tab.Content=new SmoothScrollViewer {Content=body,Margin=new Thickness(28,22,28,22),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
  body.Children.Add(Text("Настройки",28,Ink));
  body.Children.Add(Text("Виджет, запуск приложения и ваши данные",12,Muted));
  var enabled=new CheckBox {Content="Показывать виджет на рабочем столе",Margin=new Thickness(0,15,0,16)};
  var style=new ComboBox {ItemsSource=new[]{"Бумага","Компактная полоска","Обложка с прогрессом"},Margin=new Thickness(0,6,0,16)};
  var pin=new ComboBox {Margin=new Thickness(0,6,0,0)};
  var widgetPanel=new StackPanel();widgetPanel.Children.Add(Text("Виджет",20,Ink));widgetPanel.Children.Add(enabled);widgetPanel.Children.Add(Text("Оформление",12,Muted));widgetPanel.Children.Add(style);widgetPanel.Children.Add(Text("Какое аниме показывать",12,Muted));widgetPanel.Children.Add(pin);
  var widgetHelp=Text("Отключённый виджет остаётся выключенным после перезапуска. Вернуть его можно кнопкой «Показать виджет» или из трея.",12,Muted);widgetHelp.Margin=new Thickness(0,12,0,0);widgetPanel.Children.Add(widgetHelp);
  body.Children.Add(SettingsCard(widgetPanel));
  var startup=new CheckBox {Content="Запускать вместе с Windows",Margin=new Thickness(0,12,0,0)};
  var appearance=new ComboBox {ItemsSource=new[]{"Светлая","Тёмная"},Margin=new Thickness(0,6,0,16)};AutomationProperties.SetName(appearance,"Тема приложения");
  var appPanel=new StackPanel();appPanel.Children.Add(Text("Приложение",20,Ink));appPanel.Children.Add(Text("Тема",12,Muted));appPanel.Children.Add(appearance);appPanel.Children.Add(startup);var startupHelp=Text("Запуск при входе в учётную запись Windows. Настройка применяется кнопкой «Применить».",12,Muted);startupHelp.Margin=new Thickness(0,8,0,0);appPanel.Children.Add(startupHelp);body.Children.Add(SettingsCard(appPanel));
  var message=Text("",12,Muted);var actions=new WrapPanel {Margin=new Thickness(0,0,0,8)};
  Anime[] pinnedItems=new Anime[0];
  Action reload=delegate {
   appearance.SelectedIndex=service.Appearance.Dark?1:0;enabled.IsChecked=widgetOptions.Enabled;style.SelectedIndex=widgetOptions.Style;
   pinnedItems=library.Items.OrderBy(a=>a.Title,StringComparer.CurrentCultureIgnoreCase).ToArray();
   pin.ItemsSource=new[]{"Автоматически: последнее изменённое"}.Concat(pinnedItems.Select(a=>a.Title)).ToArray();pin.SelectedIndex=Array.FindIndex(pinnedItems,a=>a.Id==widgetOptions.Pinned)+1;
   try {startup.IsChecked=AutoStart.Enabled;startup.IsEnabled=true;message.Text="";}catch(Exception ex){startup.IsEnabled=false;message.Text="Не удалось прочитать автозапуск: "+ex.Message;}
  };
  actions.Children.Add(Primary(Btn("Применить",delegate {
   string selected=pin.SelectedIndex<=0?"":pinnedItems[pin.SelectedIndex-1].Id;
   bool wasStartup=false,changedStartup=false;
   try {
    if(startup.IsEnabled){wasStartup=AutoStart.Enabled;changedStartup=wasStartup!=(startup.IsChecked==true);if(changedStartup || startup.IsChecked==true)AutoStart.Set(startup.IsChecked==true);}
    if(!SaveWidgetOptions(selected,Math.Max(0,style.SelectedIndex),enabled.IsChecked==true)){if(changedStartup)AutoStart.Set(wasStartup);return;}
    service.SetAppearance(appearance.SelectedIndex==1?"dark":"light");message.Text="Настройки сохранены";
   } catch(Exception ex){message.Text="Не удалось сохранить настройки: "+ex.Message;}
  })));
  actions.Children.Add(Btn("Сбросить изменения",delegate {reload();message.Text="Показаны сохранённые настройки";}));
  body.Children.Add(actions);body.Children.Add(message);
  var dataPanel=new StackPanel();dataPanel.Children.Add(Text("Данные и резервные копии",20,Ink));
  var dataActions=new WrapPanel {Margin=new Thickness(0,14,0,0)};
  var import=Btn("Импорт XML из MyAnimeList",ImportMal);import.IsEnabled=!storageBlocked;dataActions.Children.Add(import);
  dataActions.Children.Add(Btn("Открыть папку данных",delegate {try{var path=Path.GetDirectoryName(dataPath);Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}catch(Exception ex){ShowMessage(owner,"Не удалось открыть папку: "+ex.Message,"Anime Tracker");}}));
  dataActions.Children.Add(Btn("Резервная копия ZIP",delegate {
   var picker=new Microsoft.Win32.SaveFileDialog {Title="Сохранить резервную копию",Filter="ZIP архив|*.zip",DefaultExt=".zip",AddExtension=true,FileName="AnimeTracker-backup-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".zip"};
   if(picker.ShowDialog(owner)!=true)return;
   try {ExportSettingsBackup(Path.GetDirectoryName(dataPath),picker.FileName);message.Text="Резервная копия сохранена: "+picker.FileName;}catch(Exception ex){ShowMessage(owner,"Не удалось сохранить копию: "+ex.Message,"Anime Tracker");}
  }));
  foreach(Button action in dataActions.Children)action.Margin=new Thickness(0,0,8,8);
  dataPanel.Children.Add(dataActions);body.Children.Add(SettingsCard(dataPanel));
  var version=Text("Anime Tracker · Windows · "+FileVersionInfo.GetVersionInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).ProductVersion,11,Muted);version.Margin=new Thickness(0,5,0,10);body.Children.Add(version);
  tab.AddHandler(System.Windows.Controls.Primitives.Selector.SelectedEvent,new RoutedEventHandler(delegate(object sender,RoutedEventArgs e){if(e.OriginalSource==tab)reload();}));reload();return tab;
 }
 static Border SettingsCard(UIElement content) {
  return new Border {Background=ThemeBrush(Color.FromRgb(255,249,225)),CornerRadius=new CornerRadius(8),Padding=new Thickness(20),Margin=new Thickness(0,18,0,14),Child=content};
 }
 internal static void ExportSettingsBackup(string folder,string target) {
  folder=Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;target=Path.GetFullPath(target);
  if(target.StartsWith(folder,StringComparison.OrdinalIgnoreCase))throw new IOException("Сохраните архив за пределами папки данных.");
  string temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
  try {ZipFile.CreateFromDirectory(folder,temporary,CompressionLevel.Optimal,false);if(File.Exists(target))File.Replace(temporary,target,target+".bak");else File.Move(temporary,target);}finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
 static void SettingsBackupTest(string root) {
  string data=Path.Combine(root,"settings-data"),target=Path.Combine(root,"settings-backup.zip");Directory.CreateDirectory(data);Directory.CreateDirectory(Path.Combine(data,"covers"));
  var sample=new Library();sample.Items.Add(new Anime {Title="Резервная копия 日本語",Watched=7,Total=12,Status="watching",Score=8,Notes="Моя заметка"});sample.Save(Path.Combine(data,"library.xml"));new WidgetOptions {Enabled=false,Style=2}.Save(Path.Combine(data,"widget-settings.xml"));File.WriteAllBytes(Path.Combine(data,"covers","21.png"),new byte[]{1,2,3});
  ExportSettingsBackup(data,target);byte[] previous=File.ReadAllBytes(target);
  using(var zip=ZipFile.OpenRead(target)) {
   foreach(var file in Directory.GetFiles(data,"*",SearchOption.AllDirectories)) {
    var name=file.Substring(data.Length+1).Replace('\\','/');var entry=zip.GetEntry(name);if(entry==null)throw new Exception("Missing backup entry: "+name);
    using(var input=entry.Open())using(var bytes=new MemoryStream()){input.CopyTo(bytes);if(!bytes.ToArray().SequenceEqual(File.ReadAllBytes(file)))throw new Exception("Backup content changed: "+name);}
   }
  }
  bool rejected=false;try{ExportSettingsBackup(data,Path.Combine(data,"recursive.zip"));}catch(IOException){rejected=true;}if(!rejected || File.Exists(Path.Combine(data,"recursive.zip")))throw new Exception("Backup allowed a recursive destination");
  rejected=false;try{ExportSettingsBackup(Path.Combine(root,"missing-data"),target);}catch(DirectoryNotFoundException){rejected=true;}if(!rejected || !File.ReadAllBytes(target).SequenceEqual(previous))throw new Exception("Failed backup replaced a good archive");
  File.WriteAllText(Path.Combine(data,"additional.xml"),"new data");ExportSettingsBackup(data,target);if(!File.ReadAllBytes(target+".bak").SequenceEqual(previous))throw new Exception("Backup replacement lost previous archive");
 }
}
}
