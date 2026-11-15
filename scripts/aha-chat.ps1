# Chat aha: tell the gateway a fact, then ask for it in a second request that carries
# ONLY the new question. The answer can only come from ContextMemory's session memory.
# Works with any engine behind the gateway (llama.cpp, vLLM, Ollama, OpenAI, ...).
# Usage: .\scripts\aha-chat.ps1
# Optional: $env:CONTEXTMEMORY_STREAM = "1" — use stream:true and assemble SSE content.
$ErrorActionPreference = "Stop"

$Base = if ($env:CONTEXTMEMORY_BASE_URL) { $env:CONTEXTMEMORY_BASE_URL } else { "http://localhost:5100" }
$Key = if ($env:CONTEXTMEMORY_API_KEY) { $env:CONTEXTMEMORY_API_KEY } else { "cm_live_dev_key_change_me" }
$App = if ($env:CONTEXTMEMORY_APP_ID) { $env:CONTEXTMEMORY_APP_ID } else { "demo-dev" }
$Model = if ($env:CONTEXTMEMORY_MODEL) { $env:CONTEXTMEMORY_MODEL } else { "local-model" }
$UserId = if ($env:CONTEXTMEMORY_USER_ID) { $env:CONTEXTMEMORY_USER_ID } else { "aha-user" }
$Timeout = if ($env:CONTEXTMEMORY_TIMEOUT) { [int]$env:CONTEXTMEMORY_TIMEOUT } else { 300 }
$Stream = ($env:CONTEXTMEMORY_STREAM -eq "1" -or $env:CONTEXTMEMORY_STREAM -eq "true")
$Session = "aha-$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"
$Fact = "postgres-staging-01"

$headers = @{
  Authorization = "Bearer $Key"
  "X-App-Id" = $App
  "X-User-Id" = $UserId
  "X-Session-Id" = $Session
}

function Send-ChatNonStream([string]$Content) {
  $body = @{ model = $Model; messages = @(@{ role = "user"; content = $Content }) } | ConvertTo-Json -Depth 5
  Invoke-RestMethod -Method Post -Uri "$Base/v1/chat/completions" -Headers $headers `
    -ContentType "application/json" -Body $body -TimeoutSec $Timeout
}

function Send-ChatStream([string]$Content) {
  $body = @{ model = $Model; stream = $true; messages = @(@{ role = "user"; content = $Content }) } | ConvertTo-Json -Depth 5
  $req = [System.Net.HttpWebRequest]::Create("$Base/v1/chat/completions")
  $req.Method = "POST"
  $req.ContentType = "application/json"
  $req.Timeout = $Timeout * 1000
  $req.ReadWriteTimeout = $Timeout * 1000
  foreach ($k in $headers.Keys) { $req.Headers[$k] = $headers[$k] }
  $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
  $req.ContentLength = $bytes.Length
  $stream = $req.GetRequestStream()
  $stream.Write($bytes, 0, $bytes.Length)
  $stream.Close()
  $resp = $req.GetResponse()
  $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
  $sb = New-Object System.Text.StringBuilder
  while ($null -ne ($line = $reader.ReadLine())) {
    if (-not $line.StartsWith("data:")) { continue }
    $data = $line.Substring(5).Trim()
    if (-not $data -or $data -eq "[DONE]") { continue }
    try {
      $obj = $data | ConvertFrom-Json
      foreach ($ch in @($obj.choices)) {
        if ($ch.delta -and $ch.delta.content) { [void]$sb.Append([string]$ch.delta.content) }
        if ($ch.message -and $ch.message.content) { [void]$sb.Append([string]$ch.message.content) }
      }
    } catch { }
  }
  $reader.Close()
  $resp.Close()
  return $sb.ToString()
}

Write-Host "==> Health ($Base)"
Invoke-RestMethod -Uri "$Base/health" | ConvertTo-Json -Compress
Write-Host ""

if ($Stream) { Write-Host "==> Streaming mode (CONTEXTMEMORY_STREAM=1)" }

Write-Host "==> Turn 1 (session $Session): state the fact"
if ($Stream) {
  $turn1 = Send-ChatStream "Remember this for later: our staging database host is $Fact. Reply with OK."
  $turn1
} else {
  $turn1 = Send-ChatNonStream "Remember this for later: our staging database host is $Fact. Reply with OK."
  $turn1.choices[0].message.content
}
Write-Host ""

Write-Host "==> Turn 2 (same session, body contains only the new question)"
if ($Stream) {
  $answer = Send-ChatStream "What is our staging database host? Reply with the host name only."
} else {
  $turn2 = Send-ChatNonStream "What is our staging database host? Reply with the host name only."
  $answer = $turn2.choices[0].message.content
}
$answer
Write-Host ""

if ($answer -match [regex]::Escape($Fact)) {
  Write-Host "AHA OK — the client sent no history; the gateway remembered '$Fact'."
  exit 0
}

Write-Host "AHA FAILED — '$Fact' not found in the turn 2 answer."
exit 1
