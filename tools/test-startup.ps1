[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$output = Join-Path $env:TEMP "SkyboxSelector.StartupSyntheticTest.exe"

& $csc /nologo /optimize+ /target:exe /platform:anycpu `
    /main:StartupSyntheticTest `
    /reference:System.Drawing.dll `
    /reference:System.Web.Extensions.dll `
    /reference:System.Windows.Forms.dll `
    "/out:$output" `
    (Join-Path $PSScriptRoot "startup-synthetic-test.cs") `
    (Join-Path $root "source\launcher\SelectorForm.cs")
if ($LASTEXITCODE -ne 0) {
    throw "Startup synthetic test compilation failed with exit code $LASTEXITCODE"
}

& $output
if ($LASTEXITCODE -ne 0) {
    throw "Startup synthetic test failed with exit code $LASTEXITCODE"
}
