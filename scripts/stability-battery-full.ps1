# Live stability battery via curl.exe.
# Modes:
#   Distinct    — each query gets a fresh X-Session-Id (default)
#   SameSession — all queries share one X-Session-Id (multi-turn)
# Usage:
#   .\stability-battery-full.ps1 -Mode Distinct -Rounds 2 -RequireAllStable
#   .\stability-battery-full.ps1 -Mode SameSession -Rounds 2 -RequireAllStable
param(
  [ValidateSet("Distinct", "SameSession")]
  [string]$Mode = "Distinct",
  [int]$Rounds = 1,
  [switch]$RequireAllStable,
  [string]$CasesPath = "",
  [string]$OutPrefix = "cm-stability-battery"
)

$ErrorActionPreference = "Continue"
$base = "http://localhost:5100/v1/chat/completions"
$apiKey = "cm_live_951dc02ed20314e85cb7df74"
$appId = "companybrain-prod-034429"
$userId = "e2e-stability-full"
$timeoutSec = 420
if (-not $CasesPath) { $CasesPath = Join-Path $PSScriptRoot "stability-battery-cases.json" }
$cases = Get-Content $CasesPath -Raw -Encoding UTF8 | ConvertFrom-Json
$work = Join-Path $env:TEMP "cm-battery-work"
New-Item -ItemType Directory -Force -Path $work | Out-Null

