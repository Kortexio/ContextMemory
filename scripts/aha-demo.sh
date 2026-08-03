#!/usr/bin/env bash
# Sub-5-min aha demo (no Cursor required) — save a fact, recall it.
# Usage: ./scripts/aha-demo.sh
set -euo pipefail

BASE="${CONTEXTMEMORY_BASE_URL:-http://localhost:5100}"
KEY="${CONTEXTMEMORY_API_KEY:-cm_live_dev_key_change_me}"
APP="${CONTEXTMEMORY_APP_ID:-demo-dev}"
DOC_ID="memory:staging-db"

echo "==> Health"
curl -sf "$BASE/health" | head -c 200
echo
echo

echo "==> Save fact (memory_save equivalent)"
curl -sf -X PUT "$BASE/apps/$APP/wiki/documents/$DOC_ID" \
  -H "Authorization: Bearer $KEY" \
  -H "X-App-Id: $APP" \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Staging DB",
    "content": "# Staging DB\n\nStaging database host is **postgres-staging-01**.\n",
    "sourceId": "mcp:memory",
    "summary": "Staging DB is postgres-staging-01"
  }' | tee /tmp/cm-aha-save.json
echo
echo

echo "==> Search (new session equivalent)"
curl -sf -X POST "$BASE/apps/$APP/wiki/query" \
  -H "Authorization: Bearer $KEY" \
  -H "X-App-Id: $APP" \
  -H "Content-Type: application/json" \
  -d '{"query":"staging database","topK":3}' | tee /tmp/cm-aha-search.json
echo
echo

if grep -q "postgres-staging-01" /tmp/cm-aha-search.json; then
  echo "AHA OK — fact survived and was retrieved."
  exit 0
else
  echo "AHA FAILED — fact not found in search output."
  exit 1
fi
