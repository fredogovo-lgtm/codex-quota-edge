using System.IO;
using System.Text.Json;
namespace CodexEdgeWidget;
public interface IQuotaClient : IAsyncDisposable { Task<QuotaData> FetchAsync(CancellationToken cancellationToken); }
public record QuotaData(QuotaPeriod? Primary, QuotaPeriod? Secondary);
public record QuotaPeriod(double UsedPercent, int WindowMinutes, DateTimeOffset? ResetsAt);
public enum DisplayStyle { Ring, Line, Vertical }
public record QuotaView(double? FiveHourRemaining, double? ResetFraction, string ResetText, double? WeeklyRemaining);
public static class QuotaDisplay {
 public static QuotaView Calculate(QuotaData? data, DateTimeOffset now) {
  var periods=new[]{data?.Primary,data?.Secondary};
  var five=periods.FirstOrDefault(p=>p?.WindowMinutes==300);
  var week=periods.FirstOrDefault(p=>p?.WindowMinutes==10080);
  double? Remaining(QuotaPeriod? p) => p is null || !double.IsFinite(p.UsedPercent) || p.ResetsAt<=now ? null : (100-Math.Clamp(p.UsedPercent,0,100))/100;
  var seconds=five?.ResetsAt is {} reset ? (double?)Math.Max(0,Math.Ceiling((reset-now).TotalSeconds)) : null;
  string text=seconds is {} s ? $"{(int)(s/3600):00}:{(int)(s/60)%60:00}:{(int)s%60:00}" : "—";
  return new(Remaining(five),seconds is {} n ? Math.Clamp(n/18000,0,1) : null,text,Remaining(week));
 }
 public static DisplayStyle Toggle(DisplayStyle value) => value switch { DisplayStyle.Ring=>DisplayStyle.Line, DisplayStyle.Line=>DisplayStyle.Vertical, _=>DisplayStyle.Ring };
}
public sealed class PreferenceStore(string path) {
 public DisplayStyle Load() => TryReadStyle(path,out var style) ? style : DisplayStyle.Ring;
 public bool LoadTopmost()=>ReadTopmost(path);
 private static bool ReadTopmost(string source) {
  try {
   using var json=JsonDocument.Parse(File.ReadAllText(source));var root=json.RootElement;
   if(root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("topmost",out var value)&&value.ValueKind is JsonValueKind.True or JsonValueKind.False)return value.GetBoolean();
  }catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException){}
  return true;
 }
 public DockPreferences LoadDock() {
  try {
   using var json=JsonDocument.Parse(File.ReadAllText(path));var root=json.RootElement;
   if(root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("dock",out var d)&&d.ValueKind==JsonValueKind.Object&&
    d.TryGetProperty("edge",out var edge)&&edge.ValueKind==JsonValueKind.String&&Enum.TryParse<DockEdge>(edge.GetString(),true,out var e)&&Enum.IsDefined(e)&&
    d.TryGetProperty("fraction",out var f)&&f.TryGetDouble(out var fraction)&&double.IsFinite(fraction)&&
    d.TryGetProperty("locked",out var l)&&l.ValueKind is JsonValueKind.True or JsonValueKind.False) {
     string? monitor=d.TryGetProperty("monitor",out var m)&&m.ValueKind==JsonValueKind.String?m.GetString():null;
     return new(DockGeometry.Normalize(new(e,fraction,monitor)),l.GetBoolean());
   }
  }catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException or InvalidOperationException){}
  return new(new(DockEdge.Right,.5),false);
 }
 public bool MigrateLegacy(IEnumerable<string> paths) {
  if(File.Exists(path)||Directory.Exists(path))return false;
  DisplayStyle? selected=null;bool selectedTopmost=true;DateTime latest=DateTime.MinValue;
  foreach(var candidate in paths) {
   if(!TryReadStyle(candidate,out var style))continue;
   try {
    var written=File.GetLastWriteTimeUtc(candidate);
    if(selected is null||written>latest){latest=written;selected=style;selectedTopmost=ReadTopmost(candidate);}
   } catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
  }
  return selected is {} value && Write(value,LoadDock(),selectedTopmost,false);
 }
 private static bool TryReadStyle(string source,out DisplayStyle style) {
  style=DisplayStyle.Ring;
  try {
   using var json=JsonDocument.Parse(File.ReadAllText(source));
   if(json.RootElement.ValueKind!=JsonValueKind.Object||!json.RootElement.TryGetProperty("style",out var value)||value.ValueKind!=JsonValueKind.String)return false;
   switch(value.GetString()) {
    case "ring":return true;
    case "line":style=DisplayStyle.Line;return true;
    case "vertical":style=DisplayStyle.Vertical;return true;
    default:return false;
   }
  } catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException) {return false;}
 }
 public bool Save(DisplayStyle style) => Save(style,true);
 public bool SaveDock(DockPreferences dock)=>Write(Load(),new(DockGeometry.Normalize(dock.Position),dock.Locked),LoadTopmost(),true);
 public bool SaveTopmost(bool topmost)=>Write(Load(),LoadDock(),topmost,true);
 private bool Save(DisplayStyle style,bool overwrite)=>Write(style,LoadDock(),LoadTopmost(),overwrite);
 private bool Write(DisplayStyle style,DockPreferences dock,bool topmost,bool overwrite) {
  string? temp=null;
  try { var full=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(full)!);
   temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
   File.WriteAllText(temp,JsonSerializer.Serialize(new{style=style switch { DisplayStyle.Line=>"line", DisplayStyle.Vertical=>"vertical", _=>"ring" },dock=new{edge=dock.Position.Edge.ToString().ToLowerInvariant(),fraction=dock.Position.Fraction,monitor=dock.Position.Monitor,locked=dock.Locked},topmost}));
   File.Move(temp,full,overwrite); return true;
  } catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {return false;}
  finally {try{if(temp!=null&&File.Exists(temp))File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){}}
 }
}
public record DockBounds(int X,int Y,int Width,int Height);
public static class DockLayout {
 public static DockBounds Calculate(int left,int top,int right,int bottom,double scale,bool expanded) {
  int w=(int)Math.Round((expanded?184:26)*scale),h=(int)Math.Round(84*scale);
  return new(right-w,top+Math.Max(0,((bottom-top)-h)/2),w,h);
 }
}
