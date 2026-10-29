Option Explicit
Dim shell, fso, folder, command
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
folder = fso.GetParentFolderName(WScript.ScriptFullName)
command = """" & fso.BuildPath(folder,"CodexUsageWidgetHost.exe") & """ --disable"
shell.Run command, 0, False
