#!/usr/bin/env node
/**
 * Minimal MCP stdio server for Azure Log Analytics (ContextMemory inbound).
 * Auth via env (injected from Admin MCP credentials):
 *   AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET
 *   LOG_ANALYTICS_WORKSPACE_ID (default workspace)
 *
 * Protocol: JSON-RPC 2.0 over stdio.
 * - Writes NDJSON (JSON + "\n") — required by ContextMemory mcp-runtime / McpStdioClient.
 * - Reads NDJSON and Content-Length framing (dual) for local smoke / standard MCP clients.
 */

const TOOLS = [
  {
    name: "azure_logs_query",
    description: "Execute a KQL query in Azure Log Analytics workspace",
    inputSchema: {
      type: "object",
      properties: {
        workspace_id: {
          type: "string",
          description: "Log Analytics workspace ID (defaults to LOG_ANALYTICS_WORKSPACE_ID)",
        },
        query: { type: "string", description: "KQL query" },
        timespan_hours: {
          type: "number",
          description: "Time range in hours (default 1)",
          default: 1,
        },
      },
      required: ["query"],
    },
  },
  {
    name: "azure_logs_get_timeline",
    description: "Chronological HTTP/trace timeline for a resource + entity id",
    inputSchema: {
      type: "object",
      properties: {
        workspace_id: { type: "string" },
        resource_name: {
          type: "string",
          description: "Azure resource name fragment (e.g. paccar-api-acc)",
        },
        entity_id: {
          type: "string",
          description: "Business entity (VIN, subscription number, …)",
        },
        time_range_hours: { type: "number", default: 1 },
      },
      required: ["resource_name", "entity_id"],
    },
  },
  {
    name: "azure_logs_search_traces",
    description: "Search AppTraces / exceptions by keyword",
    inputSchema: {
      type: "object",
      properties: {
        workspace_id: { type: "string" },
        search_term: { type: "string" },
        time_range_hours: { type: "number", default: 1 },
        severity_level: {
          type: "string",
          enum: ["all", "error", "warning", "info"],
          default: "all",
        },
      },
      required: ["search_term"],
    },
  },
];

function env(name) {
  const v = process.env[name];
  return v && String(v).trim() ? String(v).trim() : "";
}

async function getAccessToken() {
  const tenant = env("AZURE_TENANT_ID");
  const clientId = env("AZURE_CLIENT_ID");
  const clientSecret = env("AZURE_CLIENT_SECRET");
  if (!tenant || !clientId || !clientSecret) {
    throw new Error(
      "Missing AZURE_TENANT_ID / AZURE_CLIENT_ID / AZURE_CLIENT_SECRET (set via MCP credentials Env)"
    );
  }

  const url = `https://login.microsoftonline.com/${encodeURIComponent(tenant)}/oauth2/v2.0/token`;
  const body = new URLSearchParams({
    client_id: clientId,
    client_secret: clientSecret,
    grant_type: "client_credentials",
    scope: "https://api.loganalytics.azure.com/.default",
  });

  const res = await fetch(url, {
    method: "POST",
    headers: { "content-type": "application/x-www-form-urlencoded" },
    body,
  });
  const json = await res.json();
  if (!res.ok) {
    throw new Error(`Azure AD token failed HTTP ${res.status}: ${JSON.stringify(json)}`);
  }
  if (!json.access_token) throw new Error("Azure AD response missing access_token");
  return json.access_token;
}

function resolveWorkspace(explicit) {
  const id = (explicit && String(explicit).trim()) || env("LOG_ANALYTICS_WORKSPACE_ID");
  if (!id) {
    throw new Error("workspace_id required (or set LOG_ANALYTICS_WORKSPACE_ID)");
  }
  return id;
}

