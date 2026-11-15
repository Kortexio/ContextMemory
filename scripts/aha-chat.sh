#!/usr/bin/env bash
# Chat aha: tell the gateway a fact, then ask for it in a second request that carries
# ONLY the new question. The answer can only come from ContextMemory's session memory.
# Works with any engine behind the gateway (llama.cpp, vLLM, Ollama, OpenAI, ...).
# Usage: ./scripts/aha-chat.sh
# Optional: CONTEXTMEMORY_STREAM=1 — use stream:true and assemble SSE content.
set -euo pipefail

BASE="${CONTEXTMEMORY_BASE_URL:-http://localhost:5100}"
KEY="${CONTEXTMEMORY_API_KEY:-cm_live_dev_key_change_me}"
APP="${CONTEXTMEMORY_APP_ID:-demo-dev}"
MODEL="${CONTEXTMEMORY_MODEL:-local-model}"
USER_ID="${CONTEXTMEMORY_USER_ID:-aha-user}"
SESSION="aha-$(date +%s)"
FACT="postgres-staging-01"
OUT="$(mktemp -d)"
STREAM="${CONTEXTMEMORY_STREAM:-0}"

extract_content() {
  # Prefer jq when present; else a small Python fallback.
  if command -v jq >/dev/null 2>&1; then
    jq -r '.choices[0].message.content // empty' "$1"
  else
    python3 - "$1" <<'PY'
import json,sys
doc=json.load(open(sys.argv[1],encoding="utf-8"))
print((doc.get("choices") or [{}])[0].get("message",{}).get("content") or "")
PY
  fi
}

chat_nonstream() {
  curl -sS --fail-with-body --max-time "${CONTEXTMEMORY_TIMEOUT:-300}" \
    -X POST "$BASE/v1/chat/completions" \
    -H "Authorization: Bearer $KEY" \
    -H "X-App-Id: $APP" \
    -H "X-User-Id: $USER_ID" \
    -H "X-Session-Id: $SESSION" \
    -H "Content-Type: application/json" \
    -d "{\"model\":\"$MODEL\",\"messages\":[{\"role\":\"user\",\"content\":\"$1\"}]}"
}

chat_stream() {
  local prompt="$1"
  local raw="$OUT/stream.raw"
  curl -sS --fail-with-body --max-time "${CONTEXTMEMORY_TIMEOUT:-300}" \
    -N -X POST "$BASE/v1/chat/completions" \
    -H "Authorization: Bearer $KEY" \
    -H "X-App-Id: $APP" \
    -H "X-User-Id: $USER_ID" \
    -H "X-Session-Id: $SESSION" \
    -H "Content-Type: application/json" \
    -d "{\"model\":\"$MODEL\",\"stream\":true,\"messages\":[{\"role\":\"user\",\"content\":\"$prompt\"}]}" \
    >"$raw"
  python3 - "$raw" <<'PY'
import json,sys
text=[]
for line in open(sys.argv[1],encoding="utf-8",errors="replace"):
    line=line.strip()
    if not line.startswith("data:"):
        continue
    data=line[5:].strip()
    if not data or data=="[DONE]":
        continue
    try:
        obj=json.loads(data)
    except json.JSONDecodeError:
        continue
    for ch in obj.get("choices") or []:
        delta=(ch.get("delta") or {})
        if isinstance(delta.get("content"), str):
            text.append(delta["content"])
        msg=ch.get("message") or {}
        if isinstance(msg.get("content"), str) and msg["content"]:
            text.append(msg["content"])
print("".join(text))
PY
}

chat() {
  if [ "$STREAM" = "1" ] || [ "$STREAM" = "true" ]; then
    chat_stream "$1"
  else
    chat_nonstream "$1"
  fi
}

echo "==> Health ($BASE)"
curl -sSf "$BASE/health" | head -c 300
echo; echo

if [ "$STREAM" = "1" ] || [ "$STREAM" = "true" ]; then
  echo "==> Streaming mode (CONTEXTMEMORY_STREAM=$STREAM)"
fi

echo "==> Turn 1 (session $SESSION): state the fact"
if [ "$STREAM" = "1" ] || [ "$STREAM" = "true" ]; then
  ANSWER1=$(chat "Remember this for later: our staging database host is $FACT. Reply with OK.")
  echo "$ANSWER1" | tee "$OUT/turn1.txt"
else
  chat "Remember this for later: our staging database host is $FACT. Reply with OK." | tee "$OUT/turn1.json"
fi
echo; echo

echo "==> Turn 2 (same session, body contains only the new question)"
if [ "$STREAM" = "1" ] || [ "$STREAM" = "true" ]; then
  ANSWER2=$(chat "What is our staging database host? Reply with the host name only.")
  echo "$ANSWER2" | tee "$OUT/turn2.txt"
else
  chat "What is our staging database host? Reply with the host name only." | tee "$OUT/turn2.json"
  ANSWER2=$(extract_content "$OUT/turn2.json")
fi
echo; echo

if printf '%s' "${ANSWER2:-}" | grep -q "$FACT"; then
  echo "AHA OK — the client sent no history; the gateway remembered '$FACT'."
  exit 0
fi

# Non-stream: also accept raw JSON containing the fact (jq missing / alternate shape)
if [ -f "$OUT/turn2.json" ] && grep -q "$FACT" "$OUT/turn2.json"; then
  echo "AHA OK — the client sent no history; the gateway remembered '$FACT'."
  exit 0
fi

echo "AHA FAILED — '$FACT' not found in the turn 2 answer."
exit 1
