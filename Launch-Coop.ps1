[CmdletBinding(DefaultParameterSetName = 'Host')]
param(
    [Parameter(Mandatory)][string] $GamePath,
    [Parameter(Mandatory, ParameterSetName = 'Host')][switch] $HostGame,
    [Parameter(Mandatory, ParameterSetName = 'Join')][string] $Join,
    [ValidateRange(1024, 65535)][int] $Port = 46241,
    [switch] $Install
)

$ErrorActionPreference = 'Stop'
$game = (Resolve-Path -LiteralPath $GamePath).Path
$core = Join-Path $game 'coremod'
$launcher = Join-Path $core 'core/host/startup/DeadCellsModding.exe'
$modName = 'DeadCellsMultiplayerMod'
$mods = Join-Path $core 'mods'
$installed = Join-Path $mods $modName

if (!(Test-Path -LiteralPath $launcher -PathType Leaf)) {
    throw 'DCCM 35.14.1 must be installed in the selected Dead Cells Update 35 folder.'
}
$version = (Get-Item -LiteralPath $launcher).VersionInfo
if ($version.FileMajorPart -ne 35 -or $version.FileMinorPart -ne 14 -or $version.FileBuildPart -ne 1) {
    throw "Expected DCCM 35.14.1. Found $($version.FileVersion)."
}

if ($HostGame) {
    $mode = "host:$Port"
}
else {
    $address = $null
    if ($Join -notmatch '^\d{1,3}(\.\d{1,3}){3}$' -or
        ![Net.IPAddress]::TryParse($Join, [ref]$address) -or
        $address.Equals([Net.IPAddress]::Any) -or $address.Equals([Net.IPAddress]::Broadcast)) {
        throw 'Enter the host IPv4 address, for example -Join 192.168.1.20.'
    }
    $mode = "join:${address}:$Port"
}

if (Get-Process DeadCellsModding -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $launcher }) {
    throw 'Close Dead Cells in this installation before starting another co-op instance.'
}

if ($Install) {
    $package = Join-Path $PSScriptRoot "bin/Release/net10.0/win-x64/output/$modName"
    if (!(Test-Path -LiteralPath (Join-Path $package "$modName.dll") -PathType Leaf) -or
        !(Test-Path -LiteralPath (Join-Path $package 'modinfo.json') -PathType Leaf)) {
        throw 'Build the Release package with dotnet build -c Release first.'
    }
    New-Item -ItemType Directory -Path $mods -Force | Out-Null
    $installId = [Guid]::NewGuid().ToString('N')
    $staging = Join-Path $core "$modName-install-$installId"
    $previous = Join-Path $core "$modName-backup-$installId"
    Copy-Item -LiteralPath $package -Destination $staging -Recurse
    if (Test-Path -LiteralPath $installed) {
        Move-Item -LiteralPath $installed -Destination $previous
    }
    try {
        Move-Item -LiteralPath $staging -Destination $installed
    }
    catch {
        if (Test-Path -LiteralPath $previous) {
            Move-Item -LiteralPath $previous -Destination $installed
        }
        throw
    }
}

if (!(Test-Path -LiteralPath (Join-Path $installed "$modName.dll") -PathType Leaf) -or
    !(Test-Path -LiteralPath (Join-Path $installed 'modinfo.json') -PathType Leaf)) {
    throw 'Run once with -Install to install the Release mod.'
}

$previousMode = $env:DEAD_CELLS_COOP
$previousPath = $env:Path
try {
    $env:DEAD_CELLS_COOP = $mode
    if ($env:DOTNET_ROOT -and (Test-Path -LiteralPath (Join-Path $env:DOTNET_ROOT 'dotnet.exe') -PathType Leaf)) {
        $env:Path = "$env:DOTNET_ROOT;$env:Path"
    }
    Start-Process -FilePath $launcher -WorkingDirectory $game
}
finally {
    $env:DEAD_CELLS_COOP = $previousMode
    $env:Path = $previousPath
}
