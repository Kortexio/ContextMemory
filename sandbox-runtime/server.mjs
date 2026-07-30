#!/usr/bin/env node
/**
 * ContextMemory local-dev sandbox runtime.
 * Implements POST /execute for self-hosted-sandbox execution tools.
 * NOT the same as mcp-runtime (which hosts MCP stdio servers).
 *
 * This is a lightweight local sandbox (timeout + non-root), not full gVisor isolation.
 */
import http from "node:http";
import { spawn } from "node:child_process";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";

const PORT = Number(process.env.PORT || 8080);
const DEFAULT_TIMEOUT_MS = Number(process.env.SANDBOX_TIMEOUT_MS || 60_000);
const MAX_OUTPUT_CHARS = Number(process.env.SANDBOX_MAX_OUTPUT_CHARS || 32_000);
const ALLOW_EGRESS = String(process.env.SANDBOX_ALLOW_EGRESS || "true").toLowerCase() !== "false";

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    req.on("data", (c) => chunks.push(c));
    req.on("end", () => {
      const raw = Buffer.concat(chunks).toString("utf8");
      if (!raw) return resolve({});
      try {
        resolve(JSON.parse(raw));
      } catch (err) {
        reject(Object.assign(new Error(`Invalid JSON: ${err.message}`), { statusCode: 400 }));
      }
    });
    req.on("error", reject);
  });
}

function sendJson(res, status, body) {
  const json = JSON.stringify(body);
  res.writeHead(status, {
    "content-type": "application/json; charset=utf-8",
    "content-length": Buffer.byteLength(json),
  });
  res.end(json);
}

function truncate(text) {
  if (!text) return "";
  if (text.length <= MAX_OUTPUT_CHARS) return text;
  return text.slice(0, MAX_OUTPUT_CHARS) + `\n…[truncated ${text.length - MAX_OUTPUT_CHARS} chars]`;
}

/**
 * @param {string} command
 * @param {string[]} args
 * @param {{ cwd?: string, timeoutMs?: number, input?: string }} opts
 */
function runProcess(command, args, opts = {}) {
  const timeoutMs = opts.timeoutMs ?? DEFAULT_TIMEOUT_MS;
  return new Promise((resolve) => {
    const child = spawn(command, args, {
      cwd: opts.cwd || process.cwd(),
      env: {
        ...process.env,
        HOME: "/tmp",
        TMPDIR: "/tmp",
        PYTHONDONTWRITEBYTECODE: "1",
        PYTHONUNBUFFERED: "1",
      },
      stdio: ["pipe", "pipe", "pipe"],
      windowsHide: true,
    });

    let stdout = "";
    let stderr = "";
    let settled = false;

    const finish = (exitCode, timedOut = false) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      resolve({
        exitCode: timedOut ? 124 : exitCode ?? 1,
        output: truncate(
          [
            timedOut ? `[sandbox] timed out after ${timeoutMs}ms` : null,
            stdout.trimEnd(),
            stderr.trimEnd() ? `stderr:\n${stderr.trimEnd()}` : null,
          ]
            .filter(Boolean)
            .join("\n")
        ),
      });
    };

    const timer = setTimeout(() => {
      try {
        child.kill("SIGKILL");
      } catch {
        // ignore
      }
      finish(124, true);
    }, timeoutMs);

    child.stdout.on("data", (buf) => {
      stdout += buf.toString("utf8");
      if (stdout.length > MAX_OUTPUT_CHARS * 2) stdout = stdout.slice(0, MAX_OUTPUT_CHARS * 2);
    });
    child.stderr.on("data", (buf) => {
      stderr += buf.toString("utf8");
      if (stderr.length > MAX_OUTPUT_CHARS * 2) stderr = stderr.slice(0, MAX_OUTPUT_CHARS * 2);
    });
    child.on("error", (err) => {
      stderr += `\n${err.message}`;
      finish(1);
    });
    child.on("close", (code) => finish(code ?? 1));

    if (opts.input != null) {
      child.stdin.write(opts.input);
    }
    child.stdin.end();
  });
}

/**
 * @param {{ runtime?: string, command?: string, code?: string }} body
 */
async function execute(body) {
  const runtime = String(body.runtime || "").toLowerCase();
  const workDir = await mkdtemp(join(tmpdir(), "cm-sandbox-"));

  try {
    if (runtime === "shell") {
      const command = String(body.command || "").trim();
      if (!command) {
        const err = new Error("command is required for runtime=shell");
        err.statusCode = 400;
        throw err;
      }
      // Local-dev only: run via /bin/sh -c with timeout.
      return await runProcess("/bin/sh", ["-c", command], { cwd: workDir });
    }

    if (runtime === "python") {
      const code = String(body.code || "");
      if (!code.trim()) {
        const err = new Error("code is required for runtime=python");
        err.statusCode = 400;
        throw err;
      }
      const file = join(workDir, "main.py");
      await writeFile(file, code, "utf8");
      return await runProcess("python3", [file], { cwd: workDir });
    }

    if (runtime === "node") {
      const code = String(body.code || "");
      if (!code.trim()) {
        const err = new Error("code is required for runtime=node");
        err.statusCode = 400;
        throw err;
      }
      const file = join(workDir, "main.mjs");
      await writeFile(file, code, "utf8");
      return await runProcess("node", [file], { cwd: workDir });
    }

    const err = new Error(`unsupported runtime '${runtime}' (expected shell|python|node)`);
    err.statusCode = 400;
    throw err;
  } finally {
    await rm(workDir, { recursive: true, force: true }).catch(() => {});
  }
}

const server = http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url || "/", `http://${req.headers.host || "localhost"}`);

    if (req.method === "GET" && url.pathname === "/health") {
      return sendJson(res, 200, {
        status: "healthy",
        platforms: ["shell", "python", "node"],
        allowEgress: ALLOW_EGRESS,
        timeoutMs: DEFAULT_TIMEOUT_MS,
        note: "local-dev sandbox (not full gVisor isolation)",
      });
    }

    if (req.method === "POST" && url.pathname === "/execute") {
      const body = await readBody(req);
      const result = await execute(body);
      return sendJson(res, 200, {
        output: result.output,
        exitCode: result.exitCode,
      });
    }

    sendJson(res, 404, { error: "not found" });
  } catch (err) {
    const status = Number(err?.statusCode) || 500;
    if (status >= 500) console.error(err);
    sendJson(res, status, {
      output: err?.message || String(err),
      exitCode: 1,
      error: err?.message || String(err),
    });
  }
});

server.listen(PORT, "0.0.0.0", () => {
  console.log(`sandbox-runtime listening on :${PORT} (egress=${ALLOW_EGRESS})`);
});
