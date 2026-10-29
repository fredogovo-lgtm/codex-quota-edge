using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
namespace CodexEdgeWidget;
internal static class WindowRecovery {
  private static readonly int ProductionMessage=(int)RegisterWindowMessage("CodexQuotaEdge.RestorePanel.87D45B32-BCF8-44E5-8F9B-36E9BB488F4F");
 private static readonly int PreviewMessage=(int)RegisterWindowMessage("CodexQuotaEdge.RestorePanel.Preview.87D45B32-BCF8-44E5-8F9B-36E9BB488F4F");
 public static int GetRestoreMessage(bool preview)=>preview?PreviewMessage:ProductionMessage;
 public static void NotifyExisting(bool preview) {
  int message=GetRestoreMessage(preview);if(message==0)return;
  using var current=Process.GetCurrentProcess();int session=current.SessionId;
  EnumWindows((window,_)=> {
   GetWindowThreadProcessId(window,out uint pid);
   if(pid==Environment.ProcessId)return true;
   var title=new StringBuilder(100);GetWindowText(window,title,title.Capacity);
   if(title.ToString()!="Codex 用量悬浮窗")return true;
   try {using var process=Process.GetProcessById((int)pid);if(process.ProcessName!="CodexUsageWidget"||process.SessionId!=session)return true;}
   catch(ArgumentException){return true;}catch(InvalidOperationException){return true;}catch(System.ComponentModel.Win32Exception){return true;}
   PostMessage(window,message,0,0);return true;
  },0);
 }
 private delegate bool EnumProc(nint window,nint parameter);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern uint RegisterWindowMessage(string name);
 [DllImport("user32.dll")]private static extern bool EnumWindows(EnumProc callback,nint parameter);
 [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint window,out uint process);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetWindowText(nint window,StringBuilder text,int length);
 [DllImport("user32.dll")]private static extern bool PostMessage(nint window,int message,nint wParam,nint lParam);
}