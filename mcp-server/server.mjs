#!/usr/bin/env node
/**
 * ContextMemory MCP server — permanent memory for Cursor / Claude Desktop.
 *
 * Tools (wedge):
 *   memory_save   — persist a fact (Global Wiki, temporal supersede)
 *   memory_search — recall facts (optional asOf)
 *   memory_get    — fetch one document by id
 *   session_recall — compiled session wiki (advanced)
 *
 * Env:
 *   CONTEXTMEMORY_BASE_URL  (default http://localhost:5100)
 *   CONTEXTMEMORY_API_KEY   (cm_live_... or cmk_live_...)
 *   CONTEXTMEMORY_APP_ID    (required for self-host; omit on Kortexio Cloud)
 */
import { createHash } from "node:crypto";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";

const baseUrl = (process.env.CONTEXTMEMORY_BASE_URL || "http://localhost:5100").replace(/\/$/, "");
const apiKey = process.env.CONTEXTMEMORY_API_KEY || "";
const appId = (process.env.CONTEXTMEMORY_APP_ID || "").trim();

function headers() {
  if (!apiKey) {
    throw new Error("Set CONTEXTMEMORY_API_KEY (Bearer cm_live_... or cmk_live_...)");
  }
  const h = {
    Authorization: `Bearer ${apiKey}`,
    "Content-Type": "application/json",
  };
  if (appId) h["X-App-Id"] = appId;
  return h;
}

function wikiBase() {
  if (!appId) {
    // Cloud keys bind the tenant; self-host needs X-App-Id / path appId.
    throw new Error(
      "CONTEXTMEMORY_APP_ID is required for self-host wiki routes. On Kortexio Cloud, set your tenant app id from the dashboard if the gateway still expects it."
    );
  }
  return `/apps/${encodeURIComponent(appId)}/wiki`;
}

async function api(method, path, body) {
  const res = await fetch(`${baseUrl}${path}`, {
    method,
    headers: headers(),
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await res.text();
  let json;
  try {
    json = text ? JSON.parse(text) : null;
  } catch {
    json = { raw: text };
  }
  if (!res.ok) {
    throw new Error(`HTTP ${res.status}: ${typeof json === "object" ? JSON.stringify(json) : text}`);
  }
  return json;
}

function slugify(input) {
  const s = String(input || "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "")
    .slice(0, 48);
  return s || "note";
}

function memoryDocumentId(title, content) {
  if (title && title.trim()) return `memory:${slugify(title)}`;
  const hash = createHash("sha256").update(content).digest("hex").slice(0, 12);
  return `memory:${hash}`;
}

const server = new McpServer({
  name: "contextmemory",
  version: "1.1.0",
});

server.tool(
  "memory_save",
  "Save a durable fact to ContextMemory (persists across Cursor/Claude chats). Use when the user says remember, prefer, always, or states a project/user fact worth keeping.",
  {
    content: z.string().describe("Fact or note to remember (markdown ok)"),
    title: z.string().optional().describe("Short title (also used as stable id slug)"),
    documentId: z
      .string()
      .optional()
      .describe("Optional stable id (e.g. memory:stack). Default memory:<slug-or-hash>"),
  },
  async ({ content, title, documentId }) => {
    const id = documentId?.trim() || memoryDocumentId(title, content);
    const heading = title?.trim() || id;
    const body = content.trim().startsWith("#")
      ? content.trim()
      : `# ${heading}\n\n${content.trim()}\n`;
    const result = await api("PUT", `${wikiBase()}/documents/${encodeURIComponent(id)}`, {
      title: heading,
      content: body,
      sourceId: "mcp:memory",
      summary: content.trim().slice(0, 200),
    });
    return {
      content: [
        {
          type: "text",
          text: `Saved \`${result.documentId}\` (revision ${result.revisionId}${result.superseded ? ", superseded previous" : ""}).`,
        },
      ],
    };
  }
);

server.tool(
  "memory_search",
  "Search saved ContextMemory facts / Global Wiki. Use before answering questions about preferences, stack, or past decisions. Optional asOf (ISO-8601) for what was true at a past time.",
  {
    query: z.string().describe("Search query"),
    asOf: z.string().optional().describe("ISO-8601 point-in-time"),
    topK: z.number().int().positive().optional().describe("Max matches (default 5)"),
  },
  async ({ query, asOf, topK }) => {
    const result = await api("POST", `${wikiBase()}/query`, {
      query,
      asOf: asOf || undefined,
      topK: topK || 5,
      includeIndex: false,
    });
    const text =
      result.matches?.length > 0
        ? `Found ${result.matches.length} match(es).\n\n${result.compiledMarkdown || ""}`
        : "No matching memories found.";
    return { content: [{ type: "text", text }] };
  }
);

server.tool(
  "memory_get",
  "Fetch one saved memory / wiki document by documentId (e.g. memory:stack).",
  {
    documentId: z.string().describe("Document id"),
  },
  async ({ documentId }) => {
    const doc = await api(
      "GET",
      `${wikiBase()}/documents/${encodeURIComponent(documentId)}`
    );
    const text = `# ${doc.title || doc.documentId}\n\n_${doc.status} · ${doc.validFrom}_\n\n${doc.content || ""}`;
    return { content: [{ type: "text", text }] };
  }
);

// Aliases for docs / power users
server.tool(
  "wiki_search",
  "Alias of memory_search — search the Global Wiki knowledge base.",
  {
    query: z.string(),
    asOf: z.string().optional(),
    topK: z.number().int().positive().optional(),
    sourceId: z.string().optional(),
  },
  async ({ query, asOf, topK, sourceId }) => {
    const result = await api("POST", `${wikiBase()}/query`, {
      query,
      asOf: asOf || undefined,
      topK: topK || 5,
      sourceId: sourceId || undefined,
      includeIndex: false,
    });
    const text =
      result.matches?.length > 0
        ? `Found ${result.matches.length} match(es).\n\n${result.compiledMarkdown || ""}`
        : "No matching documents found.";
    return { content: [{ type: "text", text }] };
  }
);

server.tool(
  "session_recall",
  "Recall a chat session wiki (needs userId + sessionId from the gateway).",
  {
    userId: z.string(),
    sessionId: z.string(),
    query: z.string().optional(),
    budgetChars: z.number().int().positive().optional(),
  },
  async ({ userId, sessionId, query, budgetChars }) => {
    if (!appId) throw new Error("CONTEXTMEMORY_APP_ID required for session_recall");
    const qs = new URLSearchParams();
    if (query) qs.set("query", query);
    if (budgetChars) qs.set("budgetChars", String(budgetChars));
    const path =
      `/apps/${encodeURIComponent(appId)}/sessions/${encodeURIComponent(userId)}/${encodeURIComponent(sessionId)}/wiki` +
      (qs.toString() ? `?${qs}` : "");
    const result = await api("GET", path);
    return {
      content: [{ type: "text", text: result.compiledMarkdown || "(empty session wiki)" }],
    };
  }
);

const transport = new StdioServerTransport();
await server.connect(transport);
