[CmdletBinding()]
param(
    [string]$DeadlockRoot = "C:\Program Files (x86)\Steam\steamapps\common\Deadlock"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$output = Join-Path $env:TEMP "SkyboxSelector.InPlaceApplySyntheticTest.exe"

& $csc /nologo /optimize+ /target:exe /platform:anycpu `
    /main:InPlaceApplySyntheticTest `
    /reference:System.Drawing.dll `
    /reference:System.Web.Extensions.dll `
    /reference:System.Windows.Forms.dll `
    "/out:$output" `
    (Join-Path $PSScriptRoot "inplace-apply-synthetic-test.cs") `
    (Join-Path $root "source\launcher\SelectorForm.cs")
if ($LASTEXITCODE -ne 0) {
    throw "In-place apply test compilation failed with exit code $LASTEXITCODE"
}

& $output $DeadlockRoot
if ($LASTEXITCODE -ne 0) {
    throw "In-place apply test failed with exit code $LASTEXITCODE"
}