async function queryWorkspace(workspaceId, kql, timespanHours = 1) {
  const token = await getAccessToken();
  const hours = Number.isFinite(Number(timespanHours)) ? Number(timespanHours) : 1;
  const url = `https://api.loganalytics.azure.com/v1/workspaces/${encodeURIComponent(workspaceId)}/query`;
  const res = await fetch(url, {
    method: "POST",
    headers: {
      authorization: `Bearer ${token}`,
      "content-type": "application/json",
    },
    body: JSON.stringify({
      query: kql,
      timespan: `PT${Math.max(1, Math.round(hours))}H`,
    }),
  });
  const json = await res.json();
  if (!res.ok) {
    throw new Error(`Log Analytics query failed HTTP ${res.status}: ${JSON.stringify(json)}`);
  }

  const table = Array.isArray(json.tables) ? json.tables[0] : null;
  const columns = table?.columns?.map((c) => c.name) ?? [];
  const rows = table?.rows ?? [];
  return {
    columns,
    rows,
    row_count: rows.length,
  };
}

function buildTimelineKql(resourceName, entityId) {
  const res = resourceName.replace(/"/g, '\\"');
  const ent = entityId.replace(/"/g, '\\"');
  return `
let res = "${res}";
let ent = "${ent}";
union isfuzzy=true
  (AppServiceHTTPLogs
    | where Resource contains res and Url contains ent
    | project TimeGenerated, event_type="http_request", details=strcat(Method, " ", Url, " → ", tostring(ScStatus)), source="AppServiceHTTPLogs"),
  (AppTraces
    | where Message contains ent or OperationName contains ent
    | project TimeGenerated, event_type="trace", details=Message, source="AppTraces"),
  (AppExceptions
    | where OuterMessage contains ent or Details contains ent or ProblemId contains ent
    | project TimeGenerated, event_type="exception", details=OuterMessage, source="AppExceptions")
| order by TimeGenerated asc
| take 200
`.trim();
}

function buildTracesKql(searchTerm, severity) {
  const term = searchTerm.replace(/"/g, '\\"');
  const sevFilter =
    severity === "error"
      ? "| where SeverityLevel >= 3"
      : severity === "warning"
        ? "| where SeverityLevel >= 2"
        : severity === "info"
          ? "| where SeverityLevel >= 1"
          : "";
  return `
let term = "${term}";
union isfuzzy=true
  (AppTraces
    | where Message contains term or OperationName contains term
    ${sevFilter}
    | project TimeGenerated, kind="trace", text=Message, severity=SeverityLevel),
  (AppExceptions
    | where OuterMessage contains term or Details contains term
    | project TimeGenerated, kind="exception", text=OuterMessage, severity=3)
| order by TimeGenerated desc
| take 100
`.trim();
}

async function callTool(name, args) {
  const a = args || {};
  if (name === "azure_logs_query") {
    const workspaceId = resolveWorkspace(a.workspace_id);
    const result = await queryWorkspace(workspaceId, String(a.query || ""), a.timespan_hours ?? 1);
    return result;
  }

  if (name === "azure_logs_get_timeline") {
    const workspaceId = resolveWorkspace(a.workspace_id);
    const resource = String(a.resource_name || "").trim();
    const entity = String(a.entity_id || "").trim();
    if (!resource || !entity) throw new Error("resource_name and entity_id are required");
    const result = await queryWorkspace(
      workspaceId,
      buildTimelineKql(resource, entity),
      a.time_range_hours ?? 1
    );
    const timeline = (result.rows || []).map((row) => {
      const obj = {};
      result.columns.forEach((c, i) => {
        obj[c] = row[i];
      });
      return {
        timestamp: obj.TimeGenerated,
        event_type: obj.event_type,
        details: obj.details,
        source: obj.source,
      };
    });
    return { entity_id: entity, timeline, row_count: timeline.length };
  }

  if (name === "azure_logs_search_traces") {
    const workspaceId = resolveWorkspace(a.workspace_id);
    const term = String(a.search_term || "").trim();
    if (!term) throw new Error("search_term is required");
    const severity = String(a.severity_level || "all").toLowerCase();
    return await queryWorkspace(
      workspaceId,
      buildTracesKql(term, severity),
      a.time_range_hours ?? 1
    );
  }

  throw new Error(`Unknown tool: ${name}`);
}

function writeMessage(msg) {
  // mcp-runtime Session reads line-delimited JSON (createInterface + JSON.parse).
  process.stdout.write(JSON.stringify(msg) + "\n");
}

function handleRequest(msg) {
  const id = msg.id;
  const method = msg.method;
  const params = msg.params || {};

  const reply = (result) => writeMessage({ jsonrpc: "2.0", id, result });
  const fail = (code, message) =>
    writeMessage({ jsonrpc: "2.0", id, error: { code, message } });

  try {
    if (method === "initialize") {
      return reply({
        protocolVersion: "2024-11-05",
        capabilities: { tools: {} },
        serverInfo: { name: "azure-monitor-mcp", version: "0.1.1" },
      });
    }

    if (method === "notifications/initialized" || method === "initialized") {
      return;
    }

    if (method === "tools/list") {
      return reply({ tools: TOOLS });
    }

    if (method === "tools/call") {
      const name = params.name;
      const args = params.arguments || {};
      return callTool(name, args)
        .then((data) =>
          reply({
            content: [{ type: "text", text: JSON.stringify(data, null, 2) }],
            structuredContent: data,
          })
        )
        .catch((err) =>
          reply({
            content: [{ type: "text", text: String(err?.message || err) }],
            isError: true,
          })
        );
    }

    if (method === "ping") {
      return reply({});
    }

    return fail(-32601, `Method not found: ${method}`);
  } catch (err) {
    return fail(-32000, String(err?.message || err));
  }
}

function dispatchMessage(raw) {
  try {
    const msg = JSON.parse(raw);
    if (msg.method) handleRequest(msg);
  } catch (err) {
    writeMessage({
      jsonrpc: "2.0",
      id: null,
      error: { code: -32700, message: `Parse error: ${err.message}` },
    });
  }
}

/** Dual reader: NDJSON (mcp-runtime) + Content-Length (spec / local clients). */
let buffer = Buffer.alloc(0);
process.stdin.on("data", (chunk) => {
  buffer = Buffer.concat([buffer, chunk]);
  while (buffer.length > 0) {
    const asText = buffer.toString("utf8");
    const trimmedStart = asText.match(/^\s*/)?.[0].length ?? 0;
    const first = asText.slice(trimmedStart, trimmedStart + 1);

    // Content-Length framing
    if (/^content-length:/i.test(asText.slice(trimmedStart))) {
      const headerEnd = buffer.indexOf("\r\n\r\n");
      if (headerEnd < 0) break;
      const header = buffer.slice(0, headerEnd).toString("utf8");
      const match = /Content-Length:\s*(\d+)/i.exec(header);
      if (!match) {
        buffer = buffer.slice(headerEnd + 4);
        continue;
      }
      const len = Number(match[1]);
      const total = headerEnd + 4 + len;
      if (buffer.length < total) break;
      const body = buffer.slice(headerEnd + 4, total).toString("utf8");
      buffer = buffer.slice(total);
      dispatchMessage(body);
      continue;
    }

    // NDJSON / line-delimited JSON (ContextMemory mcp-runtime)
    if (first === "{" || first === "[") {
      const nl = buffer.indexOf(0x0a); // \n
      if (nl < 0) break;
      const line = buffer.slice(0, nl).toString("utf8").replace(/\r$/, "").trim();
      buffer = buffer.slice(nl + 1);
      if (line) dispatchMessage(line);
      continue;
    }

    // Skip leading junk / incomplete header wait
    if (trimmedStart > 0) {
      buffer = buffer.slice(trimmedStart);
      continue;
    }
    break;
  }
});

process.stdin.on("end", () => process.exit(0));
