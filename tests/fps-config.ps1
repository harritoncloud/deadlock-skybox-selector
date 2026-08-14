[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path $PSScriptRoot -Parent
$installer = Join-Path $projectRoot "source\runtime\install-fps-config.ps1"
$profile = Join-Path $projectRoot "source\runtime\deadlock-fps.cfg"
$testRoot = Join-Path $projectRoot ".fps-config-test"
$cfgRoot = Join-Path $testRoot "game\citadel\cfg"
$gameInfo = Join-Path $testRoot "game\citadel\gameinfo.gi"
$autoexec = Join-Path $cfgRoot "autoexec.cfg"
$backupRoot = Join-Path $testRoot "backups"

function Invoke-Profile([string]$Action) {
    $process = Start-Process -FilePath "powershell.exe" -ArgumentList @(
        "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $installer,
        "-Action", $Action, "-DeadlockRoot", $testRoot, "-ProfilePath", $profile,
        "-BackupRoot", $backupRoot
    ) -Wait -PassThru -NoNewWindow
    return $process.ExitCode
}

if (Test-Path -LiteralPath $testRoot) {
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}
try {
    New-Item -ItemType Directory -Force -Path $cfgRoot | Out-Null
    Set-Content -LiteralPath $gameInfo -Value "GameInfo`r`n{`r`n}`r`n" -Encoding ASCII

    if ((Invoke-Profile "status") -ne 10) { throw "Missing autoexec status must be not-installed." }
    if ((Invoke-Profile "install") -ne 0) { throw "Install with a missing autoexec failed." }
    if ((Get-Content -LiteralPath $autoexec -Raw) -notmatch "FPS profile - start") {
        throw "Install with a missing autoexec did not create the managed block."
    }

    [IO.File]::WriteAllText($autoexec, "", [Text.UTF8Encoding]::new($false))
    if ((Invoke-Profile "status") -ne 10) { throw "Empty autoexec status must be not-installed." }
    if ((Invoke-Profile "install") -ne 0) { throw "Install with an empty autoexec failed." }
    if ((Get-Content -LiteralPath $autoexec -Raw) -notmatch "FPS profile - start") {
        throw "Install with an empty autoexec did not create the managed block."
    }

    $original = "fps_max `"240`"`r`nmat_viewportscale `"0.75`"`r`n"
    [IO.File]::WriteAllText($autoexec, $original, [Text.UTF8Encoding]::new($false))

    if ((Invoke-Profile "status") -ne 10) { throw "Fresh status must be not-installed." }
    if ((Invoke-Profile "install") -ne 0) { throw "Initial install failed." }
    $installed = Get-Content -LiteralPath $autoexec -Raw
    if ($installed -notmatch "fps_max `"240`"" -or $installed -notmatch "mat_viewportscale `"0.75`"") {
        throw "User-owned settings were not preserved."
    }
    if ($installed -notmatch "Deadlock Skybox Selector FPS profile - start") {
        throw "Managed profile marker is missing."
    }
    if ($installed -notmatch 'cl_ragdoll_limit\s+"8"') {
        throw "The managed profile lost the requested ragdoll limit."
    }
    if ((Invoke-Profile "status") -ne 0) { throw "Installed status failed." }
    if ((Invoke-Profile "install") -ne 0) { throw "Repeat install failed." }
    $repeat = Get-Content -LiteralPath $autoexec -Raw
    if (([regex]::Matches($repeat, "FPS profile - start")).Count -ne 1) {
        throw "Repeat install duplicated the managed profile."
    }
    if (@(Get-ChildItem -LiteralPath $backupRoot -File -Filter "autoexec-*.cfg").Count -lt 1) {
        throw "No backup was created."
    }
    Write-Host "FPS config test passed: preserve, backup, status and idempotent install."
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
