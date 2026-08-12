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

/** @type {Map<string, import('playwright').BrowserContext>} */
const browserSessions = new Map();
/** @type {import('playwright').Browser | null} */
let sharedBrowser = null;

async function getBrowser(headless = true) {
  if (sharedBrowser) return sharedBrowser;
  const { chromium } = await import("playwright");
  sharedBrowser = await chromium.launch({ headless: headless !== false });
  return sharedBrowser;
}

/**
 * @param {{ action?: string, sessionKey?: string, headless?: boolean, args?: any }} body
 */
async function runBrowser(body) {
  const action = String(body.action || "").toLowerCase();
  const sessionKey = String(body.sessionKey || "default");
  const args = body.args || {};
  const browser = await getBrowser(body.headless !== false);
  let context = browserSessions.get(sessionKey);
  if (!context) {
    context = await browser.newContext({ viewport: { width: 1280, height: 720 } });
    browserSessions.set(sessionKey, context);
  }
  const pages = context.pages();
  let page = pages.length > 0 ? pages[0] : await context.newPage();

  if (action === "navigate") {
    const target = String(args.url || "").trim();
    if (!target) {
      const err = new Error("url is required");
      err.statusCode = 400;
      throw err;
    }
    await page.goto(target, { waitUntil: "domcontentloaded", timeout: DEFAULT_TIMEOUT_MS });
    return { exitCode: 0, output: `Navigated to ${page.url()}\ntitle: ${await page.title()}` };
  }

  if (action === "snapshot") {
    const links = await page.$$eval("a[href], button, input, textarea, [role='button']", (els) =>
      els.slice(0, 80).map((el, i) => {
        const tag = el.tagName.toLowerCase();
        const text = (el.innerText || el.value || el.getAttribute("aria-label") || "").trim().slice(0, 80);
        const href = el.getAttribute("href") || "";
        return `ref=${i} <${tag}> ${text}${href ? ` href=${href}` : ""}`;
      })
    );
    // stash refs on page for click/type
    await page.evaluate((count) => {
      window.__cmRefs = Array.from(
        document.querySelectorAll("a[href], button, input, textarea, [role='button']")
      ).slice(0, count);
    }, 80);
    return {
      exitCode: 0,
      output: [`url: ${page.url()}`, `title: ${await page.title()}`, "", ...links].join("\n"),
    };
  }

  if (action === "click") {
    const ref = Number(args.ref ?? args.Ref);
    const handle = await page.evaluateHandle((i) => (window.__cmRefs || [])[i], ref);
    const el = handle.asElement();
    if (!el) {
      return { exitCode: 1, output: `No element for ref=${ref}. Call browser_snapshot first.` };
    }
    await el.click({ timeout: 10_000 });
    return { exitCode: 0, output: `Clicked ref=${ref}. url=${page.url()}` };
  }

  if (action === "type") {
    const ref = Number(args.ref ?? args.Ref);
    const text = String(args.text ?? "");
    const handle = await page.evaluateHandle((i) => (window.__cmRefs || [])[i], ref);
    const el = handle.asElement();
    if (!el) {
      return { exitCode: 1, output: `No element for ref=${ref}. Call browser_snapshot first.` };
    }
    await el.fill(text, { timeout: 10_000 }).catch(async () => {
      await el.click();
      await page.keyboard.type(text, { delay: 10 });
    });
    return { exitCode: 0, output: `Typed into ref=${ref} (${text.length} chars).` };
  }

  if (action === "screenshot") {
    const fullPage = Boolean(args.fullPage);
    const buf = await page.screenshot({ fullPage, type: "png" });
    return {
      exitCode: 0,
      output: `Screenshot captured (${buf.length} bytes) of ${page.url()}`,
      screenshotBase64: buf.toString("base64"),
    };
  }

  const err = new Error(`unsupported browser action '${action}'`);
  err.statusCode = 400;
  throw err;
}

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
 * @param {{ cwd?: string, timeoutMs?: number, input?: string, env?: Record<string, string> }} opts
 */
function runProcess(command, args, opts = {}) {
  const timeoutMs = opts.timeoutMs ?? DEFAULT_TIMEOUT_MS;
  const extraEnv = opts.env && typeof opts.env === "object" ? opts.env : {};
  return new Promise((resolve) => {
    const child = spawn(command, args, {
      cwd: opts.cwd || process.cwd(),
      env: {
        ...process.env,
        ...extraEnv,
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
 * @param {{ runtime?: string, command?: string, code?: string, env?: Record<string, string> }} body
 */
async function execute(body) {
  const runtime = String(body.runtime || "").toLowerCase();
  const workDir = await mkdtemp(join(tmpdir(), "cm-sandbox-"));
  const extraEnv =
    body.env && typeof body.env === "object" && !Array.isArray(body.env)
      ? Object.fromEntries(
          Object.entries(body.env)
            .filter(([k, v]) => typeof k === "string" && k.length > 0 && typeof v === "string")
            .map(([k, v]) => [k, String(v)])
        )
      : {};

  try {
    if (runtime === "shell") {
      const command = String(body.command || "").trim();
      if (!command) {
        const err = new Error("command is required for runtime=shell");
        err.statusCode = 400;
        throw err;
      }
      // Local-dev only: run via /bin/sh -c with timeout.
      return await runProcess("/bin/sh", ["-c", command], { cwd: workDir, env: extraEnv });
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
      return await runProcess("python3", [file], { cwd: workDir, env: extraEnv });
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
      return await runProcess("node", [file], { cwd: workDir, env: extraEnv });
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
        platforms: ["shell", "python", "node", "browser"],
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

    if (req.method === "POST" && url.pathname === "/browser") {
      const body = await readBody(req);
      const result = await runBrowser(body);
      return sendJson(res, 200, result);
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
