using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AnimeTracker {
public partial class Tracker {
 bool isolatedThemeTest;
 static void ThemeCheck(bool ok,string name){if(!ok)throw new Exception("Theme: "+name);}
 static IEnumerable<DependencyObject> ThemeChildren(DependencyObject parent){yield return parent;foreach(var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())foreach(var descendant in ThemeChildren(child))yield return descendant;}
 static Color ThemeColor(Brush brush){return ((SolidColorBrush)brush).Color;}
 static void ThemeCapture(FrameworkElement content,string folder,string name,double width,double height){
  if(folder==null)return;Directory.CreateDirectory(folder);content.Measure(new Size(width,height));content.Arrange(new Rect(0,0,width,height));content.UpdateLayout();
  var bitmap=new RenderTargetBitmap((int)width,(int)height,96,96,PixelFormats.Pbgra32);bitmap.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(folder,name+".png")))encoder.Save(output);
 }
 static void ThemeTest(string[] args){
  string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"theme-test.txt"),root=Path.Combine(Path.GetTempPath(),"anime-theme-"+Guid.NewGuid().ToString("N"));Tracker app=null;Window host=null;
  try{
   app=new Tracker{isolatedThemeTest=true,ShutdownMode=ShutdownMode.OnExplicitShutdown};app.InitializeThemeResources();app.service=new TrackerService(root);app.preferences=app.service.View;app.service.SetWidget("",0,false);
   app.service.AppearanceChanged+=app.ApplyAppearance;app.service.Changed+=app.ApplyAppearance;
   foreach(var title in new[]{"Бумажная история","Путешествие к звёздам","Последний летний день"}){var a=app.service.AddManual(title);app.service.Update(a,title,7,24,"watching",8,"Синтетические данные");a.English="Paper story";a.Genres="Приключения|Драма";}
   app.service.Save();var original=File.ReadAllBytes(app.service.LibraryPath);app.BuildWidget();
   host=new Window{Title="Anime Tracker",Width=940,Height=720,Opacity=0,ShowInTaskbar=false,ShowActivated=false};app.StyleWindow(host);
   var rows=new StackPanel{Margin=new Thickness(24)};rows.Children.Add(app.Text("Библиотека аниме",28,Ink));var query=new TextBox{Text="Поиск по названию",Margin=new Thickness(0,18,0,12)};rows.Children.Add(query);
   var combo=new ComboBox{ItemsSource=new[]{"Все аниме","Смотрю","Просмотрено","В планах"},SelectedIndex=0,Width=240,HorizontalAlignment=HorizontalAlignment.Left};rows.Children.Add(combo);
   foreach(var anime in app.library.Items)rows.Children.Add(app.Row(anime,false));
   var settings=app.BuildSettingsTab(host);var tabs=new TabControl{Background=Brushes.Transparent,BorderThickness=new Thickness(0)};tabs.Items.Add(new TabItem{Header="Библиотека",Content=new SmoothScrollViewer{Content=rows}});tabs.Items.Add(settings);host.Content=new Border{Background=ThemeBrush("#F8EECC"),Child=tabs};host.Show();host.UpdateLayout();
   var theme=ThemeChildren(settings).OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="Тема приложения");var apply=ThemeChildren(settings).OfType<Button>().Single(b=>b.Content as string=="Применить");
   theme.SelectedIndex=1;ThemeCheck(!app.service.Appearance.Dark&&!WindowsPalette.Dark,"draft applied early");
   string screenshots=null;int index=Array.IndexOf(args,"--screenshots");if(index>=0&&index+1<args.Length)screenshots=Path.GetFullPath(args[index+1]);
   foreach(bool dark in new[]{false,true,false,true}){
    app.service.SetAppearance(dark?"dark":"light");app.Dispatcher.Invoke(new Action(delegate{}),DispatcherPriority.ApplicationIdle);host.UpdateLayout();
    ThemeCheck(ThemeColor(host.Background)==(Color)ColorConverter.ConvertFromString(PaperPalette.Resolve("#F8EECC",dark)),"window background");
    ThemeCheck(ThemeColor(query.Foreground)==ThemeColor(Ink)&&ThemeColor(query.Background)==ThemeBrush("#FFF8DF").Color,"existing text box");
    var paper=(DrawingBrush)app.widgetSurface.Background;var baseBrush=((GeometryDrawing)((DrawingGroup)paper.Drawing).Children[0]).Brush;ThemeCheck(ThemeColor(baseBrush)==ThemeBrush("#F9E7A5").Color,"existing widget drawing");
    ThemeCheck(!app.widget.AllowsTransparency&&app.widget.WindowStyle==WindowStyle.None,"opaque Explorer widget changed");
    combo.ApplyTemplate();var popup=(Popup)combo.Template.FindName("PART_Popup",combo);((UIElement)popup.Child).Opacity=0;combo.IsDropDownOpen=true;app.Dispatcher.Invoke(new Action(delegate{}),DispatcherPriority.ApplicationIdle);
    ThemeCheck(ThemeColor(((Border)popup.Child).Background)==ThemeBrush("#FFF8DF").Color,"combo popup");combo.IsDropDownOpen=false;
    ThemeCapture((FrameworkElement)host.Content,screenshots,"windows-"+(dark?"dark":"light"),host.ActualWidth-16,host.ActualHeight-39);
    ThemeCapture(app.widgetSurface,screenshots,"windows-widget-"+(dark?"dark":"light"),app.widget.Width,app.widget.Height);
   }
   // Exercise the real settings handler with the widget disabled and startup unchanged.
   Action applyTheme=delegate{ThemeChildren(settings).OfType<CheckBox>().Single(c=>c.Content as string=="Запускать вместе с Windows").IsEnabled=false;apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};
   tabs.SelectedItem=settings;host.UpdateLayout();theme.SelectedIndex=0;applyTheme();ThemeCheck(!app.service.Appearance.Dark,"settings apply");
   theme.SelectedIndex=1;var reset=ThemeChildren(settings).OfType<Button>().Single(b=>b.Content as string=="Сбросить изменения");reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));ThemeCheck(theme.SelectedIndex==0&&!app.service.Appearance.Dark,"settings reset");
   theme.SelectedIndex=1;applyTheme();ThemeCheck(new TrackerService(root).Appearance.Dark,"settings restart");
   ThemeCheck(File.ReadAllBytes(app.service.LibraryPath).SequenceEqual(original),"personal library changed");
   var notice=app.MessageWindow(host,"Сообщение в выбранной теме","Anime Tracker");notice.Opacity=0;notice.ShowInTaskbar=false;notice.Show();notice.UpdateLayout();ThemeCheck(ThemeColor(notice.Background)==ThemeBrush("#F8EECC").Color,"application message");notice.Close();
   File.WriteAllText(report,"PASS: WPF light/dark repeated live switch; text fields, row/paper brushes, combo popup, opaque widget; settings draft/apply/reset/restart; themed message; personal XML unchanged. Synthetic data; no Explorer attachment, tray, autostart mutation or native OS-scale claim.");
  }catch(Exception e){File.WriteAllText(report,"FAIL "+e);Environment.ExitCode=1;}
  finally{if(app!=null){app.quitting=true;if(host!=null)host.Close();if(app.widget!=null)app.widget.Close();app.Shutdown();}if(Directory.Exists(root))Directory.Delete(root,true);}
 }
}
}
