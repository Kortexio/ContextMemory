# Live stability battery via curl.exe - sends up to -Parallel requests at a time.
# Distinct sessions by default (SameSession forces Parallel=1).
param(
  [ValidateSet("Distinct", "SameSession")]
  [string]$Mode = "Distinct",
  [Alias("Rounds")]
  [int]$RoundCount = 1,
  [ValidateRange(1, 8)]
  [int]$Parallel = 2,
  [switch]$RequireAllStable,
  [string]$CasesPath = "",
  [string]$OutPrefix = "cm-stability-battery"
)

Write-Host "BATTERY START"
$ErrorActionPreference = "Stop"
$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
Write-Host ("scriptRoot={0}" -f $scriptRoot)

$base = "http://localhost:5100/v1/chat/completions"
$apiKey = "cm_live_951dc02ed20314e85cb7df74"
$appId = "companybrain-prod-034429"
$userId = "e2e-stability-full"
$timeoutSec = 420

if (-not $CasesPath) { $CasesPath = Join-Path $scriptRoot "stability-battery-cases.json" }
Write-Host ("CasesPath={0}" -f $CasesPath)
if (-not (Test-Path -LiteralPath $CasesPath)) { throw "Cases file not found: $CasesPath" }

$parsed = Get-Content -LiteralPath $CasesPath -Raw -Encoding UTF8 | ConvertFrom-Json
$cases = @($parsed | ForEach-Object { $_ })
Write-Host ("cases.Length={0}" -f $cases.Length)
if ($cases.Length -lt 1) { throw "No cases loaded from $CasesPath" }

$work = Join-Path $env:TEMP "cm-battery-work"
New-Item -ItemType Directory -Force -Path $work | Out-Null
$invokeCasePath = Join-Path $scriptRoot "stability-battery-invoke-case.ps1"
if (-not (Test-Path -LiteralPath $invokeCasePath)) { throw "Invoke script not found: $invokeCasePath" }

if ($Mode -eq "SameSession" -and $Parallel -gt 1) {
  Write-Host "SameSession cannot safely share one session concurrently - forcing Parallel=1."
  $Parallel = 1
}

Write-Host ("Loaded {0} cases | Mode={1} Parallel={2} Rounds={3}" -f $cases.Length, $Mode, $Parallel, $RoundCount)

$allResults = New-Object System.Collections.Generic.List[object]

