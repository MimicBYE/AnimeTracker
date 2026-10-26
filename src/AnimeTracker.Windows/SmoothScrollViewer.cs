using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace AnimeTracker {
// Pixel scrolling shared by the library, rankings and expanded desktop widget.
public class SmoothScrollViewer : ScrollViewer {
 readonly DispatcherTimer timer;
 readonly Stopwatch clock=new Stopwatch();
 double target, previousSeconds;
 int direction;
 public SmoothScrollViewer() {
  CanContentScroll=false;
  timer=new DispatcherTimer(DispatcherPriority.Render) { Interval=TimeSpan.FromMilliseconds(16) };
  timer.Tick+=Tick;
  Unloaded+=delegate { Stop(); };
  IsVisibleChanged+=delegate { if(!IsVisible) Stop(); };
  // Direct manipulation and keyboard navigation take over immediately.
  PreviewMouseDown+=delegate { Stop(); };
  PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {
   if(e.Key==Key.Up || e.Key==Key.Down || e.Key==Key.PageUp || e.Key==Key.PageDown || e.Key==Key.Home || e.Key==Key.End) Stop();
  };
  ScrollChanged+=delegate(object sender,ScrollChangedEventArgs e) {
   if(e.OriginalSource==this && (e.ExtentHeightChange!=0 || e.ViewportHeightChange!=0)) Stop();
  };
 }
 static double Clamp(double value,double maximum) { return Math.Max(0,Math.Min(value,Math.Max(0,maximum))); }
 internal static double WheelTarget(double current,double pending,int oldDirection,int delta,double viewport,double maximum,int lines) {
  int nextDirection=Math.Sign(delta);
  // On reversal discard the untravelled distance instead of fighting the wheel.
  double start=oldDirection!=0 && oldDirection!=nextDirection?current:pending;
  double step=lines<0?viewport:24.0*lines;
  return Clamp(start-delta/120.0*step,maximum);
 }
 internal static double NextOffset(double current,double target,double elapsed) {
  return current+(target-current)*(1-Math.Exp(-Math.Max(0,elapsed)/0.055));
 }
 protected override void OnMouseWheel(MouseWheelEventArgs e) {
  if(e.Handled || e.Delta==0 || SystemParameters.WheelScrollLines==0) return;
  double pending=timer.IsEnabled?target:VerticalOffset;
  double next=WheelTarget(VerticalOffset,pending,timer.IsEnabled?direction:0,e.Delta,ViewportHeight,ScrollableHeight,SystemParameters.WheelScrollLines);
  if(!timer.IsEnabled && Math.Abs(next-VerticalOffset)<0.1) return;
  target=next; direction=Math.Sign(e.Delta); e.Handled=true;
  if(!timer.IsEnabled) { clock.Restart(); previousSeconds=0; timer.Start(); }
 }
 void Tick(object sender,EventArgs e) {
  if(!IsLoaded || !IsVisible) { Stop(); return; }
  double seconds=clock.Elapsed.TotalSeconds,elapsed=seconds-previousSeconds; previousSeconds=seconds;
  target=Clamp(target,ScrollableHeight);
  double next=NextOffset(VerticalOffset,target,elapsed);
  if(Math.Abs(target-next)<0.5) { ScrollToVerticalOffset(target); Stop(); }
  else ScrollToVerticalOffset(next);
 }
 void Stop() { timer.Stop(); clock.Stop(); direction=0; }
 public void ResetToTop() { Stop(); target=0; ScrollToTop(); }
 public static void Test() {
  if(WheelTarget(100,172,-1,-120,300,1000,3)!=244) throw new Exception("Wheel accumulation");
  if(WheelTarget(100,244,-1,120,300,1000,3)!=28) throw new Exception("Wheel reversal");
  if(WheelTarget(10,10,0,120,300,1000,3)!=0 || WheelTarget(980,980,0,-120,300,1000,3)!=1000) throw new Exception("Wheel bounds");
  if(WheelTarget(0,0,0,-30,300,1000,3)!=18 || WheelTarget(0,0,0,-120,300,1000,-1)!=300) throw new Exception("High resolution/page wheel");
  double current=0; for(int i=0;i<30;i++) { double next=NextOffset(current,200,0.016); if(next<current || next>200) throw new Exception("Scroll overshoot"); current=next; }
  if(200-current>0.5 || NextOffset(100,0,0.016)>=100) throw new Exception("Scroll convergence");
 }
}
}
