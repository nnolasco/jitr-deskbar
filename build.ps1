# Builds bin\jitr-deskbar.exe with the in-box .NET Framework 4.8 C# compiler.
# No SDK, no packages -- works on any Windows 11 machine as-is.
$ErrorActionPreference = "Stop"
$fw = "$env:windir\Microsoft.NET\Framework64\v4.0.30319"
$out = Join-Path $PSScriptRoot "bin"
New-Item -ItemType Directory -Force $out | Out-Null

$sources = Get-ChildItem (Join-Path $PSScriptRoot "src\*.cs") | ForEach-Object { $_.FullName }
$exe = Join-Path $out "jitr-deskbar.exe"
$icon = Join-Path $PSScriptRoot "app.ico"   # regenerate with make-icon.py

# /codepage:65001 -- sources are UTF-8 (Sessions.cs contains a literal "✳")
& "$fw\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 `
    "/win32icon:$icon" `
    "/lib:$fw\WPF" `
    /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll `
    /r:System.Xaml.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:Microsoft.CSharp.dll `
    "/out:$exe" `
    $sources

if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED"; exit 1 }
Write-Host "OK -> $(Join-Path $out 'jitr-deskbar.exe')"
