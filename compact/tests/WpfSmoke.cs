using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexEdgeWidget;
internal static class WpfSmoke {
 [STAThread]static int Main(string[] args) {
  string output=args.Length>0?args[0]:Path.Combine(Path.GetTempPath(),"CodexEdgeWpfSmoke");Directory.CreateDirectory(output);
  int failures=0;var app=new Application();var store=new PreferenceStore(Path.Combine(output,"settings.json"));bool restoreOnly=args.Contains("--restore-only");if(!restoreOnly)store.Save(DisplayStyle.Ring);
  var expectedStyle=store.Load();
  var window=new MainWindow(new Fixture(args.Contains("--dispose-failure")),store,!args.Contains("--production-window"));
  void Click(){window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseDownEvent});window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseUpEvent});}
  void Check(bool value,string name){Console.WriteLine((value?"PASS ":"FAIL ")+name);if(!value)failures++;}
  void Snap(string name){window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth*3,(int)window.ActualHeight*3,288,288,PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name+".png"));encoder.Save(file);}
  window.Loaded+=async (_,_)=>{
   try {
    Console.WriteLine("CHECKPOINT loaded");await Task.Delay(250);
    if(restoreOnly) {
     string mode=expectedStyle.ToString() switch {"Line"=>"横条","Vertical"=>"竖条",_=>"圆环"};
     Check(System.Windows.Automation.AutomationProperties.GetName(window).Contains(mode+"模式"),"new process restores last closed "+mode+" style");Snap("restored-"+expectedStyle);
     if(expectedStyle.ToString()=="Vertical")Check(VerticalPixels(window),"restored vertical bars keep fill colors and a shared bottom baseline");
     return;
    }
    Check(window.IsVisible&&window.ActualWidth==26&&window.ActualHeight==84,"collapsed window is visible and 26x84");Snap("ring-collapsed");
    window.Width=26.25;SendMessage(new WindowInteropHelper(window).Handle,0x7E,0,0);await Task.Delay(100);
    Check(window.ActualWidth<27,"fractional pixel rounding cannot turn collapsed into expanded on display change");
    Console.WriteLine("CHECKPOINT before hover");window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.MouseEnterEvent});
    await Task.Delay(250);Console.WriteLine("CHECKPOINT after hover");
    Check(window.Width==184&&window.ActualWidth==184&&window.ActualHeight==84,"hover expands left to 184x84");Snap("ring-expanded");
    Click();await Task.Delay(100);
    Check(store.Load()==DisplayStyle.Line,"whole-panel click switches to line and persists");Snap("line-expanded");
    Click();await Task.Delay(100);
    Check(store.Load().ToString()=="Vertical","second click selects vertical bars");Snap("vertical-expanded");
    Check(VerticalPixels(window),"vertical mode draws three bottom-aligned fills with correct colors and proportions");
    Click();await Task.Delay(100);
    Check(store.Load()==DisplayStyle.Ring,"third click returns to ring");
    var name=System.Windows.Automation.AutomationProperties.GetName(window);Check(name.Contains("68%")&&name.Contains("42%")&&name.Contains("02:18:"),"display exposes only requested quota and reset values");
    int endIndex=Array.IndexOf(args,"--end-style");if(endIndex>=0&&endIndex+1<args.Length) {
     var endStyle=Enum.Parse<DisplayStyle>(args[endIndex+1],true);
     for(int i=0;i<3&&store.Load()!=endStyle;i++)Click();
     await Task.Delay(50);Check(store.Load()==endStyle,"last selected style before close is "+endStyle);
    }
   }catch(Exception e){failures++;Console.WriteLine("FAIL "+e.GetType().Name+": "+e.Message);}
   finally{window.Close();}
  };
  app.Run(window);Console.WriteLine("WPF RESULT "+(failures==0?"PASS":"FAIL"));return failures==0?0:1;
 }
 [DllImport("user32.dll")]private static extern nint SendMessage(nint h,int message,nint w,nint l);
 private static bool VerticalPixels(Window window) {
  window.UpdateLayout();int width=(int)window.ActualWidth,height=(int)window.ActualHeight;
  var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
  byte[] pixels=new byte[width*height*4];bitmap.CopyPixels(pixels,width*4,0);
  Color[] colors=[Color.FromRgb(96,214,188),Color.FromRgb(229,181,102),Color.FromRgb(117,169,237)];
  int[] lengths=new int[3];
  for(int c=0;c<3;c++) {
   int minX=width,maxX=0,minY=height,maxY=0;
   for(int y=0;y<height;y++)for(int x=Math.Max(0,width-25);x<width;x++) {
    int p=(y*width+x)*4;
    if(Math.Abs(pixels[p]-colors[c].B)<8&&Math.Abs(pixels[p+1]-colors[c].G)<8&&Math.Abs(pixels[p+2]-colors[c].R)<8) {
     minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);
    }
   }
   lengths[c]=maxY-minY+1;
   if(lengths[c]<23||maxX-minX>4||maxY<72||maxY>75)return false;
  }
  return lengths[0]>lengths[1]&&lengths[1]>lengths[2];
 }
 private sealed class Fixture(bool disposeFailure) : IQuotaClient {
  private readonly DateTimeOffset reset=DateTimeOffset.UtcNow.AddSeconds(8316);
  public Task<QuotaData> FetchAsync(CancellationToken cancellationToken)=>Task.FromResult(new QuotaData(new(32,300,reset),new(58,10080,DateTimeOffset.UtcNow.AddDays(3))));
  public ValueTask DisposeAsync()=>disposeFailure?ValueTask.FromException(new IOException("Controlled fixture disposal failure")):ValueTask.CompletedTask;
 }
}
