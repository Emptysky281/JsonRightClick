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
$exe = Join-Path $output 'CompactArrayHeadlessTests.exe'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:anycpu /optimize+ /unsafe /langversion:5 /codepage:65001 `
    /main:JsonRightClick.CompactArrayHeadlessTests ('/out:' + $exe) `
    @references @sources (Join-Path $PSScriptRoot 'CompactArrayHeadlessTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Headless array test compilation failed.' }
# Runs only the pure formatter tests: never Program.Main or the interactive editor suite.
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Headless array tests failed.' }