for ($roundIdx = 1; $roundIdx -le $RoundCount; $roundIdx++) {
  Write-Host ""
  Write-Host ("#" * 72)
  Write-Host ("ROUND {0}/{1} MODE={2} PARALLEL={3}" -f $roundIdx, $RoundCount, $Mode, $Parallel)
  Write-Host ("#" * 72)

  $sharedSession = "same-" + [guid]::NewGuid().ToString("N").Substring(0, 12)
  $roundResults = New-Object System.Collections.Generic.List[object]

  for ($i = 0; $i -lt $cases.Length; $i += $Parallel) {
    $end = [Math]::Min($i + $Parallel - 1, $cases.Length - 1)
    Write-Host ""
    Write-Host ("-- batch {0}-{1} / {2} --" -f ($i + 1), ($end + 1), $cases.Length)

    $runspaces = @()
    $pool = [runspacefactory]::CreateRunspacePool(1, $Parallel)
    $pool.Open()

    for ($j = $i; $j -le $end; $j++) {
      $c = $cases[$j]
      if ($Mode -eq "SameSession") {
        $session = $sharedSession
      } else {
        $session = "full-" + $c.id + "-" + [guid]::NewGuid().ToString("N").Substring(0, 8)
      }
      Write-Host ("[R{0}/{1}][{2}] {3}  session={4}" -f $roundIdx, $Mode, $c.cat, $c.id, $session)
      Write-Host ("  Q: {0}" -f $c.q)

      $ps = [powershell]::Create()
      $ps.RunspacePool = $pool
      [void]$ps.AddCommand($invokeCasePath).AddParameters(@{
        item = $c
        session = $session
        round = $roundIdx
        mode = $Mode
        baseUrl = $base
        apiKey = $apiKey
        appId = $appId
        userId = $userId
        timeoutSec = $timeoutSec
        work = $work
      })
      $runspaces += [pscustomobject]@{ Pipe = $ps; Handle = $ps.BeginInvoke(); Id = $c.id }
    }

    foreach ($rs in $runspaces) {
      $rows = $rs.Pipe.EndInvoke($rs.Handle)
      $rs.Pipe.Dispose()
      foreach ($row in @($rows)) {
        if (-not $row) { continue }
        [void]$roundResults.Add($row)
        if ($row.Ok) {
          Write-Host ("-> [{0}] HTTP {1} {2}ms steps={3} ok={4} fail={5} budgetRej={6} awaiting={7} tools=[{8}] stable={9}" -f `
            $row.Id, $row.Status, $row.Ms, $row.Steps, $row.OkSteps, $row.FailSteps, $row.BudgetRej, $row.Awaiting, $row.Tools, $row.Stable)
          Write-Host ("   A: {0}" -f $row.AnswerPrefix)
        } elseif ($row.Error) {
          Write-Host ("-> [{0}] FAIL {1}ms err={2}" -f $row.Id, $row.Ms, $row.Error)
        } else {
          Write-Host ("-> [{0}] FAIL {1}ms status={2}" -f $row.Id, $row.Ms, $row.Status)
        }
      }
    }
    $pool.Close()
    $pool.Dispose()

    if ($end -lt $cases.Length - 1) { Start-Sleep -Seconds 2 }
  }

  foreach ($row in $roundResults) { [void]$allResults.Add($row) }

  $okN = @($roundResults | Where-Object { $_.Ok }).Count
  $stableN = @($roundResults | Where-Object { $_.Stable }).Count
  $total = $roundResults.Count
  Write-Host ""
  Write-Host ("ROUND {0} SUMMARY ({1}): ok={2}/{3} stable={4}/{3}" -f $roundIdx, $Mode, $okN, $total, $stableN)
  $roundResults | Group-Object Cat | ForEach-Object {
    $s = @($_.Group | Where-Object { $_.Stable }).Count
    $o = @($_.Group | Where-Object { $_.Ok }).Count
    Write-Host ("  {0,-12} ok={1}/{2} stable={3}/{2}" -f $_.Name, $o, $_.Count, $s)
  }
}

Write-Host ""
Write-Host ("#" * 72)
Write-Host "GRAND SUMMARY"
$allResults | Format-Table Round, Mode, Id, Cat, Ok, Stable, Status, Ms, Steps, BudgetRej, Tools -AutoSize
$stableAll = @($allResults | Where-Object { $_.Stable }).Count
$okAll = @($allResults | Where-Object { $_.Ok }).Count
Write-Host ("TOTAL: ok={0}/{1} stable={2}/{1} mode={3} parallel={4} rounds={5}" -f $okAll, $allResults.Count, $stableAll, $Mode, $Parallel, $RoundCount)

$out = Join-Path $env:TEMP ("{0}-{1}.json" -f $OutPrefix, $Mode.ToLower())
@($allResults) | ConvertTo-Json -Depth 5 | Set-Content -Path $out -Encoding utf8
Write-Host "Wrote $out"

if ($RequireAllStable) {
  if ($stableAll -ne $allResults.Count -or $okAll -ne $allResults.Count) {
    Write-Host "REQUIRE 100% STABLE FAILED"
    exit 1
  }
  Write-Host "REQUIRE 100% STABLE: PASS"
  exit 0
}
$loopSuspect = @($allResults | Where-Object { $_.BudgetRej -gt 3 -or $_.Steps -ge 20 -or $_.Awaiting }).Count
if ($allResults.Count -gt 0 -and ($loopSuspect -gt 2 -or $okAll -lt [Math]::Ceiling($allResults.Count * 0.6))) { exit 2 }
exit 0
