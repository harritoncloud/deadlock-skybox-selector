[CmdletBinding()]
param(
    [string]$DeadlockRoot = "C:\Program Files (x86)\Steam\steamapps\common\Deadlock"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$output = Join-Path $env:TEMP "SkyboxSelector.UiSyntheticBenchmark.exe"

& $csc /nologo /optimize+ /target:exe /platform:anycpu `
    /main:UiSyntheticBenchmark `
    /reference:System.Drawing.dll `
    /reference:System.Web.Extensions.dll `
    /reference:System.Windows.Forms.dll `
    "/out:$output" `
    (Join-Path $PSScriptRoot "ui-synthetic-benchmark.cs") `
    (Join-Path $root "source\launcher\SelectorForm.cs")
if ($LASTEXITCODE -ne 0) {
    throw "UI synthetic benchmark compilation failed with exit code $LASTEXITCODE"
}

& $output $DeadlockRoot
if ($LASTEXITCODE -ne 0) {
    throw "UI synthetic benchmark failed with exit code $LASTEXITCODE"
}
