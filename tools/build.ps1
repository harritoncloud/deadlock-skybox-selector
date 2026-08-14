[CmdletBinding()]
param(
    [string]$Csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$sourceRoot = Join-Path $repositoryRoot "source"
$unpackedRoot = Join-Path $repositoryRoot "unpacked"
$runtimeSource = Join-Path $sourceRoot "runtime"
$runtimeTools = Join-Path $unpackedRoot "runtime"
$assetRoot = Join-Path $unpackedRoot "assets"
$buildRoot = Join-Path $repositoryRoot ".build"
$distRoot = Join-Path $repositoryRoot "dist"
$buildRuntime = Join-Path $buildRoot "runtime"
$archivePath = Join-Path $buildRoot "skyboxes.7z"
$installerPath = Join-Path $buildRoot "DeadlockGameInfoInstaller.exe"
$outputPath = Join-Path $distRoot "SkyboxSelector.exe"
$runtimeChecksums = Join-Path $buildRuntime "runtime-checksums.sha256"

$launcherSource = Join-Path $sourceRoot "launcher\Program.cs"
$uiSource = Join-Path $sourceRoot "launcher\SelectorForm.cs"
$launcherManifest = Join-Path $sourceRoot "launcher\app.manifest"
$launcherIcon = Join-Path $sourceRoot "launcher\app.ico"
$installerSource = Join-Path $sourceRoot "gameinfo-installer\Program.cs"
$installerManifest = Join-Path $sourceRoot "gameinfo-installer\app.manifest"
$gameInfoPath = Join-Path $sourceRoot "config\gameinfo.gi"
$sevenZip = Join-Path $runtimeTools "7z.exe"
$sevenZipDll = Join-Path $runtimeTools "7z.dll"
$sevenZipLicense = Join-Path $runtimeTools "7zip-License.txt"

function Assert-File([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required file is missing: $Path"
    }
}

function Assert-SafeChild([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullRoot = [IO.Path]::GetFullPath($repositoryRoot).TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($fullRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset a directory outside the repository: $fullPath"
    }
}

function Reset-Directory([string]$Path) {
    Assert-SafeChild $Path
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

foreach ($path in @(
    $Csc,
    $launcherSource,
    $uiSource,
    $launcherManifest,
    $launcherIcon,
    $installerSource,
    $installerManifest,
    $gameInfoPath,
    $sevenZip,
    $sevenZipDll,
    $sevenZipLicense,
    (Join-Path $runtimeSource "SkyboxSelector.cmd"),
    (Join-Path $runtimeSource "select-skybox.ps1"),
    (Join-Path $runtimeSource "install-fps-config.ps1"),
    (Join-Path $runtimeSource "deadlock-fps.cfg"),
    (Join-Path $assetRoot "manifest.json")
)) {
    Assert-File $path
}

$gameInfoText = Get-Content -Raw -LiteralPath $gameInfoPath
if ($gameInfoText -notmatch '(?m)^GameInfo\s*$') {
    throw "GameInfo root is missing."
}
if ($gameInfoText -notmatch '(?im)^\s*citadel_show_survey\s+"false"') {
    throw "GameInfo must disable the playtester survey."
}
if ($gameInfoText -notmatch '(?im)^\s*cl_phys_enabled\s+"true"') {
    throw "GameInfo must keep client physics enabled."
}
if (([regex]::Matches($gameInfoText, '\{')).Count -ne ([regex]::Matches($gameInfoText, '\}')).Count) {
    throw "GameInfo braces are unbalanced."
}

Reset-Directory $buildRoot
Reset-Directory $distRoot
New-Item -ItemType Directory -Force -Path $buildRuntime | Out-Null

& $sevenZip a -t7z -mx=9 -m0=lzma2 -md=256m -mfb=273 -ms=on -mmt=on -- $archivePath (Join-Path $assetRoot "*") | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "7-Zip failed to build the asset archive."
}
$archiveTest = @(& $sevenZip t -- $archivePath 2>&1)
if ($LASTEXITCODE -ne 0 -or -not ($archiveTest -match "Everything is Ok")) {
    throw "Built asset archive failed validation."
}
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$archiveHash  skyboxes.7z" | Set-Content -LiteralPath (Join-Path $buildRuntime "assets.sha256") -Encoding ASCII

& $Csc @(
    "/nologo",
    "/utf8output",
    "/optimize+",
    "/checked+",
    "/target:exe",
    "/platform:anycpu",
    "/win32manifest:$installerManifest",
    "/resource:$gameInfoPath,DeadlockGameInfoInstaller.gameinfo.gi",
    "/out:$installerPath",
    $installerSource
)
if ($LASTEXITCODE -ne 0) {
    throw "GameInfo installer compilation failed with exit code $LASTEXITCODE."
}

$runtimeFiles = [ordered]@{
    "SkyboxSelector.cmd" = (Join-Path $runtimeSource "SkyboxSelector.cmd")
    "select-skybox.ps1" = (Join-Path $runtimeSource "select-skybox.ps1")
    "install-fps-config.ps1" = (Join-Path $runtimeSource "install-fps-config.ps1")
    "deadlock-fps.cfg" = (Join-Path $runtimeSource "deadlock-fps.cfg")
    "DeadlockGameInfoInstaller.exe" = $installerPath
    "7z.exe" = $sevenZip
    "7z.dll" = $sevenZipDll
    "7zip-License.txt" = $sevenZipLicense
    "assets.sha256" = (Join-Path $buildRuntime "assets.sha256")
}
foreach ($entry in $runtimeFiles.GetEnumerator()) {
    $destination = Join-Path $buildRuntime $entry.Key
    if ([IO.Path]::GetFullPath($entry.Value) -ne [IO.Path]::GetFullPath($destination)) {
        Copy-Item -LiteralPath $entry.Value -Destination $destination -Force
    }
}

$checksumLines = foreach ($entry in $runtimeFiles.GetEnumerator()) {
    $hash = (Get-FileHash -LiteralPath $entry.Value -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($entry.Key)"
}
$checksumLines | Set-Content -LiteralPath $runtimeChecksums -Encoding ASCII

$resources = [ordered]@{
    (Join-Path $buildRuntime "SkyboxSelector.cmd") = "SkyboxSelector.Payload.SkyboxSelector.cmd"
    (Join-Path $buildRuntime "select-skybox.ps1") = "SkyboxSelector.Payload.select-skybox.ps1"
    (Join-Path $buildRuntime "install-fps-config.ps1") = "SkyboxSelector.Payload.install-fps-config.ps1"
    (Join-Path $buildRuntime "deadlock-fps.cfg") = "SkyboxSelector.Payload.deadlock-fps.cfg"
    (Join-Path $buildRuntime "DeadlockGameInfoInstaller.exe") = "SkyboxSelector.Payload.DeadlockGameInfoInstaller.exe"
    (Join-Path $buildRuntime "7z.exe") = "SkyboxSelector.Payload.7z.exe"
    (Join-Path $buildRuntime "7z.dll") = "SkyboxSelector.Payload.7z.dll"
    (Join-Path $buildRuntime "7zip-License.txt") = "SkyboxSelector.Payload.7zip-License.txt"
    (Join-Path $buildRuntime "assets.sha256") = "SkyboxSelector.Payload.assets.sha256"
    $runtimeChecksums = "SkyboxSelector.Payload.runtime-checksums.sha256"
    $archivePath = "SkyboxSelector.Payload.skyboxes.7z"
}

$compilerArguments = @(
    "/nologo",
    "/utf8output",
    "/optimize+",
    "/target:winexe",
    "/platform:anycpu",
    "/reference:System.Drawing.dll",
    "/reference:System.Web.Extensions.dll",
    "/reference:System.Windows.Forms.dll",
    "/win32manifest:$launcherManifest",
    "/win32icon:$launcherIcon",
    "/out:$outputPath"
)
foreach ($resource in $resources.GetEnumerator()) {
    $compilerArguments += "/resource:$($resource.Key),$($resource.Value)"
}
$compilerArguments += $launcherSource
$compilerArguments += $uiSource

& $Csc $compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "Launcher compilation failed with exit code $LASTEXITCODE."
}

$assembly = [Reflection.Assembly]::LoadFile($outputPath)
$expectedResourceNames = @($resources.Values | Sort-Object)
$actualResourceNames = @($assembly.GetManifestResourceNames() | Sort-Object)
if (($expectedResourceNames -join "`n") -ne ($actualResourceNames -join "`n")) {
    throw "Built executable resource validation failed."
}

$manifest = Get-Content -LiteralPath (Join-Path $assetRoot "manifest.json") -Raw | ConvertFrom-Json
$report = [ordered]@{
    builtAtUtc = [DateTime]::UtcNow.ToString("o")
    executable = "dist/SkyboxSelector.exe"
    bytes = (Get-Item -LiteralPath $outputPath).Length
    sha256 = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash.ToLowerInvariant()
    embeddedAsset = [ordered]@{
        bytes = (Get-Item -LiteralPath $archivePath).Length
        sha256 = $archiveHash
        variants = @($manifest.variants).Count
        compression = "7z solid LZMA2"
    }
    runtimeCache = "%LOCALAPPDATA%\DeadlockSkyboxSelector\runtime-v2"
    gameCache = "<Deadlock>\dlskybox"
    launchesGame = $false
    resources = $actualResourceNames
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $distRoot "build-report.json") -Encoding UTF8

Write-Host "Built: $outputPath"
Write-Host "Bytes: $((Get-Item -LiteralPath $outputPath).Length)"
Write-Host "SHA-256: $((Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash)"
