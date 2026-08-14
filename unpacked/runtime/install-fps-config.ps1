[CmdletBinding()]
param(
    [ValidateSet("status", "install")]
    [string]$Action = "status",
    [Parameter(Mandatory = $true)]
    [string]$DeadlockRoot,
    [Parameter(Mandatory = $true)]
    [string]$ProfilePath,
    [string]$BackupRoot
)

$ErrorActionPreference = "Stop"
$markerStart = "// Deadlock Skybox Selector FPS profile - start"
$markerEnd = "// Deadlock Skybox Selector FPS profile - end"

function Assert-ChildPath([string]$Path, [string]$Root) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($fullRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe config path: $fullPath"
    }
}

function Normalize-Lines([string]$Text) {
    return (($Text -replace "`r`n", "`n") -replace "`r", "`n").Trim()
}

try {
    $deadlockRoot = [IO.Path]::GetFullPath($DeadlockRoot).TrimEnd('\')
    $gameInfo = Join-Path $deadlockRoot "game\citadel\gameinfo.gi"
    if (-not (Test-Path -LiteralPath $gameInfo -PathType Leaf)) {
        throw "The selected directory is not a valid Deadlock installation."
    }
    if (-not (Test-Path -LiteralPath $ProfilePath -PathType Leaf)) {
        throw "The embedded FPS profile is missing."
    }

    $cfgRoot = Join-Path $deadlockRoot "game\citadel\cfg"
    $autoexec = Join-Path $cfgRoot "autoexec.cfg"
    Assert-ChildPath $autoexec $cfgRoot
    if (-not $BackupRoot) {
        $BackupRoot = Join-Path $deadlockRoot "dlskybox\backups\fps-config"
    }
    $profile = Normalize-Lines (Get-Content -LiteralPath $ProfilePath -Raw)
    if (-not $profile) {
        throw "The FPS profile is empty."
    }

    $existing = if (Test-Path -LiteralPath $autoexec -PathType Leaf) {
        Get-Content -LiteralPath $autoexec -Raw
    } else {
        ""
    }
    $pattern = "(?ms)^" + [regex]::Escape($markerStart) + ".*?^" + [regex]::Escape($markerEnd) + "\s*"
    $managedBlock = $markerStart + "`r`n" + ($profile -replace "`n", "`r`n") + "`r`n" + $markerEnd
    $match = [regex]::Match($existing, $pattern)

    if ($Action -eq "status") {
        if ($match.Success -and (Normalize-Lines $match.Value).Contains((Normalize-Lines $profile))) {
            Write-Output "installed"
            exit 0
        }
        Write-Output "not-installed"
        exit 10
    }

    if (@(Get-Process -Name "deadlock" -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "Close Deadlock before installing the FPS config."
    }

    New-Item -ItemType Directory -Force -Path $cfgRoot, $BackupRoot | Out-Null
    if (Test-Path -LiteralPath $autoexec -PathType Leaf) {
        $stamp = Get-Date -Format "yyyyMMdd-HHmmss-fff"
        $backup = Join-Path $BackupRoot ("autoexec-" + $stamp + ".cfg")
        Copy-Item -LiteralPath $autoexec -Destination $backup -Force
        if ((Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $autoexec -Algorithm SHA256).Hash) {
            throw "FPS config backup verification failed."
        }
    }

    $clean = [regex]::Replace($existing, $pattern, "").Trim()
    $newText = if ($clean) {
        $clean + "`r`n`r`n" + $managedBlock + "`r`n"
    } else {
        $managedBlock + "`r`n"
    }
    $temporary = $autoexec + ".fps-profile-new"
    [IO.File]::WriteAllText($temporary, $newText, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $autoexec -Force
    if (-not [regex]::IsMatch((Get-Content -LiteralPath $autoexec -Raw), $pattern)) {
        throw "FPS config verification failed after installation."
    }
    Write-Output "installed"
    exit 0
} catch {
    Write-Output ("ERROR: " + $_.Exception.Message)
    exit 1
}
