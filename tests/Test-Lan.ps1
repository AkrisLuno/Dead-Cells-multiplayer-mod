[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $InstanceRoot,
    [Parameter(Mandatory)][string] $Package,
    [Parameter(Mandatory)][string] $CoopAnythingRoot,
    [Net.IPAddress] $Address = '127.0.0.1',
    [ValidateRange(1024, 65535)][int] $Port = 46239,
    [ValidateRange(1024, 65535)][int] $JoinPort = $Port,
    [switch] $VerifyExit,
    [switch] $VerifyShop,
    [switch] $VerifyLoot,
    [switch] $VerifyPresentation,
    [switch] $VerifyNormal,
    [switch] $VerifyEffects,
    [switch] $VerifyEquipment,
    [switch] $VerifySummons,
    [switch] $VerifyCombat,
    [switch] $VerifyRevive,
    [switch] $VerifyEnemyTarget,
    [switch] $VerifyBoss
)

$ErrorActionPreference = 'Stop'
if (@($VerifyExit, $VerifyShop, $VerifyLoot, $VerifyPresentation, $VerifyNormal, $VerifyEffects, $VerifyEquipment, $VerifySummons, $VerifyCombat, $VerifyRevive, $VerifyEnemyTarget, $VerifyBoss).Where({ $_ }).Count -gt 1) {
    throw 'Choose one gameplay check per run.'
}
$root = (Resolve-Path -LiteralPath $InstanceRoot).Path
$packagePath = (Resolve-Path -LiteralPath $Package).Path
$slotTool = Join-Path $CoopAnythingRoot 'tools/gameslot/gameslot.py'
$lease = 'dead-cells-lan-' + [Guid]::NewGuid().ToString('N')
$instances = @{}
foreach ($side in 'Host', 'Client') {
    $game = Join-Path $root $side
    $launch = Join-Path $game 'coremod/core/host/startup/DeadCellsModding.exe'
    $settings = Get-Content -LiteralPath (Join-Path $game 'save/dc_options.json') -Raw | ConvertFrom-Json
    if (!(Test-Path -LiteralPath (Join-Path $game '.dead-cells-coop-qa')) -or
        !(Test-Path -LiteralPath $launch) -or !$settings.mute -or $settings.masterVolume -ne 0) {
        throw "$side must be an isolated QA copy with audio muted."
    }
    $instances[$side] = @{
        Root = $game
        Launcher = $launch
        Log = Join-Path $game 'coremod/logs/log_latest.log'
        CrashLog = Join-Path $game 'coremod/core/host/last_error.txt'
    }
}

$previousMode = $env:DEAD_CELLS_COOP
$previousQa = $env:DEAD_CELLS_COOP_QA
$previousStop = $env:DEAD_CELLS_COOP_QA_STOP
$previousPath = $env:Path
$testFailure = $null
$shutdownFailure = $null

function Wait-ForGameLog($instance, $pattern, $deadline) {
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($instance.Process.HasExited) {
            throw "Game PID $($instance.Process.Id) exited. See $($instance.Log)."
        }
        if (Test-Path -LiteralPath $instance.Log) {
            $logFile = Get-Item -LiteralPath $instance.Log
            if ($logFile.LastWriteTimeUtc -ge $instance.Started) {
                $failure = Select-String -LiteralPath $instance.Log -Pattern 'Fatal Error|COOP_QA FAIL' | Select-Object -Last 1
                if ($failure) { throw "$($failure.Line). See $($instance.Log)." }
                $result = Select-String -LiteralPath $instance.Log -Pattern $pattern | Select-Object -Last 1
                if ($result) { return $result }
            }
        }
        Start-Sleep -Milliseconds 300
    }
    throw "Timed out waiting for $pattern in $($instance.Log)."
}

python -I $slotTool acquire --owner $lease --slots 2 --ttl 300
if ($LASTEXITCODE -ne 0) { throw 'Could not acquire two Co-op Anything test slots.' }

