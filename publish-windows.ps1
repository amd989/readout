<#
.SYNOPSIS
Builds the Microsoft Store packages of the Windows app: one unsigned .msix per architecture
(x64 and arm64) in artifacts/windows. The Store signs them; upload both in Partner Center >
the app > Packages.

.DESCRIPTION
The package identity (Name, Publisher, PublisherDisplayName in
Readout/Platforms/Windows/Package.appxmanifest) must match Partner Center > the app >
Product identity. While it still has the placeholders, only -Test builds run.

The version is ApplicationDisplayVersion from Readout.csproj plus the build number, with 0 as
the last part (the Store requires it): 1.0 and build 26301 give 1.0.26301.0. Each upload needs
a higher version than the last.

.PARAMETER Build
Build number, 1 to 65535. Defaults to the date as yyDDD (26301 for 28 October 2026); pass a
higher one for a second upload on the same day.

.PARAMETER Test
Allow the placeholder identity. The packages work for local testing but can't be uploaded.

.PARAMETER Install
Also install this PC's build for a test run (registers the build folder; needs Developer Mode).

.PARAMETER Wack
Also install this PC's build and run the Windows App Certification Kit on it (needs an
administrator shell; the report goes to artifacts/windows).

.EXAMPLE
./publish-windows.ps1 -Test -Install
#>
param(
    [ValidateRange(1, 65535)]
    [int]$Build = [int]((Get-Date).ToString('yy')) * 1000 + (Get-Date).DayOfYear,
    [switch]$Test,
    [switch]$Install,
    [switch]$Wack
)

$ErrorActionPreference = 'Stop'

$Proj = Join-Path $PSScriptRoot 'Readout'
$Tfm = 'net10.0-windows10.0.19041.0'
$Bin = Join-Path $Proj "bin/Release/$Tfm"
$Out = Join-Path $PSScriptRoot 'artifacts/windows'
$Rids = @('win-x64', 'win-arm64')

# The package identity has to come from Partner Center.
[xml]$Manifest = Get-Content (Join-Path $Proj 'Platforms/Windows/Package.appxmanifest')
$Identity = $Manifest.Package.Identity
if (($Identity.Name -like 'PARTNER-CENTER-*' -or $Identity.Publisher -like '*PARTNER-CENTER-*') -and -not $Test) {
    Write-Error ("Package.appxmanifest still has the placeholder identity. Copy Name and Publisher " +
        "from Partner Center > the app > Product identity, or pass -Test for a local-only build.")
}

# ApplicationDisplayVersion is major.minor; the build number goes third.
[xml]$Csproj = Get-Content (Join-Path $Proj 'Readout.csproj')
$Display = @($Csproj.Project.PropertyGroup.ApplicationDisplayVersion | Where-Object { $_ })[0]
if ($Display -notmatch '^\d+\.\d+$') {
    Write-Error "Expected ApplicationDisplayVersion as major.minor (like 1.0), got '$Display'."
}
$Version = "$Display.$Build.0"

New-Item -ItemType Directory -Force $Out | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem

foreach ($Rid in $Rids) {
    $Arch = $Rid.Substring(4)
    Write-Host "==> Building $Version for $Arch..."
    # Stale packages from an earlier build would otherwise be picked up below.
    Remove-Item -Recurse -Force (Join-Path $Bin "$Rid/AppPackages") -ErrorAction SilentlyContinue

    dotnet publish $Proj -f $Tfm -c Release `
        "-p:WindowsRuntimeIdentifier=$Rid" `
        "-p:ApplicationDisplayVersion=$Display.$Build" `
        "-p:ApplicationVersion=0" `
        "-p:AppxPackageSigningEnabled=false"
    if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed for $Rid." }

    $Msix = Get-ChildItem (Join-Path $Bin "$Rid/AppPackages") -Recurse -Filter "Readout_*_$Arch.msix" |
        Select-Object -First 1
    if (-not $Msix) { Write-Error "No .msix for $Arch under $Bin/$Rid/AppPackages." }

    # Check what the Store will see.
    $Zip = [System.IO.Compression.ZipFile]::OpenRead($Msix.FullName)
    try {
        $Reader = New-Object System.IO.StreamReader($Zip.GetEntry('AppxManifest.xml').Open())
        [xml]$Built = $Reader.ReadToEnd()
        $Reader.Dispose()
    } finally { $Zip.Dispose() }
    $BuiltIdentity = $Built.Package.Identity
    if ($BuiltIdentity.Version -ne $Version) {
        Write-Error "Package version is $($BuiltIdentity.Version), expected $Version."
    }
    if ($BuiltIdentity.ProcessorArchitecture -ne $Arch) {
        Write-Error "Package is for $($BuiltIdentity.ProcessorArchitecture), expected $Arch."
    }
    if (-not ($Built.Package.Capabilities.DeviceCapability | Where-Object { $_.Name -eq 'bluetooth' })) {
        Write-Error "The package doesn't declare the bluetooth capability."
    }

    $Target = Join-Path $Out $Msix.Name
    Copy-Item $Msix.FullName $Target -Force
    Write-Host "    $Target"
}

# The certification kit tests an installed app (an unsigned .msix can't be installed on its own).
if ($Wack) { $Install = $true }

if ($Install) {
    $HostArch = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'arm64' } else { 'x64' }
    $Layout = Join-Path $Bin "win-$HostArch/AppxManifest.xml"
    $Runtime = Get-ChildItem (Join-Path $Bin "win-$HostArch/AppPackages") -Recurse -Filter 'Microsoft.WindowsAppRuntime.*.msix' |
        Where-Object { $_.Directory.Name -eq $HostArch } | Select-Object -First 1
    Write-Host "==> Installing the $HostArch build for testing..."
    # The app needs the Windows App Runtime. -Register can't take it as a dependency file, so
    # install it on its own first if this PC doesn't have it (the Store does this for users).
    if ($Runtime -and -not (Get-AppxPackage -Name $Runtime.BaseName | Where-Object { "$($_.Architecture)" -eq $HostArch })) {
        Add-AppxPackage -Path $Runtime.FullName
    }
    Add-AppxPackage -Register $Layout -ForceApplicationShutdown
    Write-Host "    Readout is in the Start menu. Remove it with: Get-AppxPackage $($Identity.Name) | Remove-AppxPackage"
}

if ($Wack) {
    $Appcert = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/App Certification Kit/appcert.exe'
    $IsAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).
        IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not (Test-Path $Appcert)) {
        Write-Error "No Windows App Certification Kit; it comes with the Windows SDK."
    }
    if (-not $IsAdmin) { Write-Error "The certification kit needs an administrator shell." }
    $FullName = (Get-AppxPackage -Name $Identity.Name).PackageFullName
    $Report = Join-Path $Out "wack-$HostArch.xml"
    Write-Host "==> Certification kit on $FullName (takes a few minutes)..."
    & $Appcert reset | Out-Null
    & $Appcert test -packagefullname $FullName -reportoutputpath $Report
    [xml]$Result = Get-Content $Report
    $Overall = $Result.REPORT.OVERALL_RESULT
    Write-Host "    $Overall ($Report)"
    if ($Overall -eq 'FAIL') { Write-Error "The certification kit reported failures; see $Report." }
}

Write-Host "==> Done: $Version in $Out"
