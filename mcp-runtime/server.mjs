#!/usr/bin/env node
/**
 * ContextMemory MCP Runtime sidecar.
 * Generic stdio MCP host: gateway posts command/args/env and we speak MCP JSON-RPC over child stdin/stdout.
 */
import http from "node:http";
import { spawn } from "node:child_process";
import { createInterface } from "node:readline";
import { createHash } from "node:crypto";

const PORT = Number(process.env.PORT || 8080);
const IDLE_MS = Number(process.env.MCP_SESSION_IDLE_MS || 5 * 60 * 1000);
const DEFAULT_TIMEOUT_MS = Number(process.env.MCP_DEFAULT_TIMEOUT_MS || 120_000);

/** @type {Map<string, Session>} */
const sessions = new Map();

class Session {
  /**
   * @param {string} key
   * @param {{ command: string, args?: string[], env?: Record<string,string>, cwd?: string, timeoutSeconds?: number }} spec
   */
  constructor(key, spec) {
    this.key = key;
    this.spec = spec;
    this.requestId = 0;
    /** @type {Map<number, { resolve: Function, reject: Function, timer: NodeJS.Timeout }>} */
    this.pending = new Map();
    this.lastUsed = Date.now();
    this.ready = null;
    this.child = spawn(spec.command, spec.args || [], {
      cwd: spec.cwd || process.cwd(),
      env: { ...process.env, ...(spec.env || {}) },
      stdio: ["pipe", "pipe", "pipe"],
      windowsHide: true,
    });

    this.stdout = createInterface({ input: this.child.stdout });
    this.stdout.on("line", (line) => this.#onLine(line));
    this.child.stderr.on("data", (buf) => {
      const text = buf.toString("utf8").trim();
      if (text) console.error(`[mcp:${key}] ${text}`);
    });
    this.child.on("exit", (code, signal) => {
      for (const [, p] of this.pending) {
        clearTimeout(p.timer);
        p.reject(new Error(`MCP process exited code=${code} signal=${signal}`));
      }
      this.pending.clear();
      sessions.delete(this.key);
    });

    this.ready = this.#initialize();
  }

  get alive() {
    return this.child.exitCode === null && !this.child.killed;
  }

  async #initialize() {
    const init = await this.request("initialize", {
      protocolVersion: "2024-11-05",
      capabilities: {},
      clientInfo: { name: "contextmemory-mcp-runtime", version: "1.0.0" },
    });
    if (init?.error) throw new Error(init.error.message || "initialize failed");
    await this.notify("notifications/initialized");
    return true;
  }

  /**
   * @param {string} line
   */
  #onLine(line) {
    if (!line?.trim()) return;
    let msg;
    try {
      msg = JSON.parse(line);
    } catch {
      return;
    }
    const id = msg?.id;
    if (typeof id !== "number") return;
    const pending = this.pending.get(id);
    if (!pending) return;
    clearTimeout(pending.timer);
    this.pending.delete(id);
    this.lastUsed = Date.now();
    pending.resolve(msg);
  }

  /**
   * @param {string} method
   * @param {any} params
   */
  async request(method, params) {
    await (this.ready ?? Promise.resolve());
    this.lastUsed = Date.now();
    const id = ++this.requestId;
    const payload = JSON.stringify({ jsonrpc: "2.0", id, method, params });
    const timeoutMs =
      this.spec.timeoutSeconds > 0 ? this.spec.timeoutSeconds * 1000 : DEFAULT_TIMEOUT_MS;

    const resultPromise = new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`MCP request timed out: ${method} after ${timeoutMs}ms`));
      }, timeoutMs);
      this.pending.set(id, { resolve, reject, timer });
    });

    if (!this.child.stdin?.writable) throw new Error("MCP stdin is not writable");
    this.child.stdin.write(payload + "\n");
    return resultPromise;
  }

  /**
   * @param {string} method
   */
  async notify(method) {
    const payload = JSON.stringify({ jsonrpc: "2.0", method });
    this.child.stdin.write(payload + "\n");
  }

  /**
   * Drop this session so the next call starts a fresh MCP process/remote stream.
   * Needed after remote Streamable HTTP 504/network errors that leave the child half-dead.
   */
  invalidate(reason) {
    if (reason) console.warn(`[mcp:${this.key}] invalidating session: ${reason}`);
    sessions.delete(this.key);
    this.dispose();
  }

  dispose() {
    try {
      this.child.kill("SIGKILL");
    } catch {
      // ignore
    }
  }
}

/**
 * @param {{ command: string, args?: string[], env?: Record<string,string>, cwd?: string }} spec
 */
function sessionKey(spec) {
  const hash = createHash("sha256")
    .update(
      JSON.stringify({
        command: spec.command,
        args: spec.args || [],
        env: spec.env || {},
        cwd: spec.cwd || "",
      })
    )
    .digest("hex")
    .slice(0, 24);
  return hash;
}

/**
 * @param {{ command: string, args?: string[], env?: Record<string,string>, cwd?: string, timeoutSeconds?: number }} spec
 */
