# Run ContextMemory from GHCR (or build locally with -Build).
# Usage:
#   .\scripts\docker-run.ps1
#   .\scripts\docker-run.ps1 -WithAdmin
#   .\scripts\docker-run.ps1 -Build -WithAdmin
param(
    [switch]$WithAdmin,
    [switch]$Build,
    [int]$ApiPort = 5100,
    [int]$AdminPort = 5200,
    [string]$OllamaEndpoint = "http://host.docker.internal:11434",
    [string]$DefaultLlmModel = "qwen3.5:9b",
    [string]$MasterKey = "cm_master_dev_key_change_me",
    [string]$DemoAppApiKey = "cm_live_dev_key_change_me",
    [string]$Tag = "latest",
    [string]$Network = "contextmemory-net"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

$ImageApi = "ghcr.io/kortexio/contextmemory:$Tag"
$ImageAdmin = "ghcr.io/kortexio/contextmemory-admin:$Tag"

if ($Build) {
    $ImageApi = "contextmemory-api:local"
    $ImageAdmin = "contextmemory-admin:local"
    Write-Host "==> Building API image ($ImageApi)"
    docker build -t $ImageApi -f Dockerfile .
    if ($WithAdmin) {
        Write-Host "==> Building Admin image ($ImageAdmin)"
        docker build -t $ImageAdmin -f Dockerfile.admin .
    }
}
else {
    Write-Host "==> Pulling $ImageApi"
    docker pull $ImageApi
    if ($WithAdmin) {
        Write-Host "==> Pulling $ImageAdmin"
        docker pull $ImageAdmin
    }
}

$netExists = docker network inspect $Network 2>$null
if (-not $netExists) {
    docker network create $Network | Out-Null
}

docker rm -f contextmemory-api 2>$null | Out-Null

Write-Host "==> Starting API on http://localhost:$ApiPort"
docker run -d --name contextmemory-api `
  --network $Network `
  --network-alias api `
  --add-host=host.docker.internal:host-gateway `
  -p "${ApiPort}:8080" `
  -v contextmemory-data:/app/data `
  -e ASPNETCORE_ENVIRONMENT=Production `
  -e ASPNETCORE_URLS=http://+:8080 `
  -e ContextMemory__PersistenceProvider=File `
  -e ContextMemory__DataPath=/app/data `
  -e "ContextMemory__OllamaEndpoint=$OllamaEndpoint" `
  -e "ContextMemory__DefaultLlmModel=$DefaultLlmModel" `
  -e "ContextMemory__MasterKey=$MasterKey" `
  -e "ContextMemory__AdminCorsOrigins__0=http://localhost:$AdminPort" `
  -e "ContextMemory__Apps__demo-dev__ApiKey=$DemoAppApiKey" `
  -e "ContextMemory__Apps__demo-dev__SystemPrompt=You are a helpful, clear, and precise assistant." `
  -e ContextMemory__Apps__demo-dev__DefaultLanguage=en-US `
  -e "ContextMemory__Apps__demo-dev__LlmModel=$DefaultLlmModel" `
  --restart unless-stopped `
  $ImageApi

if ($WithAdmin) {
    docker rm -f contextmemory-admin 2>$null | Out-Null

    Write-Host "==> Starting Admin on http://localhost:$AdminPort"
    docker run -d --name contextmemory-admin `
      --network $Network `
      -p "${AdminPort}:8080" `
      -e ASPNETCORE_ENVIRONMENT=Production `
      -e ASPNETCORE_URLS=http://+:8080 `
      -e Admin__DefaultApiBaseUrl=http://api:8080 `
      -e "Admin__PublicApiBaseUrl=http://localhost:$ApiPort" `
      -e "Admin__DefaultMasterKey=$MasterKey" `
      -e ApiBaseUrl=http://api:8080 `
      --restart unless-stopped `
      $ImageAdmin
}

Write-Host ""
Write-Host "API:    http://localhost:$ApiPort"
Write-Host "Health: http://localhost:$ApiPort/health"
if ($WithAdmin) {
    Write-Host "Admin:  http://localhost:$AdminPort"
}
Write-Host "Demo:   X-App-Id=demo-dev  Bearer $DemoAppApiKey"
Write-Host ""
Write-Host "Stop:   docker rm -f contextmemory-api contextmemory-admin"
