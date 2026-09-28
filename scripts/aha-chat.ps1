# Chat aha: tell the gateway a fact, then ask for it in a second request that carries
# ONLY the new question. The answer can only come from ContextMemory's session memory.
# Works with any engine behind the gateway (llama.cpp, vLLM, Ollama, OpenAI, ...).
# Usage: .\scripts\aha-chat.ps1
$ErrorActionPreference = "Stop"

$Base = if ($env:CONTEXTMEMORY_BASE_URL) { $env:CONTEXTMEMORY_BASE_URL } else { "http://localhost:5100" }
$Key = if ($env:CONTEXTMEMORY_API_KEY) { $env:CONTEXTMEMORY_API_KEY } else { "cm_live_dev_key_change_me" }
$App = if ($env:CONTEXTMEMORY_APP_ID) { $env:CONTEXTMEMORY_APP_ID } else { "demo-dev" }
$Model = if ($env:CONTEXTMEMORY_MODEL) { $env:CONTEXTMEMORY_MODEL } else { "local-model" }
$UserId = if ($env:CONTEXTMEMORY_USER_ID) { $env:CONTEXTMEMORY_USER_ID } else { "aha-user" }
$Timeout = if ($env:CONTEXTMEMORY_TIMEOUT) { [int]$env:CONTEXTMEMORY_TIMEOUT } else { 300 }
$Session = "aha-$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"
$Fact = "postgres-staging-01"

$headers = @{
  Authorization = "Bearer $Key"
  "X-App-Id" = $App
  "X-User-Id" = $UserId
  "X-Session-Id" = $Session
}

function Send-Chat([string]$Content) {
  $body = @{ model = $Model; messages = @(@{ role = "user"; content = $Content }) } | ConvertTo-Json -Depth 5
  Invoke-RestMethod -Method Post -Uri "$Base/v1/chat/completions" -Headers $headers `
    -ContentType "application/json" -Body $body -TimeoutSec $Timeout
}

Write-Host "==> Health ($Base)"
Invoke-RestMethod -Uri "$Base/health" | ConvertTo-Json -Compress
Write-Host ""

Write-Host "==> Turn 1 (session $Session): state the fact"
$turn1 = Send-Chat "Remember this for later: our staging database host is $Fact. Reply with OK."
$turn1.choices[0].message.content
Write-Host ""

Write-Host "==> Turn 2 (same session, body contains only the new question)"
$turn2 = Send-Chat "What is our staging database host? Reply with the host name only."
$answer = $turn2.choices[0].message.content
$answer
Write-Host ""

if ($answer -match [regex]::Escape($Fact)) {
  Write-Host "AHA OK — the client sent no history; the gateway remembered '$Fact'."
  exit 0
}

Write-Host "AHA FAILED — '$Fact' not found in the turn 2 answer."
exit 1
