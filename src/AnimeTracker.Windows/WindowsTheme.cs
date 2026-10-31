using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace AnimeTracker {
// Mutable, shared brushes update existing controls and paper drawings in place.
static class WindowsPalette {
 internal static bool Dark;
 sealed class Tint:DependencyObject {
  public static readonly DependencyProperty ValueProperty=DependencyProperty.Register("Value",typeof(Color),typeof(Tint));
  public Color Value {get{return (Color)GetValue(ValueProperty);}set{SetValue(ValueProperty,value);}}
  public readonly SolidColorBrush Brush;
  public Tint(Color color){Value=color;Brush=new SolidColorBrush();BindingOperations.SetBinding(Brush,SolidColorBrush.ColorProperty,new Binding("Value"){Source=this,Mode=BindingMode.OneWay});}
 }
 static readonly Dictionary<Color,Tint> brushes=new Dictionary<Color,Tint>();
 internal static Color Resolve(Color light) {
  var rgb="#"+light.R.ToString("X2")+light.G.ToString("X2")+light.B.ToString("X2");
  var color=(Color)ColorConverter.ConvertFromString(PaperPalette.Resolve(rgb,Dark));color.A=light.A;return color;
 }
 internal static SolidColorBrush Brush(Color light) {
  Tint tint;if(!brushes.TryGetValue(light,out tint)){tint=new Tint(Resolve(light));brushes[light]=tint;}return tint.Brush;
 }
 // A binding also prevents WPF from freezing brushes when it seals styles/drawings.
 internal static void Apply(bool dark) {Dark=dark;foreach(var pair in brushes)pair.Value.Value=Resolve(pair.Key);}
}
public partial class Tracker {
 bool themeResourcesReady,lastDark;
 static SolidColorBrush ThemeBrush(Color light) {return WindowsPalette.Brush(light);}
 static SolidColorBrush ThemeBrush(string light) {return ThemeBrush((Color)ColorConverter.ConvertFromString(light));}
 void ApplyAppearance() {
  bool dark=service!=null&&service.Appearance.Dark;if(themeResourcesReady&&lastDark==dark)return;
  WindowsPalette.Apply(dark);themeResourcesReady=true;lastDark=dark;
  foreach(var light in PaperPalette.LightColors)Resources[PaperPalette.Key(light)]=ThemeBrush(light);
  foreach(Window window in Windows)ApplyWindowTheme(window);
  ApplyTrayTheme();
 }
 static int NativeColor(Brush brush) {var c=((SolidColorBrush)brush).Color;return c.R|(c.G<<8)|(c.B<<16);}
 void ApplyWindowTheme(Window window) {
  var handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero||window.WindowStyle==WindowStyle.None)return;
  int dark=WindowsPalette.Dark?1:0,background=NativeColor(ThemeBrush("#F8EECC")),foreground=NativeColor(Ink),border=NativeColor(ThemeBrush("#D6C18B"));
  // Unsupported attributes are ignored on older Windows; no global OS setting is changed.
  DwmSetWindowAttribute(handle,20,ref dark,4);DwmSetWindowAttribute(handle,35,ref background,4);
  DwmSetWindowAttribute(handle,36,ref foreground,4);DwmSetWindowAttribute(handle,34,ref border,4);
 }
 sealed class TrayColors:Forms.ProfessionalColorTable {
  static System.Drawing.Color Color(string light) {var c=ThemeBrush(light).Color;return System.Drawing.Color.FromArgb(c.R,c.G,c.B);}
  public override System.Drawing.Color ToolStripDropDownBackground {get{return Color("#FFF8DF");}}
  public override System.Drawing.Color ImageMarginGradientBegin {get{return Color("#FFF8DF");}}
  public override System.Drawing.Color ImageMarginGradientMiddle {get{return Color("#FFF8DF");}}
  public override System.Drawing.Color ImageMarginGradientEnd {get{return Color("#FFF8DF");}}
  public override System.Drawing.Color MenuBorder {get{return Color("#D6C18B");}}
  public override System.Drawing.Color MenuItemSelected {get{return Color("#EAD490");}}
  public override System.Drawing.Color MenuItemBorder {get{return Color("#C8A65C");}}
  public override System.Drawing.Color MenuItemSelectedGradientBegin {get{return Color("#EAD490");}}
  public override System.Drawing.Color MenuItemSelectedGradientEnd {get{return Color("#EAD490");}}
  public override System.Drawing.Color CheckBackground {get{return Color("#EAD490");}}
  public override System.Drawing.Color CheckSelectedBackground {get{return Color("#EAD490");}}
  public override System.Drawing.Color CheckPressedBackground {get{return Color("#E3C977");}}
  public override System.Drawing.Color SeparatorDark {get{return Color("#D6C18B");}}
  public override System.Drawing.Color SeparatorLight {get{return Color("#FFF8DF");}}
 }
 void ApplyTrayTheme() {
  if(tray==null||tray.ContextMenuStrip==null)return;var menu=tray.ContextMenuStrip;
  menu.Renderer=new Forms.ToolStripProfessionalRenderer(new TrayColors()){RoundedEdges=false};
  var c=((SolidColorBrush)Ink).Color;menu.ForeColor=System.Drawing.Color.FromArgb(c.R,c.G,c.B);menu.Invalidate(true);
 }
 static void ShowMessage(string text) {ShowMessage(null,text,"Anime Tracker");}
 static void ShowMessage(string text,string title) {ShowMessage(null,text,title);}
 static void ShowMessage(Window owner,string text,string title) {
  var app=Current as Tracker;if(app==null){MessageBox.Show(text,title);return;}
  owner=owner??app.Windows.OfType<Window>().FirstOrDefault(w=>w.IsActive&&w!=app.widget);
  app.MessageWindow(owner,text,title).ShowDialog();
 }
 Window MessageWindow(Window owner,string text,string title) {
  var window=new Window{Title=title,Width=520,SizeToContent=SizeToContent.Height,MaxHeight=600,MinHeight=160,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=owner==null,WindowStartupLocation=owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner};
  if(owner!=null)window.Owner=owner;StyleWindow(window);var panel=new DockPanel{Margin=new Thickness(24)};
  var close=Primary(Btn("Закрыть",delegate{window.Close();}));close.IsDefault=true;close.IsCancel=true;close.HorizontalAlignment=HorizontalAlignment.Right;close.Margin=new Thickness(0,20,0,0);DockPanel.SetDock(close,Dock.Bottom);panel.Children.Add(close);
  panel.Children.Add(new SmoothScrollViewer{Content=Text(text,14,Ink),MaxHeight=420,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});window.Content=panel;return window;
 }
}
}