async function getSession(spec) {
  if (!spec?.command) {
    const err = new Error(
      "command is required (expected JSON body: { command, args?, env?, cwd?, timeoutSeconds? })"
    );
    err.statusCode = 400;
    throw err;
  }
  const key = sessionKey(spec);
  let session = sessions.get(key);
  if (session && !session.alive) {
    sessions.delete(key);
    session = undefined;
  }
  if (!session) {
    session = new Session(key, spec);
    sessions.set(key, session);
    try {
      await session.ready;
    } catch (err) {
      sessions.delete(key);
      session.dispose();
      throw err;
    }
  }
  session.lastUsed = Date.now();
  return session;
}

function httpError(statusCode, message) {
  const err = new Error(message);
  err.statusCode = statusCode;
  return err;
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    req.on("data", (c) => chunks.push(c));
    req.on("end", () => {
      const raw = Buffer.concat(chunks).toString("utf8").replace(/^\uFEFF/, "");
      if (!raw.trim()) return resolve({});
      try {
        resolve(JSON.parse(raw));
      } catch (err) {
        const preview = raw.length > 200 ? `${raw.slice(0, 200)}…` : raw;
        reject(
          httpError(
            400,
            `Invalid JSON body: ${err.message}. Preview: ${JSON.stringify(preview)}`
          )
        );
      }
    });
    req.on("error", reject);
  });
}

/**
 * @param {unknown} value
 */
function parseToolArguments(value) {
  if (value == null) return {};
  if (typeof value === "object" && !Array.isArray(value)) return value;
  if (typeof value !== "string") {
    throw httpError(400, "arguments must be a JSON object or a JSON string");
  }
  const trimmed = value.trim();
  if (!trimmed) return {};
  try {
    const parsed = JSON.parse(trimmed);
    if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) return parsed;
    throw httpError(400, "arguments JSON must be an object");
  } catch (err) {
    if (err?.statusCode) throw err;
    throw httpError(400, `Invalid arguments JSON: ${err.message}`);
  }
}

function sendJson(res, status, body) {
  const json = JSON.stringify(body);
  res.writeHead(status, {
    "content-type": "application/json; charset=utf-8",
    "content-length": Buffer.byteLength(json),
  });
  res.end(json);
}

const server = http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url || "/", `http://${req.headers.host || "localhost"}`);

    if (req.method === "GET" && url.pathname === "/health") {
      return sendJson(res, 200, {
        status: "healthy",
        sessions: sessions.size,
        node: process.version,
        platforms: ["stdio"],
      });
    }

    if (req.method === "POST" && url.pathname === "/v1/stdio/tools/list") {
      const body = await readBody(req);
      const session = await getSession(body);
      const response = await session.request("tools/list", {});
      if (response.error) return sendJson(res, 502, { error: response.error });
      return sendJson(res, 200, { result: response.result || { tools: [] } });
    }

    if (req.method === "POST" && url.pathname === "/v1/stdio/tools/call") {
      const body = await readBody(req);
      if (!body.toolName || typeof body.toolName !== "string") {
        throw httpError(400, "toolName is required");
      }
      const session = await getSession(body);
      let response;
      try {
        response = await session.request("tools/call", {
          name: body.toolName,
          arguments: parseToolArguments(body.arguments),
        });
      } catch (err) {
        session.invalidate(err?.message || "tools/call failed");
        throw err;
      }

      if (response.error) {
        const msg = formatRemoteError(response.error);
        if (isFatalRemoteError(response.error)) {
          session.invalidate(msg);
        }
        return sendJson(res, 502, {
          error: {
            ...response.error,
            message: msg,
            hint: remoteErrorHint(response.error),
          },
        });
      }
      return sendJson(res, 200, { result: response.result || {} });
    }

    sendJson(res, 404, { error: "not found" });
  } catch (err) {
    const status = Number(err?.statusCode) || 500;
    if (status >= 500) console.error(err);
    else console.warn(err?.message || String(err));
    sendJson(res, status, { error: err?.message || String(err) });
  }
});

/**
 * @param {any} error
 */
function formatRemoteError(error) {
  const raw = typeof error === "string" ? error : error?.message || JSON.stringify(error);
  return String(raw);
}

/**
 * @param {any} error
 */
function isFatalRemoteError(error) {
  const msg = formatRemoteError(error).toLowerCase();
  return (
    msg.includes("504") ||
    msg.includes("timed out") ||
    msg.includes("timeout") ||
    msg.includes("streamable http") ||
    msg.includes("sending the request") ||
    msg.includes("econnreset") ||
    msg.includes("socket hang up") ||
    msg.includes("network")
  );
}

/**
 * @param {any} error
 */
function remoteErrorHint(error) {
  const msg = formatRemoteError(error).toLowerCase();
  if (msg.includes("504") || msg.includes("timed out") || msg.includes("timeout")) {
    return "Zuora MCP gateway timed out (~60s). Narrow filters, reduce page size, or call the tool with help:true first.";
  }
  if (msg.includes("sending the request") || msg.includes("econnreset")) {
    return "Transient network error talking to Zuora MCP. Retry once; session was recycled.";
  }
  return undefined;
}

setInterval(() => {
  const now = Date.now();
  for (const [key, session] of sessions) {
    // Never kill a session that still has in-flight JSON-RPC calls (e.g. long Data Queries).
    if (session.pending.size > 0) continue;
    if (now - session.lastUsed > IDLE_MS) {
      session.dispose();
      sessions.delete(key);
    }
  }
}, 30_000).unref();

server.listen(PORT, "0.0.0.0", () => {
  console.log(`mcp-runtime listening on :${PORT}`);
});
