using CodexEdgeWidget;
if(args.Contains("app-server")) return await ClientIntegrationTests.RunFixtureAsync(args);
if(args.Contains("--live")) {
 await using var client=new QuotaClient();var result=await client.FetchAsync(CancellationToken.None);
 var view=QuotaDisplay.Calculate(result,DateTimeOffset.UtcNow);
 Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(view));
 return view.FiveHourRemaining is not null&&view.WeeklyRemaining is not null?0:1;
}
int failures=0, count=0;
void Check(string name, Action action) { count++; try { action(); Console.WriteLine("PASS " + name); } catch(Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
void Eq<T>(T expected,T actual) { if(!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception($"Expected {expected}, got {actual}"); }
var now=DateTimeOffset.Parse("2026-10-29T08:00:00Z");
var data=new QuotaData(new(58,10080,now.AddDays(3)),new(32,300,now.AddSeconds(8316)));
Check("quota windows selected by duration even when slots reversed",()=>{ var v=QuotaDisplay.Calculate(data,now); Eq(.68,v.FiveHourRemaining); Eq(.42,v.WeeklyRemaining); });
Check("reset countdown and fraction use 5-hour window",()=>{var v=QuotaDisplay.Calculate(data,now);Eq("02:18:36",v.ResetText);if(Math.Abs(v.ResetFraction!.Value-8316.0/18000)>1e-9)throw new Exception("Wrong fraction");});
Check("countdown ticks locally without another fetch",()=>Eq("02:18:35",QuotaDisplay.Calculate(data,now.AddSeconds(1)).ResetText));
Check("weekly primary alone does not invent five-hour balance",()=>Eq<double?>(null,QuotaDisplay.Calculate(new(data.Primary,null),now).FiveHourRemaining));
Check("unknown data never shown as full balance",()=>{ var v=QuotaDisplay.Calculate(null,now);Eq<double?>(null,v.FiveHourRemaining);Eq("—",v.ResetText);Eq<double?>(null,v.WeeklyRemaining);});
Check("expired quota is unknown until server refresh",()=>{var v=QuotaDisplay.Calculate(new(new(80,300,now.AddSeconds(-1)),null),now);Eq<double?>(null,v.FiveHourRemaining);Eq("00:00:00",v.ResetText);Eq(0.0,v.ResetFraction);});
Check("invalid and nonfinite quota is unknown",()=>Eq<double?>(null,QuotaDisplay.Calculate(new(new(double.NaN,300,null),null),now).FiveHourRemaining));
Check("valid zero and full remaining preserved",()=>{Eq(0.0,QuotaDisplay.Calculate(new(new(100,300,null),null),now).FiveHourRemaining);Eq(1.0,QuotaDisplay.Calculate(new(new(0,300,null),null),now).FiveHourRemaining);});
Check("click cycles ring line vertical ring",()=>{
 Eq(DisplayStyle.Line,QuotaDisplay.Toggle(DisplayStyle.Ring));
 var vertical=Enum.Parse<DisplayStyle>("Vertical");Eq(vertical,QuotaDisplay.Toggle(DisplayStyle.Line));
 Eq(DisplayStyle.Ring,QuotaDisplay.Toggle(vertical));
});
var temp=Path.Combine(Path.GetTempPath(),"CodexEdgeTests-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
try {
 Check("style save writes default topmost for legacy settings",()=>{
  var path=Path.Combine(temp,"topmost-legacy-save.json");File.WriteAllText(path,"{\"style\":\"line\"}");
  Eq(true,new PreferenceStore(path).Save(DisplayStyle.Vertical));
  using var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));Eq(true,json.RootElement.GetProperty("topmost").GetBoolean());
 });
 Check("missing corrupt legacy and nonboolean topmost default enabled",()=>{
  var path=Path.Combine(temp,"topmost-defaults.json");Eq(true,new PreferenceStore(path).LoadTopmost());
  foreach(var content in new[]{"broken","[]","null","123","{}","{\"style\":\"line\"}","{\"topmost\":null}","{\"topmost\":1}","{\"topmost\":\"false\"}"}) {
   File.WriteAllText(path,content);Eq(true,new PreferenceStore(path).LoadTopmost());
  }
 });
 Check("topmost choice persists across new store and preserves style dock lock",()=>{
  var path=Path.Combine(temp,"topmost-roundtrip.json");var store=new PreferenceStore(path);
  Eq(true,store.Save(DisplayStyle.Vertical));Eq(true,store.SaveDock(new(new(DockEdge.Bottom,.23,"display-2"),true)));
  foreach(var enabled in new[]{false,true,false}) {
   Eq(true,store.SaveTopmost(enabled));var reopened=new PreferenceStore(path);Eq(enabled,reopened.LoadTopmost());
   Eq(DisplayStyle.Vertical,reopened.Load());Eq(new DockPreferences(new(DockEdge.Bottom,.23,"display-2"),true),reopened.LoadDock());
   using var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));Eq(enabled,json.RootElement.GetProperty("topmost").GetBoolean());
  }
 });
 Check("style changes and close saves preserve disabled topmost",()=>{
  var path=Path.Combine(temp,"topmost-style-save.json");var store=new PreferenceStore(path);Eq(true,store.SaveTopmost(false));
  foreach(var style in Enum.GetValues<DisplayStyle>()){Eq(true,store.Save(style));Eq(false,new PreferenceStore(path).LoadTopmost());}
 });
 Check("dock moves and lock changes preserve disabled topmost",()=>{
  var path=Path.Combine(temp,"topmost-dock-save.json");var store=new PreferenceStore(path);Eq(true,store.SaveTopmost(false));
  foreach(var edge in Enum.GetValues<DockEdge>())foreach(var locked in new[]{false,true}) {
   Eq(true,store.SaveDock(new(new(edge,.71,"display-1"),locked)));Eq(false,new PreferenceStore(path).LoadTopmost());
  }
 });
 Check("failed topmost write returns false and leaves no temporary files",()=>{
  var dir=Path.Combine(temp,"topmost-write-failure");Directory.CreateDirectory(dir);var blocker=Path.Combine(dir,"file");File.WriteAllText(blocker,"x");
  Eq(false,new PreferenceStore(Path.Combine(blocker,"settings.json")).SaveTopmost(false));Eq(0,Directory.GetFiles(dir,"*.tmp").Length);
 });
 Check("migration keeps selected legacy topmost without overwriting an existing target",()=>{
  var dir=Path.Combine(temp,"topmost-migration");Directory.CreateDirectory(dir);var source=Path.Combine(dir,"source.json");
  File.WriteAllText(source,"{\"style\":\"vertical\",\"topmost\":false}");var target=Path.Combine(dir,"target.json");var store=new PreferenceStore(target);
  Eq(true,store.MigrateLegacy(new[]{source}));Eq(false,store.LoadTopmost());Eq(DisplayStyle.Vertical,store.Load());
  Eq(true,store.SaveTopmost(true));Eq(false,store.MigrateLegacy(new[]{source}));Eq(true,store.LoadTopmost());
 });
 foreach(var name in new[]{"Ring","Line","Vertical"}) {
  Check(name+" style persists across new store at restart",()=>{
   var style=Enum.Parse<DisplayStyle>(name);var path=Path.Combine(temp,name+"-settings.json");
   if(!new PreferenceStore(path).Save(style))throw new Exception("Save failed");
   Eq(style,new PreferenceStore(path).Load());
   using var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
   Eq(name.ToLowerInvariant(),json.RootElement.GetProperty("style").GetString());
  });
 }
 Check("last selected style is restored after every close and reopen",()=>{
  var path=Path.Combine(temp,"lifecycle-settings.json");var initial=new PreferenceStore(path).Load();Eq(DisplayStyle.Ring,initial);
  foreach(var name in new[]{"Line","Vertical","Ring","Line","Vertical"}) {
   var selected=QuotaDisplay.Toggle(new PreferenceStore(path).Load());Eq(Enum.Parse<DisplayStyle>(name),selected);
   if(!new PreferenceStore(path).Save(selected))throw new Exception("Close save failed");
   Eq(selected,new PreferenceStore(path).Load());
  }
 });
 Check("old ring and line settings remain compatible",()=>{
  var path=Path.Combine(temp,"legacy-settings.json");
  foreach(var name in new[]{"ring","line"}){File.WriteAllText(path,"{\"style\":\""+name+"\"}");Eq(name=="line"?DisplayStyle.Line:DisplayStyle.Ring,new PreferenceStore(path).Load());}
 });
 Check("unknown styles and invalid enum values use ring safely",()=>{
  var path=Path.Combine(temp,"unknown-settings.json");
  foreach(var value in new[]{"{\"style\":\"other\"}","{\"style\":null}","{}"}){File.WriteAllText(path,value);Eq(DisplayStyle.Ring,new PreferenceStore(path).Load());}
  if(!new PreferenceStore(path).Save((DisplayStyle)99))throw new Exception("Fallback save failed");Eq(DisplayStyle.Ring,new PreferenceStore(path).Load());
 });
 Check("missing and corrupt settings default to ring",()=>{var path=Path.Combine(temp,"bad.json");Eq(DisplayStyle.Ring,new PreferenceStore(path).Load());File.WriteAllText(path,"not-json");Eq(DisplayStyle.Ring,new PreferenceStore(path).Load());File.WriteAllText(path,"{\"style\":999}");Eq(DisplayStyle.Ring,new PreferenceStore(path).Load());});
 Check("failed settings write cannot crash click",()=>{var path=Path.Combine(temp,"file");File.WriteAllText(path,"x");Eq(false,new PreferenceStore(Path.Combine(path,"settings.json")).Save(DisplayStyle.Line));});
 Check("valid nonobject JSON settings cannot crash startup",()=>{var path=Path.Combine(temp,"nonobject.json");foreach(var value in new[]{"[]","null","123"}){File.WriteAllText(path,value);Eq(DisplayStyle.Ring,new PreferenceStore(path).Load());}});
 foreach(var style in Enum.GetValues<DisplayStyle>()) {
  Check("legacy "+style+" style migrates once to shared settings",()=>{
   var source=Path.Combine(temp,"migrate-source-"+style+".json");var target=Path.Combine(temp,"migrated-"+style,"edge-settings.json");
   new PreferenceStore(source).Save(style);var original=File.ReadAllText(source);
   Eq(true,new PreferenceStore(target).MigrateLegacy(new[]{source}));Eq(style,new PreferenceStore(target).Load());Eq(true,new PreferenceStore(target).LoadTopmost());Eq(original,File.ReadAllText(source));
   Eq(false,new PreferenceStore(target).MigrateLegacy(new[]{source}));
  });
 }
 Check("migration chooses newest valid legacy file and skips newer damage",()=>{
  var dir=Path.Combine(temp,"migration-newest");Directory.CreateDirectory(dir);
  var old=Path.Combine(dir,"old.json");var recent=Path.Combine(dir,"recent.json");var corrupt=Path.Combine(dir,"corrupt.json");var unknown=Path.Combine(dir,"unknown.json");
  new PreferenceStore(old).Save(DisplayStyle.Ring);new PreferenceStore(recent).Save(DisplayStyle.Vertical);File.WriteAllText(corrupt,"broken");File.WriteAllText(unknown,"{\"style\":\"future-mode\"}");
  var time=DateTime.UtcNow.AddHours(-1);File.SetLastWriteTimeUtc(old,time);File.SetLastWriteTimeUtc(recent,time.AddMinutes(1));File.SetLastWriteTimeUtc(corrupt,time.AddMinutes(2));File.SetLastWriteTimeUtc(unknown,time.AddMinutes(3));
  var store=new PreferenceStore(Path.Combine(dir,"target.json"));Eq(true,store.MigrateLegacy(new[]{recent,corrupt,old,unknown}));Eq(DisplayStyle.Vertical,store.Load());
 });
 Check("migration never overwrites an existing target even when target is corrupt",()=>{
  var source=Path.Combine(temp,"migration-never-source.json");new PreferenceStore(source).Save(DisplayStyle.Vertical);
  foreach(var content in new[]{"{\"style\":\"line\"}","broken-target"}) {
   var target=Path.Combine(temp,"migration-never-target.json");File.WriteAllText(target,content);
   Eq(false,new PreferenceStore(target).MigrateLegacy(new[]{source}));Eq(content,File.ReadAllText(target));
  }
 });
 Check("migration ignores missing invalid and nonobject candidate settings",()=>{
  var dir=Path.Combine(temp,"migration-invalid");Directory.CreateDirectory(dir);var paths=new List<string>{Path.Combine(dir,"missing.json")};
  foreach(var content in new[]{"broken","[]","null","123","{}","{\"style\":null}","{\"style\":999}","{\"style\":\"Ring\"}"}){var candidate=Path.Combine(dir,paths.Count+".json");File.WriteAllText(candidate,content);paths.Add(candidate);}
  var target=Path.Combine(dir,"target.json");Eq(false,new PreferenceStore(target).MigrateLegacy(paths));Eq(false,File.Exists(target));
 });
 Check("migration reads only explicitly provided old settings paths",()=>{
  var dir=Path.Combine(temp,"migration-explicit");Directory.CreateDirectory(dir);
  var listed=Path.Combine(dir,"listed.json");var unlisted=Path.Combine(dir,"unlisted.json");new PreferenceStore(listed).Save(DisplayStyle.Line);new PreferenceStore(unlisted).Save(DisplayStyle.Vertical);
  File.SetLastWriteTimeUtc(listed,DateTime.UtcNow.AddHours(-1));
  var store=new PreferenceStore(Path.Combine(dir,"target.json"));Eq(true,store.MigrateLegacy(new[]{listed}));Eq(DisplayStyle.Line,store.Load());
 });
 Check("migration failure preserves source and leaves no temporary settings",()=>{
  var dir=Path.Combine(temp,"migration-failure");Directory.CreateDirectory(dir);var source=Path.Combine(dir,"source.json");new PreferenceStore(source).Save(DisplayStyle.Vertical);var original=File.ReadAllText(source);
  var target=Path.Combine(dir,"target-directory");Directory.CreateDirectory(target);
  Eq(false,new PreferenceStore(target).MigrateLegacy(new[]{source}));Eq(original,File.ReadAllText(source));Eq(0,Directory.GetFiles(dir,"*.tmp").Length);
 });
} finally {Directory.Delete(temp,true);}
Check("hover expands left while right edge and height stay fixed",()=>{var a=DockLayout.Calculate(0,0,1920,1040,1,false);var b=DockLayout.Calculate(0,0,1920,1040,1,true);Eq(26,a.Width);Eq(184,b.Width);Eq(84,b.Height);Eq(a.X+a.Width,b.X+b.Width);Eq(a.Y,b.Y);});
Check("DPI and negative monitor coordinates anchor correctly",()=>{var b=DockLayout.Calculate(-2560,-100,0,1340,1.5,true);Eq(276,b.Width);Eq(126,b.Height);Eq(0,b.X+b.Width);Eq(557,b.Y);});
ClientContractTests.Run(Check);
ClientIntegrationTests.Run(Check);
DockingTests.Run(Check);
Console.WriteLine($"RESULT {count-failures}/{count} passed");
return failures==0?0:1;