function Invoke-Case($item, $session, $round) {
  $safeId = ($item.id -replace "[^a-zA-Z0-9_-]", "_")
  $tag = "{0}-r{1}-{2}" -f $Mode.ToLower(), $round, $safeId
  $reqFile = Join-Path $work ($tag + "-req.json")
  $resFile = Join-Path $work ($tag + "-res.json")
  $metaFile = Join-Path $work ($tag + "-meta.txt")

  $payload = @{
    model = "bonsai-27b"
    stream = $false
    messages = @(@{ role = "user"; content = [string]$item.q })
  } | ConvertTo-Json -Depth 6 -Compress
  [System.IO.File]::WriteAllText($reqFile, $payload, [System.Text.UTF8Encoding]::new($false))

  Write-Host ""
  Write-Host ("=" * 72)
  Write-Host ("[R{0}/{1}][{2}] {3}" -f $round, $Mode, $item.cat, $item.id)
  Write-Host ("Session: {0}" -f $session)
  Write-Host ("Q: {0}" -f $item.q)

  $row = [ordered]@{
    Round = $round
    Mode = $Mode
    Id = $item.id
    Cat = $item.cat
    Session = $session
    Ok = $false
    Stable = $false
    Status = 0
    Ms = 0
    Steps = 0
    OkSteps = 0
    FailSteps = 0
    BudgetRej = 0
    Tools = ""
    Awaiting = $false
    TimedOut = $false
    AnswerChars = 0
    AnswerPrefix = ""
    Error = ""
  }

  $sw = [Diagnostics.Stopwatch]::StartNew()
  $httpCode = $null
  for ($attempt = 1; $attempt -le 4; $attempt++) {
    $curlArgs = @(
      "-sS", "-X", "POST", $base,
      "-H", "Authorization: Bearer $apiKey",
      "-H", "X-App-Id: $appId",
      "-H", "X-User-Id: $userId",
      "-H", "X-Session-Id: $session",
      "-H", "Content-Type: application/json",
      "--data-binary", "@$reqFile",
      "-o", $resFile,
      "-w", "%{http_code}",
      "--max-time", "$timeoutSec"
    )
    $httpCode = & curl.exe @curlArgs 2>$metaFile
    if (($httpCode -as [int]) -eq 429 -or ($httpCode -as [int]) -eq 503) {
      Start-Sleep -Seconds (5 * $attempt)
      continue
    }
    break
  }
  $sw.Stop()
  $row.Ms = [int]$sw.ElapsedMilliseconds

  if (-not ($httpCode -as [int])) {
    $row.Error = (Get-Content $metaFile -Raw -ErrorAction SilentlyContinue)
    Write-Host ("-> FAIL {0}ms curlErr={1}" -f $row.Ms, $row.Error)
    return [pscustomobject]$row
  }

  $row.Status = [int]$httpCode
  if ($row.Status -lt 200 -or $row.Status -ge 300) {
    $row.Error = "HTTP $($row.Status)"
    if (Test-Path $resFile) {
      $raw = Get-Content $resFile -Raw -ErrorAction SilentlyContinue
      if ($raw) { $row.AnswerPrefix = $raw.Substring(0, [Math]::Min(200, $raw.Length)) }
    }
    Write-Host ("-> FAIL {0}ms status={1}" -f $row.Ms, $row.Status)
    return [pscustomobject]$row
  }

  $row.Ok = $true
  try {
    $json = Get-Content $resFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $answer = $null
    if ($null -ne $json.choices -and @($json.choices).Count -gt 0) {
      $choice = @($json.choices)[0]
      if ($choice.message -and $choice.message.content) { $answer = [string]$choice.message.content }
    } elseif ($json.message -and $json.message.content) {
      $answer = [string]$json.message.content
    }
    $row.AnswerChars = if ($answer) { $answer.Length } else { 0 }
    if ($answer) {
      $flat = ($answer -replace "\s+", " ").Trim()
      $row.AnswerPrefix = $flat.Substring(0, [Math]::Min(160, $flat.Length))
    }

    if ($json.context_memory -and $json.context_memory.agentic) {
      $ag = $json.context_memory.agentic
      if ($ag.PSObject.Properties.Name -contains "awaitingConfirmation") { $row.Awaiting = [bool]$ag.awaitingConfirmation }
      if ($ag.PSObject.Properties.Name -contains "timedOut") { $row.TimedOut = [bool]$ag.timedOut }
      $steps = @()
      if ($ag.steps) { $steps = @($ag.steps) }
      $row.Steps = $steps.Count
      $row.OkSteps = @($steps | Where-Object { $_.success -eq $true }).Count
      $row.FailSteps = @($steps | Where-Object { $_.success -eq $false }).Count
      $row.BudgetRej = @($steps | Where-Object {
        ($_.summary -match "Duplicate|budget") -or ($_.output -match "budget exhausted|esgotado")
      }).Count
      $names = @($steps | ForEach-Object { $_.toolName } | Where-Object { $_ } | Select-Object -Unique)
      $row.Tools = [string]::Join(",", $names)
    }

    $row.Stable = ($row.Ok -and -not $row.Awaiting -and -not $row.TimedOut -and $row.Steps -lt 20 -and $row.BudgetRej -le 3)
    Write-Host ("-> HTTP {0} {1}ms steps={2} ok={3} fail={4} budgetRej={5} awaiting={6} tools=[{7}] stable={8}" -f `
      $row.Status, $row.Ms, $row.Steps, $row.OkSteps, $row.FailSteps, $row.BudgetRej, $row.Awaiting, $row.Tools, $row.Stable)
    Write-Host ("A: {0}" -f $row.AnswerPrefix)
  }
  catch {
    $row.Error = $_.Exception.Message
    Write-Host ("-> PARSE FAIL {0}" -f $row.Error)
  }

  return [pscustomobject]$row
}

$allResults = @()
$failedRounds = @()

for ($r = 1; $r -le $Rounds; $r++) {
  Write-Host ""
  Write-Host ("#" * 72)
  Write-Host ("ROUND {0}/{1} MODE={2} cases={3}" -f $r, $Rounds, $Mode, $cases.Count)
  Write-Host ("#" * 72)

  $sharedSession = "same-" + [guid]::NewGuid().ToString("N").Substring(0, 12)
  $roundResults = @()

  foreach ($c in $cases) {
    $session = if ($Mode -eq "SameSession") {
      $sharedSession
    } else {
      "full-" + $c.id + "-" + [guid]::NewGuid().ToString("N").Substring(0, 8)
    }
    $roundResults += Invoke-Case $c $session $r
    Start-Sleep -Seconds 3
  }

  $allResults += $roundResults

  $okN = @($roundResults | Where-Object Ok).Count
  $stableN = @($roundResults | Where-Object Stable).Count
  $total = $roundResults.Count
  Write-Host ""
  Write-Host ("ROUND {0} SUMMARY ({1}): ok={2}/{3} stable={4}/{3}" -f $r, $Mode, $okN, $total, $stableN)
  $roundResults | Group-Object Cat | ForEach-Object {
    $s = @($_.Group | Where-Object Stable).Count
    $o = @($_.Group | Where-Object Ok).Count
    Write-Host ("  {0,-12} ok={1}/{2} stable={3}/{2}" -f $_.Name, $o, $_.Count, $s)
  }

  if ($RequireAllStable -and ($stableN -ne $total -or $okN -ne $total)) {
    $failedRounds += $r
  }
}

Write-Host ""
Write-Host ("#" * 72)
Write-Host "GRAND SUMMARY"
$allResults | Format-Table Round, Mode, Id, Cat, Ok, Stable, Status, Ms, Steps, BudgetRej, Tools -AutoSize

$stableAll = @($allResults | Where-Object Stable).Count
$okAll = @($allResults | Where-Object Ok).Count
Write-Host ("TOTAL: ok={0}/{1} stable={2}/{1} mode={3} rounds={4}" -f $okAll, $allResults.Count, $stableAll, $Mode, $Rounds)

$out = Join-Path $env:TEMP ("{0}-{1}.json" -f $OutPrefix, $Mode.ToLower())
$allResults | ConvertTo-Json -Depth 5 | Set-Content -Path $out -Encoding utf8
Write-Host "Wrote $out"

if ($RequireAllStable) {
  if ($failedRounds.Count -gt 0 -or $stableAll -ne $allResults.Count) {
    Write-Host ("REQUIRE 100% STABLE FAILED - bad rounds: {0}" -f ($failedRounds -join ","))
    exit 1
  }
  Write-Host "REQUIRE 100% STABLE: PASS"
  exit 0
}

$loopSuspect = @($allResults | Where-Object { $_.BudgetRej -gt 3 -or $_.Steps -ge 20 -or $_.Awaiting }).Count
if ($loopSuspect -gt 2 -or $okAll -lt [Math]::Ceiling($allResults.Count * 0.6)) { exit 2 }
exit 0
