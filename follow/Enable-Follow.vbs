Option Explicit
Dim shell, fso, folder, command
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
folder = fso.GetParentFolderName(WScript.ScriptFullName)
command = """" & fso.BuildPath(folder,"CodexUsageWidgetHost.exe") & """ --enable"
shell.Run command, 0, False
