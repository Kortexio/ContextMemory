#!/usr/bin/env bash
# Chat aha: tell the gateway a fact, then ask for it in a second request that carries
# ONLY the new question. The answer can only come from ContextMemory's session memory.
# Works with any engine behind the gateway (llama.cpp, vLLM, Ollama, OpenAI, ...).
# Usage: ./scripts/aha-chat.sh
set -euo pipefail

BASE="${CONTEXTMEMORY_BASE_URL:-http://localhost:5100}"
KEY="${CONTEXTMEMORY_API_KEY:-cm_live_dev_key_change_me}"
APP="${CONTEXTMEMORY_APP_ID:-demo-dev}"
MODEL="${CONTEXTMEMORY_MODEL:-local-model}"
USER_ID="${CONTEXTMEMORY_USER_ID:-aha-user}"
SESSION="aha-$(date +%s)"
FACT="postgres-staging-01"
OUT="$(mktemp -d)"

chat() {
  curl -sS --fail-with-body --max-time "${CONTEXTMEMORY_TIMEOUT:-300}" \
    -X POST "$BASE/v1/chat/completions" \
    -H "Authorization: Bearer $KEY" \
    -H "X-App-Id: $APP" \
    -H "X-User-Id: $USER_ID" \
    -H "X-Session-Id: $SESSION" \
    -H "Content-Type: application/json" \
    -d "{\"model\":\"$MODEL\",\"messages\":[{\"role\":\"user\",\"content\":\"$1\"}]}"
}

echo "==> Health ($BASE)"
curl -sSf "$BASE/health" | head -c 300
echo; echo

echo "==> Turn 1 (session $SESSION): state the fact"
chat "Remember this for later: our staging database host is $FACT. Reply with OK." | tee "$OUT/turn1.json"
echo; echo

echo "==> Turn 2 (same session, body contains only the new question)"
chat "What is our staging database host? Reply with the host name only." | tee "$OUT/turn2.json"
echo; echo

if grep -q "$FACT" "$OUT/turn2.json"; then
  echo "AHA OK — the client sent no history; the gateway remembered '$FACT'."
  exit 0
fi

echo "AHA FAILED — '$FACT' not found in the turn 2 answer."
exit 1
