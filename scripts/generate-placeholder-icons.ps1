<#
.SYNOPSIS
  Generates the placeholder travel-icon assets (ADR-0016 MVP).

.DESCRIPTION
  Parses TRAVEL_ICONS from travel-icon.util.ts and writes one SVG per icon to
  public/icons/{category}/{key}.svg.

  Placeholders share one visual language: hue derived from the key, the first two
  characters of the label, and a small category emblem in the bottom-right corner.
  Map markers render the image only (no text fallback), so icons inside the same
  category must stay distinguishable from each other. Real artwork can replace the
  files in place without any code change.

  NOTE: keep this file ASCII-only - Windows PowerShell 5.1 reads .ps1 as ANSI when
  there is no BOM, which corrupts non-ASCII text and breaks parsing.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\generate-placeholder-icons.ps1
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
  [string]$Catalog,
  [string]$OutRoot
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is empty while parameter defaults are evaluated, so resolve paths here.
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
if (-not $Catalog) {
  $Catalog = Join-Path $repoRoot 'src/frontend/client/src/app/features/travel/utils/travel-icon.util.ts'
}
if (-not $OutRoot) {
  $OutRoot = Join-Path $repoRoot 'src/frontend/client/public/icons'
}

$text = Get-Content -Raw -Encoding utf8 $Catalog
$rx = [regex]"\{\s*key:\s*'([^']+)',\s*label:\s*'([^']+)',\s*category:\s*'([^']+)'\s*\}"

# Small category emblem (bottom-right corner); one simple geometric language per category.
$emblems = @{
  animal       = '<circle cx="16" cy="14" r="5"/><circle cx="32" cy="14" r="5"/><path d="M24 20c7 0 12 6 12 12 0 4-3 6-6 6-2 0-4-1-6-1s-4 1-6 1c-3 0-6-2-6-6 0-6 5-12 12-12z"/>'
  food         = '<path d="M8 24h32c0 9-7 16-16 16S8 33 8 24z"/><path d="M12 20h24l-2 3H14z"/>'
  architecture = '<path d="M12 40V18l12-8 12 8v22z"/><rect x="20" y="30" width="8" height="10"/>'
  plant        = '<path d="M24 6c14 10 14 24 0 32C10 30 10 16 24 6z"/>'
}

function Get-Hue([string]$s) {
  $h = 0
  foreach ($c in $s.ToCharArray()) { $h = ($h * 31 + [int]$c) % 360 }
  return $h
}

# Primary glyph: first two characters of the label (e.g. "Panda" style short label).
function Get-Glyph([string]$label) {
  $chars = $label.ToCharArray()
  if ($chars.Length -le 2) { return $label }
  return "$($chars[0])$($chars[1])"
}

New-Item -ItemType Directory -Force -Path $OutRoot | Out-Null
$count = 0

foreach ($m in $rx.Matches($text)) {
  $key = $m.Groups[1].Value
  $label = $m.Groups[2].Value
  $cat = $m.Groups[3].Value

  if (-not $emblems.ContainsKey($cat)) { throw "unknown category '$cat' for icon '$key'" }

  $dir = Join-Path $OutRoot $cat
  New-Item -ItemType Directory -Force -Path $dir | Out-Null

  $hue = Get-Hue $key
  $glyph = [System.Security.SecurityElement]::Escape((Get-Glyph $label))
  $fullLabel = [System.Security.SecurityElement]::Escape($label)
  $fontSize = if ($glyph.Length -ge 2) { 18 } else { 24 }
  $baseline = [math]::Round(24 + $fontSize * 0.36)
  $emblem = $emblems[$cat]

  $svg = @"
<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 48 48' role='img' aria-label='$fullLabel'>
  <circle cx='24' cy='24' r='22' fill='hsl($hue 62% 52%)'/>
  <text x='24' y='$baseline' text-anchor='middle' font-family='system-ui, -apple-system, sans-serif' font-size='$fontSize' font-weight='700' fill='rgba(255,255,255,.96)'>$glyph</text>
  <g transform='translate(31 31) scale(.32)' fill='rgba(255,255,255,.5)'>$emblem</g>
</svg>
"@

  Set-Content -Path (Join-Path $dir "$key.svg") -Value $svg -Encoding utf8 -NoNewline
  $count++
}

if ($count -eq 0) { throw "no icons parsed from $Catalog (catalog format may have changed)" }

"generated $count placeholder icons into $OutRoot"
