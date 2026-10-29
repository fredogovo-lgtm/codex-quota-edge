using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
namespace CodexEdgeWidget;
internal sealed class NativeDock(Window window,bool preview=false) {
 private nint hwnd;private POINT pressed;private int offsetX,offsetY;private bool candidate,dragHorizontal;
 public bool Expanded {get;set;}
 public bool IsDragging {get;private set;}
 public bool DetailsOnRight {get;private set;}
 public double GaugeOffset {get;private set;}
 public DockPosition Position {get;set;}=new(DockEdge.Right,.5);
 public event Action? LayoutChanged;
 public event Action? DragStarted;
 public event Action<DockPosition>? PositionChanged;
 public void Attach() {
  hwnd=new WindowInteropHelper(window).Handle;
  long ex=GetWindowLongPtr(hwnd,-20).ToInt64();SetWindowLongPtr(hwnd,-20,(nint)(preview ? (ex&~0x80)|0x40000|0x08000000 : ex|0x80|0x08000000));
  HwndSource.FromHwnd(hwnd)?.AddHook(Hook);Place();
 }
 private nint Hook(nint h,int msg,nint w,nint l,ref bool handled) {
  if(msg==0x21){handled=true;return 3;} // MA_NOACTIVATE: retain focus in the user's editor.
  if(msg is 0x2E0 or 0x7E or 0x1A)window.Dispatcher.BeginInvoke(Place);
  return 0;
 }
 public void Place() {
  if(hwnd==0||IsDragging)return;
  var monitor=FindMonitor();var info=Info(monitor);if(info is null)return;
  double scale=Scale(monitor);var placement=DockGeometry.Calculate(Work(info.Value),scale,Position,Expanded);
  GaugeOffset=placement.GaugeOffsetX/scale;DetailsOnRight=placement.DetailsOnRight;LayoutChanged?.Invoke();Move(placement.Bounds);
 }
 public void BeginPointer() {
  if(hwnd==0||!GetCursorPos(out pressed)||!GetWindowRect(hwnd,out var bounds))return;
  dragHorizontal=DockGeometry.IsHorizontal(Position);double scale=Scale(MonitorFromWindow(hwnd,2));
  int compactWidth=(int)Math.Round((dragHorizontal?84:26)*scale),compactHeight=(int)Math.Round((dragHorizontal?26:84)*scale);
  int gaugeX=dragHorizontal?bounds.left+(int)Math.Round(GaugeOffset*scale):DetailsOnRight?bounds.left:bounds.right-compactWidth;
  int gaugeY=dragHorizontal&&Position.Edge==DockEdge.Bottom?bounds.bottom-compactHeight:bounds.top;
  offsetX=Math.Clamp(pressed.x-gaugeX,0,compactWidth-1);offsetY=Math.Clamp(pressed.y-gaugeY,0,compactHeight-1);candidate=true;
 }
 public void UpdatePointer() {
  if(!candidate||!GetCursorPos(out var cursor))return;
  var monitor=MonitorFromPoint(cursor,2);var info=Info(monitor);if(info is null)return;
  double scale=Scale(monitor);
  if(!IsDragging&&Math.Abs(cursor.x-pressed.x)<SystemParameters.MinimumHorizontalDragDistance*scale&&Math.Abs(cursor.y-pressed.y)<SystemParameters.MinimumVerticalDragDistance*scale)return;
  if(!IsDragging){IsDragging=true;Expanded=false;GaugeOffset=0;DragStarted?.Invoke();}
  int width=(int)Math.Round((dragHorizontal?84:26)*scale),height=(int)Math.Round((dragHorizontal?26:84)*scale);var work=info.Value.work;
  int x=Math.Clamp(cursor.x-offsetX,work.left,Math.Max(work.left,work.right-width));
  int y=Math.Clamp(cursor.y-offsetY,work.top,Math.Max(work.top,work.bottom-height));
  Move(new(x,y,width,height));
 }
 public bool EndPointer() {
  candidate=false;bool dragged=IsDragging;IsDragging=false;
  if(dragged&&GetWindowRect(hwnd,out var bounds)) {
   var monitor=MonitorFromWindow(hwnd,2);var info=Info(monitor);
   if(info is {} value){Position=DockGeometry.Snap(Work(value),Scale(monitor),bounds.left,bounds.top,value.device,dragHorizontal);PositionChanged?.Invoke(Position);}
   Place();
  }
  return dragged;
 }
 private void Move(DockBounds b)=>SetWindowPos(hwnd,(nint)(-1),b.X,b.Y,b.Width,b.Height,0x10|0x40);
 private nint FindMonitor() {
  nint found=0;
  if(!string.IsNullOrEmpty(Position.Monitor)) {
   MonitorEnum callback=(nint h,nint dc,ref RECT r,nint data)=>{var info=Info(h);if(info?.device==Position.Monitor)found=h;return true;};
   EnumDisplayMonitors(0,0,callback,0);
  }
  return found!=0?found:MonitorFromWindow(hwnd,2);
 }
 private double Scale(nint monitor) {
  try{if(GetDpiForMonitor(monitor,0,out uint x,out _)==0&&x>0)return x/96d;}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}
  return VisualTreeHelper.GetDpi(window).DpiScaleX;
 }
 private static DockBounds Work(MONITORINFO info)=>new(info.work.left,info.work.top,info.work.right-info.work.left,info.work.bottom-info.work.top);
 private static MONITORINFO? Info(nint monitor){var info=new MONITORINFO{size=Marshal.SizeOf<MONITORINFO>(),device=""};return GetMonitorInfo(monitor,ref info)?info:null;}
 [StructLayout(LayoutKind.Sequential)]private struct POINT {public int x,y;}
 [StructLayout(LayoutKind.Sequential)]private struct RECT {public int left,top,right,bottom;}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct MONITORINFO {public int size;public RECT monitor,work;public uint flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string device;}
 private delegate bool MonitorEnum(nint h,nint dc,ref RECT rect,nint data);
 [DllImport("user32.dll")]private static extern bool EnumDisplayMonitors(nint dc,nint clip,MonitorEnum callback,nint data);
 [DllImport("user32.dll")]private static extern nint MonitorFromPoint(POINT point,uint flags);
 [DllImport("user32.dll")]private static extern bool GetCursorPos(out POINT point);
 [DllImport("user32.dll")]private static extern bool GetWindowRect(nint hwnd,out RECT rect);
 [DllImport("user32.dll")]private static extern nint MonitorFromWindow(nint hwnd,uint flags);
 [DllImport("user32.dll",EntryPoint="GetMonitorInfoW",CharSet=CharSet.Unicode)]private static extern bool GetMonitorInfo(nint h,ref MONITORINFO info);
 [DllImport("shcore.dll")]private static extern int GetDpiForMonitor(nint monitor,int type,out uint x,out uint y);
 [DllImport("user32.dll")]private static extern bool SetWindowPos(nint h,nint after,int x,int y,int w,int hgt,uint flags);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern nint GetWindowLongPtr(nint h,int index);
 [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]private static extern nint SetWindowLongPtr(nint h,int index,nint value);
}
