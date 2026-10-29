using System.IO;
using System.Windows;
namespace CodexEdgeWidget;
internal static class App {
 [STAThread]public static void Main(string[] args) {
  bool demo=args.Contains("--demo"),preview=args.Contains("--preview");
  string suffix=demo||preview?"-Preview":"";
  using var mutex=new Mutex(true,"Local\\CodexUsageWidget-87D45B32-BCF8-44E5-8F9B-36E9BB488F4F"+suffix,out bool created);
  if(!created){WindowRecovery.NotifyExisting(demo||preview);return;}
  string settings=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex-usage-widget",demo||preview?"preview-settings.json":"edge-settings.json");
  int index=Array.IndexOf(args,"--settings-path");if(index>=0&&index+1<args.Length)settings=args[index+1];
  var preferences=new PreferenceStore(settings);
  if(!demo&&!preview&&index<0)preferences.MigrateLegacy(LegacySettingsPaths());
  var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};
  app.Run(new MainWindow(demo?new DemoQuotaClient():new QuotaClient(),preferences,demo,preview));
 }
 private static IEnumerable<string> LegacySettingsPaths() {
  var local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
  var paths=new List<string>{Path.Combine(local,"CodexUsageWidget","edge-settings.json")};
  try {
   string packages=Path.Combine(local,"Packages");
   if(Directory.Exists(packages))foreach(var package in Directory.EnumerateDirectories(packages,"OpenAI.Codex_*"))
    paths.Add(Path.Combine(package,"LocalCache","Local","CodexUsageWidget","edge-settings.json"));
  } catch(IOException){}catch(UnauthorizedAccessException){}
  return paths;
 }
 private sealed class DemoQuotaClient : IQuotaClient {
  private readonly DateTimeOffset reset=DateTimeOffset.UtcNow.AddSeconds(8316);
  public Task<QuotaData> FetchAsync(CancellationToken token)=>Task.FromResult(new QuotaData(new(32,300,reset),new(58,10080,DateTimeOffset.UtcNow.AddDays(3))));
  public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
 }
}
