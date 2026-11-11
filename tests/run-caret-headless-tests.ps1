param([switch]$Preview)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$output = Join-Path $root '.test-output'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$references = @(
    '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Management.dll', '/r:Accessibility.dll',
    ('/r:' + (Join-Path $framework 'WPF\UIAutomationClient.dll')),
    ('/r:' + (Join-Path $framework 'WPF\UIAutomationTypes.dll')),
    ('/r:' + (Join-Path $framework 'WPF\WindowsBase.dll'))
)
$sources = @(Get-ChildItem -LiteralPath $root -Filter '*.cs' -File | ForEach-Object { $_.FullName })
$exe = Join-Path $output 'CaretTrailHeadlessTests.exe'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:anycpu /optimize+ /unsafe /codepage:65001 `
    /main:JsonRightClick.CaretTrailHeadlessTests ('/out:' + $exe) `
    @references @sources (Join-Path $PSScriptRoot 'CaretTrailHeadlessTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Headless caret test compilation failed.' }
# Only the dedicated pure-logic/bitmap Main above is executed, never App.Main or UI tests.
if ($Preview) { & $exe (Join-Path $output 'purple-caret-preview') } else { & $exe }
if ($LASTEXITCODE -ne 0) { throw 'Headless caret tests failed.' }
