param($item, $session, $round, $mode, $baseUrl, $apiKey, $appId, $userId, $timeoutSec, $work)
$ErrorActionPreference = "Continue"
$safeId = ($item.id -replace "[^a-zA-Z0-9_-]", "_")
$tag = "{0}-r{1}-{2}" -f $mode.ToLower(), $round, $safeId
$reqFile = Join-Path $work ($tag + "-req.json")
$resFile = Join-Path $work ($tag + "-res.json")
$metaFile = Join-Path $work ($tag + "-meta.txt")
$payload = @{
  model = "bonsai-27b"
  stream = $false
  messages = @(@{ role = "user"; content = [string]$item.q })
} | ConvertTo-Json -Depth 6 -Compress
[System.IO.File]::WriteAllText($reqFile, $payload, [System.Text.UTF8Encoding]::new($false))
$row = [ordered]@{
  Round = $round
  Mode = $mode
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
    "-sS", "-X", "POST", $baseUrl,
    "-H", ("Authorization: Bearer " + $apiKey),
    "-H", ("X-App-Id: " + $appId),
    "-H", ("X-User-Id: " + $userId),
    "-H", ("X-Session-Id: " + $session),
    "-H", "Content-Type: application/json",
    "--data-binary", ("@" + $reqFile),
    "-o", $resFile,
    "-w", "%{http_code}",
    "--max-time", "$timeoutSec"
  )
  $httpCode = & curl.exe @curlArgs 2>$metaFile
  $codeNum = $httpCode -as [int]
  if ($codeNum -eq 429 -or $codeNum -eq 503) {
    Start-Sleep -Seconds (5 * $attempt)
    continue
  }
  break
}
$sw.Stop()
$row.Ms = [int]$sw.ElapsedMilliseconds
if (-not ($httpCode -as [int])) {
  $row.Error = (Get-Content $metaFile -Raw -ErrorAction SilentlyContinue)
  return [pscustomobject]$row
}
$row.Status = [int]$httpCode
if ($row.Status -lt 200 -or $row.Status -ge 300) {
  $row.Error = "HTTP $($row.Status)"
  if (Test-Path $resFile) {
    $raw = Get-Content $resFile -Raw -ErrorAction SilentlyContinue
    if ($raw) { $row.AnswerPrefix = $raw.Substring(0, [Math]::Min(200, $raw.Length)) }
  }
  return [pscustomobject]$row
}
$row.Ok = $true
try {
  $json = Get-Content $resFile -Raw -Encoding UTF8 | ConvertFrom-Json
  $answer = $null
  if ($null -ne $json.choices -and @($json.choices).Count -gt 0) {
    $choice = @($json.choices)[0]
    if ($choice.message -and $choice.message.content) { $answer = [string]$choice.message.content }
  }
  elseif ($json.message -and $json.message.content) {
    $answer = [string]$json.message.content
  }
  $row.AnswerChars = if ($answer) { $answer.Length } else { 0 }
  if ($answer) {
    $flat = ($answer -replace "\s+", " ").Trim()
    $row.AnswerPrefix = $flat.Substring(0, [Math]::Min(160, $flat.Length))
  }
  if ($json.context_memory -and $json.context_memory.agentic) {
    $ag = $json.context_memory.agentic
    if ($ag.PSObject.Properties.Name -contains "awaitingConfirmation") {
      $row.Awaiting = [bool]$ag.awaitingConfirmation
    }
    if ($ag.PSObject.Properties.Name -contains "timedOut") {
      $row.TimedOut = [bool]$ag.timedOut
    }
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
}
catch {
  $row.Error = $_.Exception.Message
}
return [pscustomobject]$row
