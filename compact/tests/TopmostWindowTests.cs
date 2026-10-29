using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CodexEdgeWidget;

internal static class TopmostWindowTests {
 private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
 private const uint NoMove=0x2,NoSize=0x1,NoActivate=0x10;
 private static int failures,checks;
 [STAThread] static int Main(string[] args) {
  // All persistence and windows belong to this fixture; no user's widget or Codex process is touched.
  string root=args.Length==1?Path.GetFullPath(args[0]):Path.Combine(AppContext.BaseDirectory,"fixture-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(root);string path=Path.Combine(root,"settings.json");
  var store=new PreferenceStore(path);store.Save(DisplayStyle.Line);store.SaveDock(new(new(DockEdge.Left,.31),true));
  var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
  MainWindow? window=new(new Fixture(),store,true);Window? cover=null;
  window.Loaded+=async(_,_)=>{
   try {
    await Task.Delay(120);StopTimers(window);
    var menuField=typeof(MainWindow).GetField("topmostItem",Private);
    var enabledField=typeof(MainWindow).GetField("topmostEnabled",Private);
    var maintain=typeof(MainWindow).GetMethod("MaintainVisibility",Private);
    var restore=typeof(MainWindow).GetMethod("RestorePanel",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly);
    var loadTopmost=typeof(PreferenceStore).GetMethod("LoadTopmost",BindingFlags.Public|BindingFlags.Instance);
    Check(menuField!=null,"right-click topmost menu field exists");
    Check(enabledField!=null,"persistent topmost state exists");
    Check(maintain!=null,"visibility maintenance entry exists");
    Check(restore!=null,"repeat-launch panel restore entry exists");
    Check(loadTopmost!=null,"preference topmost reader exists");
    if(menuField==null||enabledField==null||maintain==null||restore==null||loadTopmost==null)return;
    var item=(MenuItem)menuField.GetValue(window)!;
    Check(window.ContextMenu!.Items.Contains(item)&&item.IsCheckable,"right-click topmost menu is checkable");
    Check((bool)enabledField.GetValue(window)!&&item.IsChecked&&item.Header?.ToString()=="取消置顶","existing settings default to topmost with clear action");
    Check((bool)loadTopmost.Invoke(store,null)!,"old settings without topmost key default to enabled");
    Check(IsTopmost(window),"default panel is a native topmost window");
    var clock=(DispatcherTimer)typeof(MainWindow).GetField("clock",Private)!.GetValue(window)!;
    Check(clock.Interval<=TimeSpan.FromSeconds(3),"maintenance dispatcher cadence can recover within three seconds");
    Click(item);
    Check(!(bool)enabledField.GetValue(window)!&&!item.IsChecked&&item.Header?.ToString()=="置顶显示","menu disables and describes topmost state");
    Check(!window.Topmost&&!IsTopmost(window),"disabling topmost updates WPF and native window");
    Check(!(bool)loadTopmost.Invoke(new PreferenceStore(path),null)!,"disabled preference is persisted immediately");
    object dock=typeof(MainWindow).GetField("dock",Private)!.GetValue(window)!;
    var dockType=dock.GetType();var position=dockType.GetProperty("Position")!;
    var place=dockType.GetMethod("Place")!;var setExpanded=typeof(MainWindow).GetMethod("SetExpanded",Private)!;
    foreach(var edge in Enum.GetValues<DockEdge>()) {
     setExpanded.Invoke(window,[false]);position.SetValue(dock,new DockPosition(edge,.37));place.Invoke(dock,null);
     setExpanded.Invoke(window,[true]);setExpanded.Invoke(window,[false]);
     maintain.Invoke(window,null);
     Check(!window.Topmost&&!IsTopmost(window),edge+" placement and hover preserve non-topmost setting");
    }
    // Closing then constructing another WPF window exercises a real native handle, not only a preference instance.
    await Close(window);window=new MainWindow(new Fixture(),new PreferenceStore(path),true);window.Show();await Task.Delay(100);StopTimers(window);
    item=(MenuItem)menuField.GetValue(window)!;dock=typeof(MainWindow).GetField("dock",Private)!.GetValue(window)!;
    Check(!(bool)enabledField.GetValue(window)!&&!item.IsChecked&&!IsTopmost(window),"new native window restores disabled topmost setting");
    var expectedDock=store.LoadDock();var expectedStyle=store.Load();
    Check(expectedDock.Locked&&expectedStyle==DisplayStyle.Line,"topmost toggles retain style and position lock");
    Click(item);Check((bool)enabledField.GetValue(window)!&&item.IsChecked&&IsTopmost(window),"menu enables topmost immediately");
    Check((bool)loadTopmost.Invoke(new PreferenceStore(path),null)!,"enabled preference is persisted immediately");
    var initial=Bounds(window);var before=GetForegroundWindow();
    Demote(window);Check(!IsTopmost(window),"fixture can reproduce native topmost loss");
    maintain.Invoke(window,null);Check(IsTopmost(window),"maintenance repairs native topmost loss");
    Check(GetForegroundWindow()==before,"topmost repair does not activate or steal foreground");
    Check(Bounds(window)==initial&&store.Load()==expectedStyle&&store.LoadDock()==expectedDock,"repair preserves bounds style and lock");
    Demote(window);var restartedClock=(DispatcherTimer)typeof(MainWindow).GetField("clock",Private)!.GetValue(window)!;
    restartedClock.Start();await Task.Delay(3400);restartedClock.Stop();
    Check(IsTopmost(window),"live dispatcher watchdog automatically recovers a demoted panel within its cadence");
    // Put an ordinary, non-activating fixture window over the panel and inspect actual native Z order.
    cover=new Window{Width=260,Height=200,Left=window.Left-80,Top=window.Top-30,ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,Background=Brushes.DarkSlateGray};
    cover.Show();await Task.Delay(30);before=GetForegroundWindow();Demote(window);
    SetWindowPos(Handle(cover),0,0,0,0,0,NoMove|NoSize|NoActivate);
    maintain.Invoke(window,null);
    Check(IsAbove(Handle(window),Handle(cover)),"recovered panel is above an ordinary overlapping application window");
    Check(GetForegroundWindow()==before,"repair above overlapping window keeps previous foreground");
    // Even another topmost surface cannot permanently demote our fixture: next maintenance brings it forward.
    cover.Topmost=true;SetWindowPos(Handle(cover),new nint(-1),0,0,0,0,NoMove|NoSize|NoActivate);
    maintain.Invoke(window,null);Check(IsAbove(Handle(window),Handle(cover)),"maintenance restores front of topmost band after a competing fixture");
    cover.Close();cover=null;
    before=GetForegroundWindow();window.Hide();Check(!IsWindowVisible(Handle(window)),"fixture reproduces hidden panel");
    maintain.Invoke(window,null);Check(IsWindowVisible(Handle(window))&&window.IsVisible&&IsTopmost(window),"maintenance restores a hidden topmost panel");
    Check(GetForegroundWindow()==before,"hidden-panel recovery does not activate foreground");
    Check(Bounds(window)==initial,"hidden-panel recovery preserves edge placement and size");
    ShowWindow(Handle(window),0);Check(window.IsVisible&&!IsWindowVisible(Handle(window)),"fixture reproduces native-only hiding without changing WPF visibility");
    before=GetForegroundWindow();maintain.Invoke(window,null);
    Check(IsWindowVisible(Handle(window))&&IsTopmost(window),"maintenance restores native-only hidden panel");
    Check(GetForegroundWindow()==before&&Bounds(window)==initial,"native-only hidden recovery keeps foreground and edge placement");
    window.WindowState=WindowState.Minimized;Check(IsIconic(Handle(window)),"fixture reproduces minimized panel");
    before=GetForegroundWindow();maintain.Invoke(window,null);
    Check(!IsIconic(Handle(window))&&window.WindowState==WindowState.Normal&&IsTopmost(window),"maintenance restores minimized panel to normal topmost");
    Check(GetForegroundWindow()==before,"minimized-panel recovery does not activate foreground");
    Check(Bounds(window)==initial,"minimized-panel recovery preserves edge placement and size");
    // Guard states must leave a deliberately demoted window untouched while the user interacts.
    var pointer=typeof(MainWindow).GetField("pointerDown",Private)!;
    pointer.SetValue(window,true);Demote(window);maintain.Invoke(window,null);
    Check(!IsTopmost(window),"maintenance waits during a pointer gesture");pointer.SetValue(window,false);
    var dragging=dock.GetType().GetField("<IsDragging>k__BackingField",BindingFlags.NonPublic|BindingFlags.Instance)!;
    dragging.SetValue(dock,true);Demote(window);maintain.Invoke(window,null);
    Check(!IsTopmost(window),"maintenance waits during a native drag");dragging.SetValue(dock,false);
    window.ContextMenu!.PlacementTarget=window;window.ContextMenu.IsOpen=true;await Task.Delay(30);Demote(window);maintain.Invoke(window,null);
    Check(!IsTopmost(window),"maintenance waits while the context menu is open");window.ContextMenu.IsOpen=false;
    var shutting=typeof(MainWindow).GetField("shuttingDown",Private)!;
    shutting.SetValue(window,true);Demote(window);maintain.Invoke(window,null);
    Check(!IsTopmost(window),"maintenance never resurrects a closing panel");restore.Invoke(window,null);
    Check(!IsTopmost(window),"repeat-launch restore cannot resurrect a closing panel");shutting.SetValue(window,false);
    initial=Bounds(window);before=GetForegroundWindow();restore.Invoke(window,null);restore.Invoke(window,null);
    Check(IsTopmost(window)&&IsWindowVisible(Handle(window))&&!IsIconic(Handle(window)),"repeat-launch restore is idempotent and visible");
    Check(Bounds(window)==initial&&store.Load()==expectedStyle&&store.LoadDock()==expectedDock,"two repeat-launch restores retain bounds style and lock");
    Check(GetForegroundWindow()==before,"repeat-launch restore does not steal foreground");
    // Explicit restore still respects an intentional non-topmost selection.
    Click(item);window.Hide();restore.Invoke(window,null);
    Check(IsWindowVisible(Handle(window))&&!IsTopmost(window)&&!window.Topmost,"repeat-launch restore respects disabled topmost option");
   } catch(Exception e){Check(false,"unexpected exception: "+e);} finally {
    cover?.Close();if(window!=null){
     await Close(window);
     var finalDock=typeof(MainWindow).GetField("dock",Private)!.GetValue(window)!;var type=finalDock.GetType();
     var hwndField=type.GetField("hwnd",BindingFlags.NonPublic|BindingFlags.Instance);var hookField=type.GetField("foregroundHook",BindingFlags.NonPublic|BindingFlags.Instance);var rootField=type.GetField("callbackRoot",BindingFlags.NonPublic|BindingFlags.Instance);
     Check(hwndField!=null&&hookField!=null&&rootField!=null&&(nint)hwndField.GetValue(finalDock)! == 0&&
      (nint)hookField.GetValue(finalDock)! == 0&&!((GCHandle)rootField.GetValue(finalDock)!).IsAllocated,
      "closing fixture releases native window handle foreground hook and callback root");
    }
    Check(app.Windows.Count==0,"all fixture windows close cleanly");
    app.Shutdown();Dispatcher.ExitAllFrames();
   }
  };
  app.Run(window);Console.WriteLine("ARTIFACTS "+root);Console.WriteLine($"TOPMOST WINDOW {(failures==0?"PASS":"FAIL")} ({checks-failures}/{checks})");return failures==0?0:1;
 }
 private static void Check(bool ok,string name){checks++;Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)failures++;}
 private static void Click(MenuItem item)=>item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
 private static void StopTimers(MainWindow w){((DispatcherTimer)typeof(MainWindow).GetField("clock",Private)!.GetValue(w)!).Stop();((DispatcherTimer)typeof(MainWindow).GetField("collapse",Private)!.GetValue(w)!).Stop();}
 private static async Task Close(MainWindow w){
  var closed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  void Closed(object? sender,EventArgs e)=>closed.TrySetResult();
  w.Closed+=Closed;w.Close();
  if(await Task.WhenAny(closed.Task,Task.Delay(1500))!=closed.Task)Check(false,"fixture native window completes asynchronous close");
  w.Closed-=Closed;
 }
 private static nint Handle(Window w)=>new WindowInteropHelper(w).Handle;
 private static bool IsTopmost(Window w)=>(GetWindowLongPtr(Handle(w),-20).ToInt64()&8)!=0;
 private static void Demote(Window w)=>SetWindowPos(Handle(w),new nint(-2),0,0,0,0,NoMove|NoSize|NoActivate);
 private static (int,int,int,int) Bounds(Window w){GetWindowRect(Handle(w),out var r);return(r.left,r.top,r.right,r.bottom);}
 private static bool IsAbove(nint upper,nint lower){for(nint h=GetWindow(lower,3);h!=0;h=GetWindow(h,3))if(h==upper)return true;return false;}
 [StructLayout(LayoutKind.Sequential)] private struct RECT{public int left,top,right,bottom;}
 [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd,out RECT rect);
 [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd,int index);
 [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd,uint command);
 [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
 [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
 [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
 [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd,int command);
 private sealed class Fixture:IQuotaClient {public Task<QuotaData> FetchAsync(CancellationToken c)=>Task.FromResult(new QuotaData(new(32,300,DateTimeOffset.UtcNow.AddHours(2)),new(58,10080,DateTimeOffset.UtcNow.AddDays(3))));public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}
}
