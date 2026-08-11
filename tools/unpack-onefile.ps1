[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Executable,

    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path $PSScriptRoot -Parent
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repositoryRoot "unpacked"
}

$Executable = [IO.Path]::GetFullPath($Executable)
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) {
    throw "Executable not found: $Executable"
}
if (Test-Path -LiteralPath $OutputRoot) {
    if (@(Get-ChildItem -LiteralPath $OutputRoot -Force).Count -ne 0) {
        throw "Output directory is not empty: $OutputRoot"
    }
} else {
    New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
}

$runtimeRoot = Join-Path $OutputRoot "runtime"
$assetRoot = Join-Path $OutputRoot "assets"
$configRoot = Join-Path $OutputRoot "config"
New-Item -ItemType Directory -Force -Path $runtimeRoot, $assetRoot, $configRoot | Out-Null

$resourceFiles = [ordered]@{
    "SkyboxSelector.Payload.SkyboxSelector.cmd" = "SkyboxSelector.cmd"
    "SkyboxSelector.Payload.select-skybox.ps1" = "select-skybox.ps1"
    "SkyboxSelector.Payload.DeadlockGameInfoInstaller.exe" = "DeadlockGameInfoInstaller.exe"
    "SkyboxSelector.Payload.7z.exe" = "7z.exe"
    "SkyboxSelector.Payload.7z.dll" = "7z.dll"
    "SkyboxSelector.Payload.7zip-License.txt" = "7zip-License.txt"
    "SkyboxSelector.Payload.assets.sha256" = "assets.sha256"
    "SkyboxSelector.Payload.runtime-checksums.sha256" = "runtime-checksums.sha256"
}
$assetResource = "SkyboxSelector.Payload.skyboxes.7z"
$assembly = [Reflection.Assembly]::LoadFile($Executable)
$actualResources = @($assembly.GetManifestResourceNames() | Sort-Object)
$expectedResources = @(@($resourceFiles.Keys) + $assetResource | Sort-Object)
if (($actualResources -join "`n") -ne ($expectedResources -join "`n")) {
    throw "The executable resource layout does not match this source tree."
}

$resourceReport = @()
foreach ($entry in $resourceFiles.GetEnumerator()) {
    $stream = $assembly.GetManifestResourceStream($entry.Key)
    if (-not $stream) {
        throw "Embedded resource is missing: $($entry.Key)"
    }

    $targetPath = Join-Path $runtimeRoot $entry.Value
    $targetStream = [IO.File]::Create($targetPath)
    try {
        $stream.CopyTo($targetStream)
    } finally {
        $targetStream.Dispose()
        $stream.Dispose()
    }

    $resourceReport += [ordered]@{
        resource = $entry.Key
        extractedTo = "runtime/$($entry.Value)"
        bytes = (Get-Item -LiteralPath $targetPath).Length
        sha256 = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$archivePath = Join-Path $OutputRoot ".skyboxes.unpacking.7z"
$assetStream = $assembly.GetManifestResourceStream($assetResource)
if (-not $assetStream) {
    throw "Embedded resource is missing: $assetResource"
}
$archiveStream = [IO.File]::Create($archivePath)
try {
    $assetStream.CopyTo($archiveStream)
} finally {
    $archiveStream.Dispose()
    $assetStream.Dispose()
}

$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$expectedArchiveHash = ((Get-Content -Raw -LiteralPath (Join-Path $runtimeRoot "assets.sha256")) -split '\s+')[0].ToLowerInvariant()
if ($archiveHash -ne $expectedArchiveHash) {
    throw "Embedded asset archive hash mismatch."
}
$resourceReport += [ordered]@{
    resource = $assetResource
    extractedTo = "assets/"
    bytes = (Get-Item -LiteralPath $archivePath).Length
    sha256 = $archiveHash
}

$sevenZip = Join-Path $runtimeRoot "7z.exe"
$testOutput = @(& $sevenZip t -- $archivePath 2>&1)
if ($LASTEXITCODE -ne 0 -or -not ($testOutput -match "Everything is Ok")) {
    throw "Embedded asset archive validation failed."
}
& $sevenZip x -y "-o$assetRoot" -- $archivePath | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Embedded asset archive extraction failed with exit code $LASTEXITCODE."
}
Remove-Item -LiteralPath $archivePath -Force

$installerPath = Join-Path $runtimeRoot "DeadlockGameInfoInstaller.exe"
$installerAssembly = [Reflection.Assembly]::LoadFile($installerPath)
$configStream = $installerAssembly.GetManifestResourceStream("DeadlockGameInfoInstaller.gameinfo.gi")
if (-not $configStream) {
    throw "Embedded GameInfo resource is missing."
}
$configPath = Join-Path $configRoot "gameinfo.gi"
$configTarget = [IO.File]::Create($configPath)
try {
    $configStream.CopyTo($configTarget)
} finally {
    $configTarget.Dispose()
    $configStream.Dispose()
}

$vpkFiles = @(Get-ChildItem -LiteralPath $assetRoot -Recurse -File -Filter "*.vpk")
if ($vpkFiles.Count -ne 32) {
    throw "Expected 32 VPK files, extracted $($vpkFiles.Count)."
}

$report = [ordered]@{
    extractedAtUtc = [DateTime]::UtcNow.ToString("o")
    sourceExecutable = [ordered]@{
        path = [IO.Path]::GetFileName($Executable)
        bytes = (Get-Item -LiteralPath $Executable).Length
        sha256 = (Get-FileHash -LiteralPath $Executable -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    resources = $resourceReport
    extracted = [ordered]@{
        files = @(Get-ChildItem -LiteralPath $OutputRoot -Recurse -File).Count
        bytes = (Get-ChildItem -LiteralPath $OutputRoot -Recurse -File | Measure-Object Length -Sum).Sum
        vpkFiles = $vpkFiles.Count
    }
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputRoot "extraction-report.json") -Encoding UTF8

Write-Host "Unpacked: $Executable"
Write-Host "Output: $OutputRoot"
Write-Host "VPK files: $($vpkFiles.Count)"
