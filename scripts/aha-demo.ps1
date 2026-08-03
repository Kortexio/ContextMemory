# Sub-5-min aha demo (no Cursor required) — save a fact, recall it.
# Usage: .\scripts\aha-demo.ps1
$ErrorActionPreference = "Stop"

$Base = if ($env:CONTEXTMEMORY_BASE_URL) { $env:CONTEXTMEMORY_BASE_URL } else { "http://localhost:5100" }
$Key = if ($env:CONTEXTMEMORY_API_KEY) { $env:CONTEXTMEMORY_API_KEY } else { "cm_live_dev_key_change_me" }
$App = if ($env:CONTEXTMEMORY_APP_ID) { $env:CONTEXTMEMORY_APP_ID } else { "demo-dev" }
$DocId = "memory:staging-db"

$headers = @{
  Authorization = "Bearer $Key"
  "X-App-Id" = $App
  "Content-Type" = "application/json"
}

Write-Host "==> Health"
Invoke-RestMethod -Uri "$Base/health" | ConvertTo-Json -Compress
Write-Host ""

Write-Host "==> Save fact (memory_save equivalent)"
$saveBody = @{
  title = "Staging DB"
  content = "# Staging DB`n`nStaging database host is **postgres-staging-01**.`n"
  sourceId = "mcp:memory"
  summary = "Staging DB is postgres-staging-01"
} | ConvertTo-Json
$save = Invoke-RestMethod -Method Put -Uri "$Base/apps/$App/wiki/documents/$DocId" -Headers $headers -Body $saveBody
$save | ConvertTo-Json -Compress
Write-Host ""

Write-Host "==> Search (new session equivalent)"
$queryBody = @{ query = "staging database"; topK = 3 } | ConvertTo-Json
$search = Invoke-RestMethod -Method Post -Uri "$Base/apps/$App/wiki/query" -Headers $headers -Body $queryBody
$search | ConvertTo-Json -Depth 6
Write-Host ""

$blob = ($search | ConvertTo-Json -Depth 6)
if ($blob -match "postgres-staging-01") {
  Write-Host "AHA OK — fact survived and was retrieved."
  exit 0
}

Write-Host "AHA FAILED — fact not found in search output."
exit 1
