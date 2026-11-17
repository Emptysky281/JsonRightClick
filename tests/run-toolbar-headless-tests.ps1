$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$output = Join-Path $root '.test-output'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$exe = Join-Path $output 'ToolbarHeadlessTests.exe'
# Only the layout control and its tests are needed. No application/tray/IPC startup is compiled in.
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:anycpu /optimize+ /langversion:5 /codepage:65001 `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll ('/out:' + $exe) `
    (Join-Path $root 'WrappingToolStrip.cs') (Join-Path $PSScriptRoot 'ToolbarHeadlessTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Headless toolbar compilation failed.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Headless toolbar layout tests failed.' }
