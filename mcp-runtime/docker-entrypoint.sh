#!/bin/sh
set -e

# Populate mounted volume from image builtins when empty.
if [ ! -f /opt/mcps/zuora-mcp/dist/index.cjs ] && [ -f /opt/mcps-builtin/zuora-mcp/dist/index.cjs ]; then
  echo "Seeding /opt/mcps from built-in packages..."
  mkdir -p /opt/mcps
  # Copy package tree (including node_modules) so require() works.
  if [ -d /opt/mcps-builtin/node_modules ]; then
    cp -a /opt/mcps-builtin/node_modules /opt/mcps/
    cp -a /opt/mcps-builtin/package.json /opt/mcps/ 2>/dev/null || true
    cp -a /opt/mcps-builtin/package-lock.json /opt/mcps/ 2>/dev/null || true
    ln -sfn node_modules/zuora-mcp /opt/mcps/zuora-mcp
  else
    cp -a /opt/mcps-builtin/zuora-mcp /opt/mcps/
  fi
fi

if [ ! -f /opt/mcps/zuora-mcp/dist/index.cjs ]; then
  echo "Installing zuora-mcp into /opt/mcps (fallback)..."
  mkdir -p /opt/mcps
  cd /opt/mcps
  if [ ! -f package.json ]; then
    npm init -y >/dev/null 2>&1 || true
  fi
  npm install zuora-mcp@1.1.0 --omit=dev
  ln -sfn node_modules/zuora-mcp zuora-mcp
fi

if [ -f /opt/mcps/zuora-mcp/dist/index.cjs ]; then
  echo "zuora-mcp ready at /opt/mcps/zuora-mcp/dist/index.cjs"
else
  echo "Warning: zuora-mcp still missing; Zuora stdio MCP will fail until installed."
fi

exec node /app/server.mjs