try {
    if ($env:DOTNET_ROOT) { $env:Path = "$env:DOTNET_ROOT;$env:Path" }
    $env:DEAD_CELLS_COOP_QA = if ($VerifyBoss) {
        'boss'
    } elseif ($VerifyEnemyTarget) {
        'enemy-target'
    } elseif ($VerifyRevive) {
        'revive'
    } elseif ($VerifyCombat) {
        'combat'
    } elseif ($VerifySummons) {
        'summons'
    } elseif ($VerifyEquipment) {
        'equipment'
    } elseif ($VerifyEffects) {
        'effects'
    } elseif ($VerifyNormal) {
        'normal'
    } elseif ($VerifyPresentation) {
        'presentation'
    } elseif ($VerifyLoot) {
        'loot'
    } elseif ($VerifyShop) {
        'shop'
    } elseif ($VerifyExit) {
        'exit'
    } else {
        '1'
    }
    foreach ($side in 'Host', 'Client') {
        $instance = $instances[$side]
        $destination = Join-Path $instance.Root 'coremod/mods/DeadCellsMultiplayerMod'
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Copy-Item -Path (Join-Path $packagePath '*') -Destination $destination -Force
        $env:DEAD_CELLS_COOP = if ($side -eq 'Host') { "host:$Port" } else { "join:${Address}:$JoinPort" }
        $instance.StopSignal = Join-Path $instance.Root ('.stop-' + $lease)
        $env:DEAD_CELLS_COOP_QA_STOP = $instance.StopSignal
        $instance.Started = [DateTime]::UtcNow
        $instance.Process = Start-Process -FilePath $instance.Launcher -WorkingDirectory $instance.Root -WindowStyle Hidden -PassThru
        python -I $slotTool register-pid --owner $lease --pid $instance.Process.Id
        if ($LASTEXITCODE -ne 0) { throw 'Could not register the launched process.' }
        if ($side -eq 'Host') {
            Wait-ForGameLog $instance "LAN listening on .*:$Port\." ([DateTime]::UtcNow.AddSeconds(60)) | Out-Null
        }
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(100)
    $resultPattern = 'COOP_QA PASS Level=(\S+) Seed=(\d+)'
    $hostResult = Wait-ForGameLog $instances.Host $resultPattern $deadline
    $clientResult = Wait-ForGameLog $instances.Client $resultPattern $deadline
    if ($hostResult.Matches[0].Value -ne $clientResult.Matches[0].Value) {
        throw "Peers disagree. $($hostResult.Line) / $($clientResult.Line)"
    }
    if ($VerifyShop) {
        foreach ($revision in 1, 2) {
            $pattern = "COOP_QA SHOP Revision=$revision Uses=\d+ Stock=[0-9A-F]+"
            $hostStock = Wait-ForGameLog $instances.Host $pattern $deadline
            $clientStock = Wait-ForGameLog $instances.Client $pattern $deadline
            if ($hostStock.Matches[0].Value -ne $clientStock.Matches[0].Value) {
                throw "Peers disagree on stock after reroll $revision."
            }
            $hostStock.Matches[0].Value
        }
    }
    if ($VerifyLoot) {
        $pattern = 'COOP_QA LOOT Gold=2 Gem=1 Cells=1 Heal=1 Weapon=1 Food=1 ClientOnly=0'
        foreach ($side in 'Host', 'Client') {
            (Wait-ForGameLog $instances[$side] $pattern $deadline).Line
            (Wait-ForGameLog $instances[$side] 'COOP_QA PICKUP Paid=371 RemainingGold=1' $deadline).Line
        }
    }
    if ($VerifyPresentation) {
        $pattern = 'COOP_QA PRESENTATION Outline=True Direction=-?1 Sound=\S+ Muted=True'
        foreach ($side in 'Host', 'Client') {
            (Wait-ForGameLog $instances[$side] $pattern $deadline).Line
            (Wait-ForGameLog $instances[$side] 'COOP_QA AUDIO Near=\S+ Middle=\S+ FarSilent=True' $deadline).Line
        }
    }
    if ($VerifyNormal) {
        foreach ($side in 'Host', 'Client') {
            (Wait-ForGameLog $instances[$side] 'COOP_QA NORMAL resumed. Partner jump, dodge and movement sound observed.' $deadline).Line
        }
    }
    if ($VerifyEffects -or $VerifyEquipment) {
        $pattern = 'COOP_QA EFFECTS Land=True Dodge=True Dash=True StarterSword=\S+'
        foreach ($side in 'Host', 'Client') {
            (Wait-ForGameLog $instances[$side] $pattern $deadline).Line
        }
    }
    if ($VerifyEquipment) {
        foreach ($side in 'Host', 'Client') {
            foreach ($item in 'StartBow', 'ExplosiveGrenade', 'HorizontalTurret', 'Owl') {
                (Wait-ForGameLog $instances[$side] "COOP_QA EQUIPMENT Item=$item Kind=\w+ Captured=True Rendered=True" $deadline).Line
            }
            (Wait-ForGameLog $instances[$side] 'COOP_QA EQUIPMENT ShieldParry=True' $deadline).Line
            (Wait-ForGameLog $instances[$side] 'COOP_QA EQUIPMENT Heal=True Explosion=True Smoke=True Sound=True' $deadline).Line
        }
    }
    if ($VerifySummons) {
        foreach ($side in 'Host', 'Client') {
            (Wait-ForGameLog $instances[$side] 'COOP_QA SUMMON Owl Arrived=True Audio=True Frost=True Removed=True' $deadline).Line
            (Wait-ForGameLog $instances[$side] 'COOP_QA SUMMON FlyingSword Arrived=True Trail=True Frost=True Removed=True' $deadline).Line
            (Wait-ForGameLog $instances[$side] 'COOP_QA SUMMON Mushroom Arrived=True Explosion=True Removed=True' $deadline).Line
            (Wait-ForGameLog $instances[$side] 'COOP_QA SUMMON GiantWhistle Fist=True' $deadline).Line
        }
    }
    if ($VerifyCombat) {
        foreach ($side in 'Host', 'Client') {
            foreach ($event in 'Spawn=True Life=100', 'ClientHit=30 Life=70', 'HostHit=20 Life=50', 'Death=True FinalLife=0 Impacts=3') {
                (Wait-ForGameLog $instances[$side] "COOP_QA COMBAT $event" $deadline).Line
            }
        }
    }
    if ($VerifyRevive) {
        foreach ($side in 'Host', 'Client') {
            foreach ($event in 'HostRevived=True', 'ClientRevived=True', 'BothRecovered=True SameHero=True') {
                (Wait-ForGameLog $instances[$side] "COOP_QA REVIVAL $event" $deadline).Line
            }
        }
    }
    if ($VerifyEnemyTarget) {
        (Wait-ForGameLog $instances.Host 'COOP_QA ENEMY_TARGET HostTarget=True StrikeAcknowledged=True' $deadline).Line
        (Wait-ForGameLog $instances.Client 'COOP_QA ENEMY_TARGET ClientHealthBefore=\d+ After=\d+' $deadline).Line
    }
    if ($VerifyBoss) {
        foreach ($side in 'Host', 'Client') {
            foreach ($event in 'Ready=True Life=\d+', 'ClientHit=True Life=\d+', 'Phase=[1-9]\d*', 'Defeated=True GateUnlocked=True') {
                (Wait-ForGameLog $instances[$side] "COOP_QA BOSS $event" $deadline).Line
            }
        }
    }
    $hostResult.Line
    $clientResult.Line
}
catch {
    $testFailure = $_
}
finally {
    # DCCM reports a terminated process as a fatal crash and opens its crash log.
    foreach ($instance in $instances.Values) {
        if ($instance.Process -and !$instance.Process.HasExited) {
            [IO.File]::WriteAllText($instance.StopSignal, 'quit')
        }
    }
    foreach ($instance in $instances.Values) {
        $process = $instance.Process
        if (!$process) { continue }
        if (!$process.WaitForExit(12000)) {
            [void] $process.CloseMainWindow()
        }
        if (!$process.WaitForExit(15000)) {
            $shutdownFailure = "QA game PID $($process.Id) did not close normally."
        }
        elseif ($process.ExitCode -ne 0 -and !$testFailure) {
            $shutdownFailure = "QA game PID $($process.Id) exited with code $($process.ExitCode)."
        }
        if ((Test-Path -LiteralPath $instance.CrashLog) -and
            (Get-Item -LiteralPath $instance.CrashLog).LastWriteTimeUtc -ge $instance.Started) {
            $shutdownFailure = "DCCM generated a new crash report at $($instance.CrashLog)."
        }
        if ($process.HasExited) { [IO.File]::Delete($instance.StopSignal) }
    }
    python -I $slotTool release --owner $lease
    $env:DEAD_CELLS_COOP = $previousMode
    $env:DEAD_CELLS_COOP_QA = $previousQa
    $env:DEAD_CELLS_COOP_QA_STOP = $previousStop
    $env:Path = $previousPath
}

if ($testFailure -or $shutdownFailure) {
    'RESULT: FAIL'
    if ($shutdownFailure) { Write-Warning $shutdownFailure }
    if ($testFailure) { throw $testFailure }
    throw $shutdownFailure
}
'RESULT: PASS'
