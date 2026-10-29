using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexEdgeWidget;
internal static class DockWindowTests {
 [STAThread] static int Main(string[] args) {
  bool restore=args.Length==2&&args[0]=="--restore-only";
  int failures=0;var root=restore?args[1]:Path.Combine(Path.GetTempPath(),"CodexDockWindow-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  var store=new PreferenceStore(Path.Combine(root,"settings.json"));if(!restore)store.Save(DisplayStyle.Vertical);
  var app=new Application();var window=new MainWindow(new Fixture(),store,true);
  void Click(){window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseDownEvent});window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseUpEvent});}
  void Check(bool ok,string name){Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)failures++;}
  window.Loaded+=async(_,_)=>{try{
   await Task.Delay(100);
   var item=window.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault(i=>(i.Header?.ToString()??"").Contains("锁定"));
   Check(item!=null,"right-click menu exposes persistent position lock");
   if(item==null)return;
   if(restore) {
    Check(item.Header?.ToString()=="解除锁定位置","new process restores position lock menu");
    GetWindowRect(new WindowInteropHelper(window).Handle,out var restoredBounds);
    var expected=store.LoadDock();double scale=VisualTreeHelper.GetDpi(window).DpiScaleX;var work=SystemParameters.WorkArea;
    bool horizontal=expected.Position.Edge is DockEdge.Top or DockEdge.Bottom;
    bool anchored=expected.Position.Edge switch {DockEdge.Top=>Math.Abs(restoredBounds.top-work.Top*scale)<2,DockEdge.Bottom=>Math.Abs(restoredBounds.bottom-work.Bottom*scale)<2,DockEdge.Right=>Math.Abs(restoredBounds.right-work.Right*scale)<2,_=>Math.Abs(restoredBounds.left-work.Left*scale)<2};
    Check(anchored,"new process restores actual saved screen edge");
    Check(horizontal?window.Width==84&&window.Height==26:window.Width==26&&window.Height==84,"new process restores edge-aligned orientation");
    Check(expected.Locked,"new process retains saved position and lock");return;
   }
   item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
   Check(item.Header?.ToString()=="解除锁定位置","locking changes menu action to unlock");
   var json=File.ReadAllText(Path.Combine(root,"settings.json"));Check(json.Contains("true")&&store.Load()==DisplayStyle.Vertical,"lock saves without losing current style");
   Click();
   Check(store.Load()==DisplayStyle.Ring,"locked panel still switches styles on click");
   item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Check(item.Header?.ToString()=="锁定位置","unlock restores drag menu action");
   window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseUpEvent});Check(store.Load()==DisplayStyle.Ring,"release without panel press cannot change style");
   window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseDownEvent});window.ReleaseMouseCapture();
   window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseUpEvent});Check(store.Load()==DisplayStyle.Ring,"cancelled capture cannot change style on release");
   var dock=typeof(MainWindow).GetField("dock",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
   var dockType=dock.GetType();var position=dockType.GetProperty("Position")!;
   void Place(DockPosition p){SetExpanded(false);position.SetValue(dock,p);dockType.GetMethod("Place")!.Invoke(dock,null);window.UpdateLayout();}
   void SetExpanded(bool expanded)=>typeof(MainWindow).GetMethod("SetExpanded",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,[expanded]);
   RECT Bounds(){GetWindowRect(new WindowInteropHelper(window).Handle,out var b);return b;}
   void Snap(string name){window.UpdateLayout();var image=new RenderTargetBitmap((int)window.Width*3,(int)window.Height*3,288,288,PixelFormats.Pbgra32);image.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(root,name+".png"));encoder.Save(file);}
   foreach(var edge in Enum.GetValues<DockEdge>()) {
    Place(new(edge,.25));var compact=Bounds();bool horizontal=edge is DockEdge.Top or DockEdge.Bottom;
    Check(horizontal?window.Width==84&&window.Height==26:window.Width==26&&window.Height==84,edge+" uses edge-aligned collapsed orientation");
    if(horizontal) {
     foreach(var mode in Enum.GetValues<DisplayStyle>()) {
      for(int i=0;i<3&&store.Load()!=mode;i++)Click();window.UpdateLayout();
      Check(HorizontalPixels(window,mode==DisplayStyle.Vertical),edge+" "+mode+" paints three horizontal quota indicators");Snap(edge+"-"+mode+"-collapsed");
      SetExpanded(true);Snap(edge+"-"+mode+"-expanded");SetExpanded(false);
     }
     for(int i=0;i<3&&store.Load()!=DisplayStyle.Ring;i++)Click();
    }
    SetExpanded(true);await Task.Delay(30);var expanded=Bounds();
    bool detailRight=(bool)dockType.GetProperty("DetailsOnRight")!.GetValue(dock)!;
    if(horizontal) {
     Check(window.Width==184&&window.Height==110,edge+" expands details toward screen interior");
     double offset=Convert.ToDouble(dockType.GetProperty("GaugeOffset")!.GetValue(dock));double scale=VisualTreeHelper.GetDpi(window).DpiScaleX;
     int gaugeX=expanded.left+(int)Math.Round(offset*scale);int gaugeY=edge==DockEdge.Top?expanded.top:expanded.bottom-(compact.bottom-compact.top);
     Check(Math.Abs(gaugeX-compact.left)<=1&&gaugeY==compact.top,edge+" keeps horizontal gauge band anchored on hover");
    } else {
     int gaugeX=detailRight?expanded.left:expanded.right-(compact.right-compact.left);
     Check(gaugeX==compact.left&&expanded.top==compact.top,edge+" expands inward without moving gauge anchor");
    }
    Snap(edge.ToString());
   }
   GetCursorPos(out var original);
   try {
    var screen=SystemParameters.WorkArea;double screenScale=VisualTreeHelper.GetDpi(window).DpiScaleX;
    Place(new(DockEdge.Right,.5));
    foreach(var target in new[]{DockEdge.Top,DockEdge.Bottom,DockEdge.Right,DockEdge.Top,DockEdge.Left}) {
     SetExpanded(false);var origin=Bounds();SetCursorPos((origin.left+origin.right)/2,(origin.top+origin.bottom)/2);
     window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseDownEvent});
     int x=(int)((target==DockEdge.Left?screen.Left+5:target==DockEdge.Right?screen.Right-5:screen.Left+screen.Width*.5)*screenScale);
     int y=(int)((target==DockEdge.Top?screen.Top+5:target==DockEdge.Bottom?screen.Bottom-5:screen.Top+screen.Height*.5)*screenScale);
     SetCursorPos(x,y);window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.MouseMoveEvent});
     window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseUpEvent});window.UpdateLayout();
     Check(store.LoadDock().Position.Edge==target&&((target is DockEdge.Top or DockEdge.Bottom)?window.Width==84&&window.Height==26:window.Width==26&&window.Height==84),"cross-edge drag rotates and snaps to "+target);
     Check(store.Load()==DisplayStyle.Ring,"cross-edge drag to "+target+" preserves selected style");
    }
    Place(new(DockEdge.Right,.5));var b=Bounds();Check(SetCursorPos(b.left+10,b.top+35),"test can position pointer on own window");GetCursorPos(out var beforePointer);
    window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseDownEvent});
    var work=SystemParameters.WorkArea;double dpi=VisualTreeHelper.GetDpi(window).DpiScaleX;
    Check(SetCursorPos((int)(work.Left*dpi)+10,(int)((work.Top+work.Height*.7)*dpi)),"test can move pointer to target edge");GetCursorPos(out var afterPointer);
    Console.WriteLine($"POINTER {beforePointer.x},{beforePointer.y} -> {afterPointer.x},{afterPointer.y}; down={typeof(MainWindow).GetField("pointerDown",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)}");
    window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.MouseMoveEvent});
    window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseUpEvent});
    Check(store.LoadDock().Position.Edge==DockEdge.Left,"drag snaps and persists nearest edge");
    Check(store.Load()==DisplayStyle.Ring,"drag never cycles the selected style");
    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));b=Bounds();SetCursorPos(b.left+10,b.top+35);
    window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseDownEvent});SetCursorPos(b.left+250,b.top+40);
    window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.MouseMoveEvent});var lockedBounds=Bounds();
    window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseUpEvent});
    Check(lockedBounds.left==b.left&&lockedBounds.top==b.top,"locked panel cannot be dragged");
    var restored=new PreferenceStore(Path.Combine(root,"settings.json")).LoadDock();Check(restored.Locked&&restored.Position.Edge==DockEdge.Left,"new preference instance restores edge and lock");
   }finally{SetCursorPos(original.x,original.y);}
  }catch(Exception e){failures++;Console.WriteLine("FAIL "+e);}finally{window.Close();}};
  app.Run(window);Console.WriteLine("ARTIFACTS "+root);Console.WriteLine("DOCK WINDOW "+(failures==0?"PASS":"FAIL"));return failures==0?0:1;
 }
 [StructLayout(LayoutKind.Sequential)]private struct RECT{public int left,top,right,bottom;}
 [StructLayout(LayoutKind.Sequential)]private struct POINT{public int x,y;}
 [DllImport("user32.dll")]private static extern bool GetWindowRect(nint h,out RECT r);
 [DllImport("user32.dll")]private static extern bool GetCursorPos(out POINT p);
 [DllImport("user32.dll")]private static extern bool SetCursorPos(int x,int y);
 private static bool HorizontalPixels(Window window,bool longBars) {
  window.UpdateLayout();int w=(int)window.ActualWidth,h=(int)window.ActualHeight;if(w!=84||h!=26)return false;
  var bitmap=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);bitmap.Render(window);byte[] pixels=new byte[w*h*4];bitmap.CopyPixels(pixels,w*4,0);
  Color[] colors=[Color.FromRgb(96,214,188),Color.FromRgb(229,181,102),Color.FromRgb(117,169,237)];
  double[] centerX=new double[3],centerY=new double[3];int[] minX=[w,w,w],maxX=new int[3];
  for(int c=0;c<3;c++) {
   int count=0;double sx=0,sy=0;
   for(int y=0;y<h;y++)for(int x=0;x<w;x++){int p=(y*w+x)*4;if(Math.Abs(pixels[p]-colors[c].B)<8&&Math.Abs(pixels[p+1]-colors[c].G)<8&&Math.Abs(pixels[p+2]-colors[c].R)<8){count++;sx+=x;sy+=y;minX[c]=Math.Min(minX[c],x);maxX[c]=Math.Max(maxX[c],x);}}
   if(count<4)return false;centerX[c]=sx/count;centerY[c]=sy/count;
  }
  return longBars?Math.Abs(minX[0]-minX[1])<=1&&Math.Abs(minX[1]-minX[2])<=1&&maxX[0]>maxX[1]+10&&maxX[0]>maxX[2]+10&&centerY[1]>centerY[0]+3&&centerY[2]>centerY[1]+3:centerX[1]>centerX[0]+10&&centerX[2]>centerX[1]+10&&Math.Abs(centerY[0]-centerY[1])<4&&Math.Abs(centerY[1]-centerY[2])<4;
 }
 private sealed class Fixture:IQuotaClient {public Task<QuotaData> FetchAsync(CancellationToken c)=>Task.FromResult(new QuotaData(new(32,300,DateTimeOffset.UtcNow.AddHours(2)),new(58,10080,DateTimeOffset.UtcNow.AddDays(3))));public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}
}
