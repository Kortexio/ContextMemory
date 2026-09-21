# Live stability battery — ContextMemory as CompanyBrain caller (agnostic questions).
$ErrorActionPreference = "Continue"
$base = "http://localhost:5100/v1/chat/completions"
$headers = @{
  Authorization = "Bearer cm_live_951dc02ed20314e85cb7df74"
  "X-App-Id" = "companybrain-prod-034429"
  "X-User-Id" = "e2e-stability-battery"
}
$questions = @(
  @{ Id = "wiki-paccar"; Q = "Quais sao as regras de negocio PACCAR para criar subscricao / validacao ITD?" },
  @{ Id = "wiki-en"; Q = "What are the PACCAR ITD acceptance validation rules?" },
  @{ Id = "zuora-account"; Q = "Qual o estado da conta Zuora A-001 se existir na wiki ou MCP?" },
  @{ Id = "jira-key"; Q = "Resume o ticket PAC-759 se estiver na wiki." },
  @{ Id = "chitchat"; Q = "Ola, quem es e o que podes fazer?" },
  @{ Id = "not-found"; Q = "Procura na wiki por XYZ-NONEXISTENT-99999 e diz se encontraste." }
)

$results = @()
foreach ($item in $questions) {
  $session = "bat-" + $item.Id + "-" + [guid]::NewGuid().ToString("N").Substring(0, 8)
  $h = $headers.Clone()
  $h["X-Session-Id"] = $session
  $body = @{
    model = "bonsai-27b"
    stream = $false
    messages = @(@{ role = "user"; content = $item.Q })
  } | ConvertTo-Json -Depth 6

  Write-Host "`n=== $($item.Id) session=$session ==="
  Write-Host "Q: $($item.Q)"
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $row = [ordered]@{
    Id = $item.Id
    Session = $session
    Ok = $false
    Status = 0
    Ms = 0
    Steps = -1
    BudgetRej = -1
    Awaiting = $false
    AnswerChars = 0
    AnswerPrefix = ""
    Error = ""
  }
  try {
    $resp = Invoke-WebRequest -Uri $base -Method POST -Headers $h -Body $body -ContentType "application/json; charset=utf-8" -TimeoutSec 420
    $sw.Stop()
    $row.Ok = $true
    $row.Status = [int]$resp.StatusCode
    $row.Ms = $sw.ElapsedMilliseconds
    $json = $resp.Content | ConvertFrom-Json
    $answer = $json.choices[0].message.content
    if (-not $answer) { $answer = $json.message.content }
    $row.AnswerChars = if ($answer) { $answer.Length } else { 0 }
    $row.AnswerPrefix = if ($answer) { $answer.Substring(0, [Math]::Min(180, $answer.Length)).Replace("`n", " ") } else { "" }
    if ($json.context_memory.agentic) {
      $ag = $json.context_memory.agentic
      if ($ag.steps) { $row.Steps = @($ag.steps).Count }
      $row.Awaiting = [bool]$ag.awaitingConfirmation
      if ($ag.steps) {
        $row.BudgetRej = @($ag.steps | Where-Object { $_.summary -match "Duplicate|budget" }).Count
      }
    }
    Write-Host "OK $($row.Ms)ms steps=$($row.Steps) budgetRej=$($row.BudgetRej) awaiting=$($row.Awaiting) chars=$($row.AnswerChars)"
    Write-Host "A: $($row.AnswerPrefix)"
  }
  catch {
    $sw.Stop()
    $row.Ms = $sw.ElapsedMilliseconds
    $row.Error = $_.Exception.Message
    if ($_.Exception.Response) {
      $row.Status = [int]$_.Exception.Response.StatusCode
    }
    Write-Host "FAIL $($row.Ms)ms status=$($row.Status) $($row.Error)"
  }
  $results += [pscustomobject]$row
}

Write-Host "`n======== SUMMARY ========"
$results | Format-Table Id, Ok, Status, Ms, Steps, BudgetRej, Awaiting, AnswerChars -AutoSize
$pass = @($results | Where-Object { $_.Ok -and -not $_.Awaiting -and $_.Steps -lt 20 }).Count
$fail = $results.Count - $pass
Write-Host "Stable completions (HTTP OK, no HITL, steps<20): $pass / $($results.Count)"
$results | ConvertTo-Json -Depth 4 | Set-Content -Path "$env:TEMP\cm-stability-battery.json" -Encoding utf8
Write-Host "Wrote $env:TEMP\cm-stability-battery.json"
