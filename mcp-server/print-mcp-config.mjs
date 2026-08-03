#!/usr/bin/env node
/**
 * Prints a ready-to-paste Cursor / Claude Desktop MCP snippet.
 * Usage: node print-mcp-config.mjs
 *        node print-mcp-config.mjs --path /absolute/path/to/server.mjs
 */
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const serverPath = process.argv.includes("--path")
  ? process.argv[process.argv.indexOf("--path") + 1]
  : path.join(__dirname, "server.mjs");

const baseUrl = process.env.CONTEXTMEMORY_BASE_URL || "http://localhost:5100";
const apiKey = process.env.CONTEXTMEMORY_API_KEY || "cm_live_dev_key_change_me";
const appId = process.env.CONTEXTMEMORY_APP_ID || "demo-dev";

const config = {
  mcpServers: {
    contextmemory: {
      command: "node",
      args: [serverPath.replace(/\\/g, "/")],
      env: {
        CONTEXTMEMORY_BASE_URL: baseUrl,
        CONTEXTMEMORY_API_KEY: apiKey,
        CONTEXTMEMORY_APP_ID: appId,
      },
    },
  },
};

console.log(JSON.stringify(config, null, 2));
console.error(`
# Cursor: Settings → MCP → Add from JSON (or merge into ~/.cursor/mcp.json)
# Claude Desktop: merge into claude_desktop_config.json
#
# Aha test (under 5 minutes):
# 1) Gateway running on ${baseUrl}
# 2) Paste the JSON above into Cursor MCP
# 3) Chat: "Remember: our staging DB is postgres-staging-01"
# 4) New chat: "What is our staging DB?" → should call memory_search / memory_get
`);
