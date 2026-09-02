<#
.SYNOPSIS
    Enforces 100% line, branch and method coverage from a Cobertura report.

.DESCRIPTION
    Replaces the threshold enforcement that coverlet.msbuild used to perform in-build.
    coverlet.msbuild instrumented assemblies during the build and raced: whole modules
    intermittently reported 0% while every test passed, failing the gate in roughly
    20-40% of runs. Measured across 6.0.2, 6.0.4 and 10.0.1 - no version fixed it.
    See docs/adr/ADR-011-coverage-via-collector-and-script.md.

    Measurement now comes from the XPlat Code Coverage data collector
    (coverlet.collector), which instruments at runtime and does not race. The
    collector cannot enforce a threshold, so this script does it.

    Cobertura carries line-rate and branch-rate directly. It has no method-rate, so
    method coverage is derived: a method counts as covered when it has at least one
    covered line, matching what coverlet's own method threshold measured.

.PARAMETER ReportPath
    A specific coverage.cobertura.xml. When omitted, the most recently written report
    under tests/**/TestResults/ is used.

.PARAMETER Threshold
    Required coverage as a percentage. Defaults to 100.
#>
[CmdletBinding()]
param(
    [string] $ReportPath,
    [double] $Threshold = 100
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $ReportPath) {
    $searchRoot = Join-Path $repoRoot 'tests'
    $candidates = @(Get-ChildItem -Path $searchRoot -Recurse -Filter 'coverage.cobertura.xml' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending)
    if ($candidates.Count -eq 0) {
        Write-Error "No coverage.cobertura.xml found under '$searchRoot'. Run: dotnet test -c Release --collect:`"XPlat Code Coverage`" --settings tests/WindowsFileManager.Tests/coverlet.runsettings"
        exit 1
    }
    $ReportPath = $candidates[0].FullName
    if ($candidates.Count -gt 1) {
        Write-Host "note: $($candidates.Count) reports found; using the newest." -ForegroundColor DarkGray
    }
}

if (-not (Test-Path $ReportPath)) { Write-Error "Coverage report not found: $ReportPath"; exit 1 }
Write-Host "Coverage report: $ReportPath"

[xml] $report = Get-Content -LiteralPath $ReportPath -Raw
$coverage = $report.coverage
if (-not $coverage) { Write-Error "Not a Cobertura report (no <coverage> root): $ReportPath"; exit 1 }

# A collector report with no packages means nothing was instrumented. That is the
# failure the msbuild race used to produce, and it must never pass silently.
$packages = @($report.SelectNodes('//packages/package'))
if ($packages.Count -eq 0) {
    Write-Error "Report contains no packages - nothing was instrumented. Check the Include filters in coverlet.runsettings."
    exit 1
}

function Get-Rate([object] $node, [string] $attr) {
    $raw = $node.GetAttribute($attr)
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
    return [double]::Parse($raw, [System.Globalization.CultureInfo]::InvariantCulture)
}

$rows = @()
foreach ($pkg in $packages) {
    $methods = @($pkg.SelectNodes('.//methods/method'))
    $coveredMethods = @($methods | Where-Object {
        $r = Get-Rate $_ 'line-rate'
        $null -ne $r -and $r -gt 0
    })
    $methodRate = if ($methods.Count -eq 0) { 1.0 } else { $coveredMethods.Count / $methods.Count }

    $rows += [pscustomobject]@{
        Module  = $pkg.GetAttribute('name')
        Line    = [math]::Round((Get-Rate $pkg 'line-rate')   * 100, 2)
        Branch  = [math]::Round((Get-Rate $pkg 'branch-rate') * 100, 2)
        Method  = [math]::Round($methodRate * 100, 2)
        Methods = "$($coveredMethods.Count)/$($methods.Count)"
    }
}

$rows | Format-Table -AutoSize | Out-String | Write-Host

$allMethods = @($report.SelectNodes('//methods/method'))
$allCovered = @($allMethods | Where-Object { $r = Get-Rate $_ 'line-rate'; $null -ne $r -and $r -gt 0 })

$totals = [ordered]@{
    line   = (Get-Rate $coverage 'line-rate')   * 100
    branch = (Get-Rate $coverage 'branch-rate') * 100
    method = $(if ($allMethods.Count -eq 0) { 100 } else { $allCovered.Count / $allMethods.Count * 100 })
}

$failed = @()
foreach ($k in $totals.Keys) {
    $value = [math]::Round($totals[$k], 2)
    if ($value -lt $Threshold) {
        $failed += "total $k coverage is $value%, below the required $Threshold%"
    }
    Write-Host ("total {0,-6} {1,7}%" -f $k, $value)
}

if ($failed.Count -gt 0) {
    Write-Host ''
    foreach ($f in $failed) { Write-Host "FAIL: $f" -ForegroundColor Red }
    Write-Host 'A module at exactly 0% usually means instrumentation was lost, not that tests were deleted - check the module table above before writing tests.' -ForegroundColor Yellow
    exit 1
}

Write-Host ''
Write-Host "PASS: line, branch and method coverage all at or above $Threshold%." -ForegroundColor Green
exit 0
