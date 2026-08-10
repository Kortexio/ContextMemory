# Multi-model agentic smoke (canceled Zuora account)

# Usage:
#   $env:CM_API_KEY = "..."   # optional; otherwise fetches via master key
#   $env:CM_APP_ID = "companybrain-prod-034429"
#   $env:CM_BASE = "http://localhost:5100"
#   ./scripts/smoke-multi-model.ps1

$ErrorActionPreference = "Stop"
$base = if ($env:CM_BASE) { $env:CM_BASE.TrimEnd('/') } else { "http://localhost:5100" }
$appId = if ($env:CM_APP_ID) { $env:CM_APP_ID } else { "companybrain-prod-034429" }
$master = if ($env:CM_MASTER_KEY) { $env:CM_MASTER_KEY } else { "cm_master_local_dev_change_me" }

if ($env:CM_API_KEY) {
    $key = $env:CM_API_KEY
} else {
    $creds = (curl.exe -s -H "Authorization: Bearer $master" "$base/admin/apps/$appId/credentials") | ConvertFrom-Json
    $key = $creds.ApiKey
}

$cfg = (curl.exe -s -H "Authorization: Bearer $key" -H "X-App-Id: $appId" -H "X-User-Id: smoke" "$base/apps/$appId/config") | ConvertFrom-Json
Write-Host "model=$($cfg.llmModel) backend=$($cfg.llmBackend) numCtx=$($cfg.llmOptions.numCtx) harness=$($cfg.agentic.harnessMode)"

$body = '{"messages":[{"role":"user","content":"Find one canceled Zuora account via MCP query_objects. Reply with ONLY accountNumber and status."}],"stream":false}'
$tmp = Join-Path $env:TEMP "cm-smoke-body.json"
$out = Join-Path $env:TEMP "cm-smoke-out.json"
[System.IO.File]::WriteAllText($tmp, $body)

Write-Host "POST $base/v1/chat/completions ..."
curl.exe -s -m 420 -o $out -w "HTTP %{http_code} time %{time_total}`n" `
  -H "Authorization: Bearer $key" `
  -H "Content-Type: application/json" `
  -H "X-App-Id: $appId" `
  -H "X-User-Id: multi-model-smoke" `
  --data-binary "@$tmp" `
  "$base/v1/chat/completions"

$raw = Get-Content $out -Raw
$r = $raw | ConvertFrom-Json
if ($r.error) {
    $msg = [string]$r.error.message
    if ($msg -match "No user query found|raise_exception|Jinja") {
        Write-Error "FAIL: chat template Jinja error (patch Bonsai/Qwen TEMPLATE). $msg"
    }
    if ($msg -match "exceeds the available context|exceed_context_size") {
        Write-Error "FAIL: context too small (set OLLAMA_CONTEXT_LENGTH or numCtx + ollama-native). $msg"
    }
    Write-Error "FAIL: $($r.error.message)"
}

$steps = @($r.context_memory.agentic.steps)
$label = $r.context_memory.agentic.label
$answer = $r.choices[0].message.content
$discovery = $r.context_memory.discovery

Write-Host "agentic=$label tools=$($steps.Count) harness=$($discovery.harness_mode) profile=$($discovery.resolved_prompt_profile) prose=$($discovery.promoted_prose_tool_calls) repair=$($discovery.schema_repair_level)"
if ($answer) { Write-Host "answer=$($answer.Substring(0, [Math]::Min(300, $answer.Length)))" }

$successTools = @($steps | Where-Object { $_.success -eq $true })
if ($steps.Count -lt 1) {
    Write-Error "FAIL: expected >=1 tool step (got 0). Model may have invented or skipped MCP."
}

# Zuora sandbox SPI 500 is transient — warn but do not hard-fail if tools ran.
$anyOk = $successTools.Count -gt 0
if (-not $anyOk) {
    Write-Warning "No successful tool step (possible Zuora 500). tools=$($steps.Count)"
    exit 2
}

Write-Host "PASS: tools=$($steps.Count) success=$($successTools.Count)"
exit 0
