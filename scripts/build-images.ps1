#Requires -Version 5.1
<#
.SYNOPSIS
    Build production Docker images for travel-map with auto-incrementing version tags.
.DESCRIPTION
    Reads .image-version, increments the patch component, builds the requested
    image(s), and tags them with the new version. Also tags with the current
    git short SHA for traceability. No 'latest' tag is produced.
.PARAMETER Component
    Which image(s) to build: all (default), client, gateway, user-service, travel-history.
.EXAMPLE
    .\scripts\build-images.ps1
    .\scripts\build-images.ps1 -Component client
#>
param(
    [ValidateSet('all', 'client', 'gateway', 'user-service', 'travel-history')]
    [string]$Component = 'all'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $repoRoot

# --- read and bump version ---
$versionFile = Join-Path $repoRoot '.image-version'
$version = [System.IO.File]::ReadAllText($versionFile).Trim()
if (-not $version -match '^\d+\.\d+\.\d+$') {
    throw ".image-version must contain a value like 0.1.0; found: $version"
}
$parts = $version.Split('.')
[int]$major = $parts[0]
[int]$minor = $parts[1]
[int]$patch = [int]$parts[2] + 1
if ($patch -gt 9999) {
    $patch = 0
    $minor += 1
}
$newVersion = "$major.$minor.$patch"
[System.IO.File]::WriteAllText($versionFile, "$newVersion`n")

# --- git SHA for secondary tag ---
$sha = (git rev-parse --short HEAD 2>$null)
if (-not $sha) { $sha = 'unknown' }

Write-Output "Building images version: $newVersion (sha: $sha)"

# --- image definitions ---
$images = @(
    @{ name = 'client';          context = 'src/frontend/client'; dockerfile = $null },
    @{ name = 'gateway';         context = 'src/gateway';         dockerfile = $null },
    @{ name = 'user-service';    context = 'src/backend';         dockerfile = 'src/backend/services/user-service/Dockerfile' },
    @{ name = 'travel-history';  context = 'src/backend';         dockerfile = 'src/backend/services/travel-history/Dockerfile' }
)

if ($Component -ne 'all') {
    $images = $images | Where-Object { $_.name -eq $Component }
}

foreach ($img in $images) {
    $imageName = "travelmap-$($img.name)"
    $versionTag = "$imageName`:$newVersion"
    $shaTag = "$imageName`:$sha"

    $args = @('build', '-t', $versionTag)
    if ($img.dockerfile) {
        $args += @('-f', $img.dockerfile)
    }
    $args += $img.context

    Write-Output ""
    Write-Output ">>> docker $([string]::Join(' ', $args))"
    & docker @args
    if ($LASTEXITCODE -ne 0) {
        throw "Docker build failed for $imageName"
    }

    # tag with SHA as well, no latest
    docker tag $versionTag $shaTag | Out-Null
    Write-Output "Tagged: $versionTag, $shaTag"
}

Write-Output ""
Write-Output "All requested images built successfully. Version bumped to $newVersion."
