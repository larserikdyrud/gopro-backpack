<#
.SYNOPSIS
    Publish for the Pi, copy it over SSH and run install.sh.

.EXAMPLE
    ./deploy/deploy.ps1 -Target pi@gopro-bp.local

.NOTES
    The overlay filesystem on the Pi must be off (install.sh checks and refuses otherwise).
    Needs the .NET 9 SDK, plus ssh, scp and tar (all built into Windows 10/11).
#>
param(
    [Parameter(Mandatory)] [string] $Target,
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$deploy = Join-Path $root 'deploy'
$archive = Join-Path ([IO.Path]::GetTempPath()) 'gopro-backpack-deploy.tgz'

if (-not $SkipPublish) {
    $publish = Join-Path $deploy 'publish'
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
    dotnet publish (Join-Path $root 'src/GoProBackpack') -c Release -r linux-arm64 -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
}

# One archive keeps the folder layout exact and avoids nested copies on repeat deploys.
tar -czf $archive -C $deploy .
if ($LASTEXITCODE -ne 0) { throw 'tar failed' }

scp $archive "${Target}:/tmp/gopro-backpack-deploy.tgz"
if ($LASTEXITCODE -ne 0) { throw 'scp failed' }

ssh $Target 'rm -rf ~/gopro-backpack && mkdir -p ~/gopro-backpack && tar -xzf /tmp/gopro-backpack-deploy.tgz -C ~/gopro-backpack && rm /tmp/gopro-backpack-deploy.tgz && sh ~/gopro-backpack/install.sh'
if ($LASTEXITCODE -ne 0) { throw 'install on the Pi failed' }

Remove-Item $archive
