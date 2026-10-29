[CmdletBinding()]
param([string]$OutputDirectory='')
$ErrorActionPreference='Stop'
if(-not$OutputDirectory){$OutputDirectory=Join-Path (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))) 'work\follow-host-v2.2'}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not(Test-Path -LiteralPath $compiler -PathType Leaf)){throw 'The Windows .NET Framework C# compiler is unavailable.'}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output=Join-Path $OutputDirectory 'CodexUsageWidgetHost.exe'
& $compiler /nologo /target:winexe /optimize+ /debug- /platform:anycpu /reference:System.Windows.Forms.dll ('/out:'+$output) (Join-Path $PSScriptRoot 'QuietHost.cs')
if($LASTEXITCODE -ne 0){throw 'Quiet host compilation failed.'}
Get-Item -LiteralPath $output | Select-Object FullName,Length
