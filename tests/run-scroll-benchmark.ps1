param([string]$BaselineRef = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root '.test-output'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$references = @('/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Management.dll',
    ('/r:' + (Join-Path $framework 'WPF\UIAutomationClient.dll')),
    ('/r:' + (Join-Path $framework 'WPF\UIAutomationTypes.dll')),
    ('/r:' + (Join-Path $framework 'WPF\WindowsBase.dll')))
function Invoke-Benchmark([string]$SourceDirectory, [string]$Label) {
    $exe = Join-Path $output ('ScrollBenchmark-' + $Label + '.exe')
    $sources = @(Get-ChildItem -LiteralPath $SourceDirectory -Filter '*.cs' -File | ForEach-Object { $_.FullName })
    & (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:anycpu /optimize+ /unsafe /codepage:65001 `
        /main:JsonRightClick.ScrollBenchmark ('/out:' + $exe) ('/win32manifest:' + (Join-Path $root 'app.manifest')) `
        @references @sources (Join-Path $PSScriptRoot 'ScrollBenchmark.cs')
    if ($LASTEXITCODE -ne 0) { throw "Could not compile benchmark: $Label" }
    Write-Output "=== $Label ==="
    & $exe | Tee-Object -FilePath (Join-Path $output ('scroll-benchmark-' + $Label + '.txt'))
    if ($LASTEXITCODE -ne 0) { throw "Benchmark failed: $Label" }
}
if ($BaselineRef) {
    $commit = (& git -C $root rev-parse --verify ($BaselineRef + '^{commit}')).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[a-f0-9]{40}$') { throw 'BaselineRef must resolve to a local commit.' }
    $baseline = Join-Path $output ('scroll-baseline-' + $commit.Substring(0, 8))
    New-Item -ItemType Directory -Path $baseline -Force | Out-Null
    $files = @(& git -C $root ls-tree -r --name-only $commit | Where-Object { $_ -notmatch '/' -and $_ -match '\.cs$' })
    foreach ($file in $files) {
        $text = (& git -C $root show ($commit + ':' + $file)) -join "`r`n"
        if ($LASTEXITCODE -ne 0) { throw "Could not read baseline file: $file" }
        [IO.File]::WriteAllText((Join-Path $baseline $file), $text, [Text.UTF8Encoding]::new($true))
    }
    Invoke-Benchmark $baseline 'before'
}
Invoke-Benchmark $root 'after'
