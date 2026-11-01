<#
.SYNOPSIS
Builds the Windows app (Debug, unpackaged) and launches it, replacing a copy that's already running.

.DESCRIPTION
Builds for this PC's architecture (x64 or arm64). For the packaged Store build, use
./publish-windows.ps1 -Test -Install instead.

.EXAMPLE
./deploy-windows.ps1
#>
$ErrorActionPreference = 'Stop'

$Proj = Join-Path $PSScriptRoot 'Readout'
$Tfm = 'net10.0-windows10.0.19041.0'
$Rid = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
$Exe = Join-Path $Proj "bin/Debug/$Tfm/$Rid/Readout.exe"

# Quit the running copy first: it locks the files the build replaces. Match this build's path,
# so the installed Store copy keeps running.
$Running = Get-Process Readout -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and (Resolve-Path $_.Path).Path -eq [IO.Path]::GetFullPath($Exe) }
if ($Running) {
    Write-Host '==> Quitting the running copy...'
    $Running | Stop-Process -Force
    $Running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

Write-Host "==> Building ($Rid)..."
dotnet build $Proj -f $Tfm -c Debug "-p:WindowsRuntimeIdentifier=$Rid"
if ($LASTEXITCODE -ne 0) { Write-Error 'dotnet build failed.' }

Write-Host '==> Launching...'
Start-Process $Exe
Write-Host "==> Done: $Exe"
