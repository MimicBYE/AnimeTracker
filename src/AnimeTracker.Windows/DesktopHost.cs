using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace AnimeTracker {
// Attach inside the desktop view, above its icon list, not beside the view.
// Unlike a top-level tool window, this child is not minimized by Show Desktop.
public static class DesktopHost {
 delegate bool EnumProc(IntPtr hwnd, IntPtr param);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,System.Text.StringBuilder text,int count);
 [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd,uint command);
 [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out int value,int size);
 static string Describe(IntPtr h) { var name=new System.Text.StringBuilder(256); GetClassName(h,name,256); NativeRect r; GetWindowRect(h,out r); int cloaked; DwmGetWindowAttribute(h,14,out cloaked,4); return h+" "+name+" visible="+IsWindowVisible(h)+" cloaked="+cloaked+" rect="+r.Left+","+r.Top+","+r.Right+","+r.Bottom; }
 public static string Diagnostics() {
  var report=new System.Text.StringBuilder();
  EnumWindows(delegate(IntPtr h,IntPtr p) { var name=new System.Text.StringBuilder(256); GetClassName(h,name,256); if(name.ToString()=="Progman" || name.ToString()=="WorkerW") { report.AppendLine(Describe(h)); for(var c=GetWindow(h,5);c!=IntPtr.Zero;c=GetWindow(c,2)) { report.AppendLine("  "+Describe(c)); for(var d=GetWindow(c,5);d!=IntPtr.Zero;d=GetWindow(d,2)) report.AppendLine("    "+Describe(d)); } } return true; },IntPtr.Zero);
  return report.ToString();
 }
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr param);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
 [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
 [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd,int command);
 [DllImport("user32.dll", SetLastError=true)] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
 [DllImport("user32.dll", EntryPoint="GetWindowLong")] static extern int GetStyle(IntPtr hwnd, int index);
 [DllImport("user32.dll", EntryPoint="SetWindowLong")] static extern int SetStyle(IntPtr hwnd, int index, int value);
 [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
 [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hwnd, ref NativePoint p);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out NativeRect r);
 [DllImport("user32.dll")] static extern bool GetCursorPos(out NativePoint p);
 [StructLayout(LayoutKind.Sequential)] public struct NativePoint { public int X,Y; }
 [StructLayout(LayoutKind.Sequential)] public struct NativeRect { public int Left,Top,Right,Bottom; }
 public static IntPtr FindHost() {
  IntPtr found=IntPtr.Zero;
  EnumWindows(delegate(IntPtr h, IntPtr unused) {
   var view=FindWindowEx(h,IntPtr.Zero,"SHELLDLL_DefView",null);
   if(view!=IntPtr.Zero && IsWindowVisible(view)) { found=view; return false; }
   return true;
  },IntPtr.Zero);
  return found;
 }
 public static bool Attach(Window window) {
  IntPtr hwnd=new WindowInteropHelper(window).Handle, host=FindHost();
  if(hwnd==IntPtr.Zero || host==IntPtr.Zero) return false;
  if(GetParent(hwnd)==host) return true;
  int style=GetStyle(hwnd,-16);
  SetStyle(hwnd,-16,(style & ~unchecked((int)0x80000000)) | 0x40000000);
  SetParent(hwnd,host);
  if(GetParent(hwnd)!=host) { SetStyle(hwnd,-16,style); return false; }
  SetWindowPos(hwnd,IntPtr.Zero,0,0,0,0,0x0033);
  return true;
 }
 public static void Place(Window window, double left, double top) {
  IntPtr hwnd=new WindowInteropHelper(window).Handle;
  if(hwnd==IntPtr.Zero) return;
  if(IsIconic(hwnd)) ShowWindow(hwnd,4); // Restore without taking keyboard focus.
  var source=PresentationSource.FromVisual(window);
  var scale=source==null?new System.Windows.Media.Matrix(1,0,0,1,0,0):source.CompositionTarget.TransformToDevice;
  var p=new NativePoint { X=(int)Math.Round(left*scale.M11),Y=(int)Math.Round(top*scale.M22) };
  IntPtr parent=GetParent(hwnd); if(parent!=IntPtr.Zero) ScreenToClient(parent,ref p);
  int width=(int)Math.Ceiling(window.Width*scale.M11),height=(int)Math.Ceiling(window.Height*scale.M22);
  NativeRect current; GetWindowRect(hwnd,out current);
  var actual=new NativePoint { X=current.Left,Y=current.Top }; if(parent!=IntPtr.Zero) ScreenToClient(parent,ref actual);
  bool samePosition=actual.X==p.X && actual.Y==p.Y, sameSize=current.Right-current.Left==width && current.Bottom-current.Top==height;
  bool onTop=parent==IntPtr.Zero || GetWindow(parent,5)==hwnd;
  if(samePosition && sameSize && onTop && IsWindowVisible(hwnd)) return;
  uint flags=0x0010|0x0040;
  if(samePosition) flags|=0x0002; if(sameSize) flags|=0x0001; if(onTop) flags|=0x0004;
  SetWindowPos(hwnd,IntPtr.Zero,p.X,p.Y,width,height,flags);
 }
 public static Point Cursor() { NativePoint p; GetCursorPos(out p); return new Point(p.X,p.Y); }
 public static double Scale(Window window) { var s=PresentationSource.FromVisual(window); return s==null?1:s.CompositionTarget.TransformToDevice.M11; }
 public static string Status(Window window) {
  var h=new WindowInteropHelper(window).Handle; NativeRect r; GetWindowRect(h,out r);
  return "attached="+(GetParent(h)==FindHost() && GetParent(h)!=IntPtr.Zero)+"; child="+((GetStyle(h,-16)&0x40000000)!=0)+"; visible="+IsWindowVisible(h)+"; minimized="+IsIconic(h)+"; aboveIcons="+(GetWindow(GetParent(h),5)==h)+"; layered="+((GetStyle(h,-20)&0x80000)!=0)+"; rect="+r.Left+","+r.Top+","+(r.Right-r.Left)+","+(r.Bottom-r.Top);
 }
}
}
