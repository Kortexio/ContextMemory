# Chat aha: tell the gateway a fact, then ask for it in a second request that carries
# ONLY the new question. The answer can only come from ContextMemory's session memory.
# Turn 3 always uses stream:true and asserts the fact in concatenated SSE delta.content.
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
$Ask = "What is our staging database host? Reply with the host name only."

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

Write-Host "==> Turn 1 (session $Session): state the fact"
$turn1 = Send-ChatNonStream "Remember this for later: our staging database host is $Fact. Reply with OK."
$turn1.choices[0].message.content
Write-Host ""

Write-Host "==> Turn 2 (same session, body contains only the new question)"
$turn2 = Send-ChatNonStream $Ask
$answer2 = $turn2.choices[0].message.content
$answer2
Write-Host ""

if ($answer2 -notmatch [regex]::Escape($Fact)) {
  Write-Host "AHA FAILED — '$Fact' not found in the turn 2 answer."
  exit 1
}
Write-Host "AHA OK (non-stream) — gateway remembered '$Fact'."

Write-Host "==> Turn 3 (stream:true, same question)"
$answer3 = Send-ChatStream $Ask
$answer3
Write-Host ""

if ($answer3 -notmatch [regex]::Escape($Fact)) {
  Write-Host "AHA FAILED — '$Fact' not found in streamed turn 3 SSE content."
  exit 1
}

Write-Host "AHA OK (stream) — SSE delta.content contained '$Fact'."
exit 0
