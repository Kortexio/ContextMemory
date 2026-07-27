#!/usr/bin/env bash
# Run ContextMemory from GHCR (or build locally with --build).
# Usage:
#   ./scripts/docker-run.sh                 # pull API from GHCR → :5100
#   ./scripts/docker-run.sh --with-admin    # API + Admin from GHCR
#   ./scripts/docker-run.sh --build         # build images locally instead of pull
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

WITH_ADMIN=0
DO_BUILD=0
API_PORT="${API_PORT:-5100}"
ADMIN_PORT="${ADMIN_PORT:-5200}"
OLLAMA_ENDPOINT="${OLLAMA_ENDPOINT:-http://host.docker.internal:11434}"
DEFAULT_LLM_MODEL="${DEFAULT_LLM_MODEL:-qwen3.5:9b}"
MASTER_KEY="${MASTER_KEY:-cm_master_dev_key_change_me}"
DEMO_APP_API_KEY="${DEMO_APP_API_KEY:-cm_live_dev_key_change_me}"
CM_TAG="${CM_TAG:-latest}"
IMAGE_API="${IMAGE_API:-ghcr.io/kortexio/contextmemory:${CM_TAG}}"
IMAGE_ADMIN="${IMAGE_ADMIN:-ghcr.io/kortexio/contextmemory-admin:${CM_TAG}}"
NETWORK="${NETWORK:-contextmemory-net}"

for arg in "$@"; do
  case "$arg" in
    --with-admin) WITH_ADMIN=1 ;;
    --build) DO_BUILD=1 ;;
    -h|--help)
      sed -n '2,7p' "$0"
      exit 0
      ;;
  esac
done

if [[ "$DO_BUILD" -eq 1 ]]; then
  IMAGE_API="contextmemory-api:local"
  IMAGE_ADMIN="contextmemory-admin:local"
  echo "==> Building API image ($IMAGE_API)"
  docker build -t "$IMAGE_API" -f Dockerfile .
  if [[ "$WITH_ADMIN" -eq 1 ]]; then
    echo "==> Building Admin image ($IMAGE_ADMIN)"
    docker build -t "$IMAGE_ADMIN" -f Dockerfile.admin .
  fi
else
  echo "==> Pulling $IMAGE_API"
  docker pull "$IMAGE_API"
  if [[ "$WITH_ADMIN" -eq 1 ]]; then
    echo "==> Pulling $IMAGE_ADMIN"
    docker pull "$IMAGE_ADMIN"
  fi
fi

if ! docker network inspect "$NETWORK" >/dev/null 2>&1; then
  docker network create "$NETWORK" >/dev/null
fi

docker rm -f contextmemory-api >/dev/null 2>&1 || true

echo "==> Starting API on http://localhost:${API_PORT}"
docker run -d --name contextmemory-api \
  --network "$NETWORK" \
  --network-alias api \
  --add-host=host.docker.internal:host-gateway \
  -p "${API_PORT}:8080" \
  -v contextmemory-data:/app/data \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ASPNETCORE_URLS=http://+:8080 \
  -e ContextMemory__PersistenceProvider=File \
  -e ContextMemory__DataPath=/app/data \
  -e ContextMemory__OllamaEndpoint="$OLLAMA_ENDPOINT" \
  -e ContextMemory__DefaultLlmModel="$DEFAULT_LLM_MODEL" \
  -e ContextMemory__MasterKey="$MASTER_KEY" \
  -e ContextMemory__AdminCorsOrigins__0="http://localhost:${ADMIN_PORT}" \
  -e ContextMemory__Apps__demo-dev__ApiKey="$DEMO_APP_API_KEY" \
  -e ContextMemory__Apps__demo-dev__SystemPrompt="You are a helpful, clear, and precise assistant." \
  -e ContextMemory__Apps__demo-dev__DefaultLanguage=en-US \
  -e ContextMemory__Apps__demo-dev__LlmModel="$DEFAULT_LLM_MODEL" \
  --restart unless-stopped \
  "$IMAGE_API"

if [[ "$WITH_ADMIN" -eq 1 ]]; then
  docker rm -f contextmemory-admin >/dev/null 2>&1 || true

  echo "==> Starting Admin on http://localhost:${ADMIN_PORT}"
  docker run -d --name contextmemory-admin \
    --network "$NETWORK" \
    -p "${ADMIN_PORT}:8080" \
    -e ASPNETCORE_ENVIRONMENT=Production \
    -e ASPNETCORE_URLS=http://+:8080 \
    -e Admin__DefaultApiBaseUrl=http://api:8080 \
    -e Admin__PublicApiBaseUrl="http://localhost:${API_PORT}" \
    -e Admin__DefaultMasterKey="$MASTER_KEY" \
    -e ApiBaseUrl=http://api:8080 \
    --restart unless-stopped \
    "$IMAGE_ADMIN"
fi

echo
echo "API:    http://localhost:${API_PORT}"
echo "Health: http://localhost:${API_PORT}/health"
if [[ "$WITH_ADMIN" -eq 1 ]]; then
  echo "Admin:  http://localhost:${ADMIN_PORT}"
fi
echo "Demo:   X-App-Id=demo-dev  Bearer ${DEMO_APP_API_KEY}"
echo
echo "Stop:   docker rm -f contextmemory-api contextmemory-admin"
