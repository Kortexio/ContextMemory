#!/usr/bin/env node
/**
 * ContextMemory adversarial agentic benchmark CLI.
 *
 *   node benchmarks/bench.mjs list
 *   node benchmarks/bench.mjs selftest
 *   node benchmarks/bench.mjs ab [--label <name>]
 *   node benchmarks/bench.mjs report [results-file-or-dir]
 *
 * Env:
 *   CM_BENCH_BASE_URL  OpenAI-compatible host (default https://llm.example.test)
 *   CM_BENCH_MODEL     model id (default qwen3.6-35b)
 *   CM_BENCH_API_KEY   optional — only if the API under test requires a key (not a ContextMemory app key)
 */

import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const HERE = __dirname;
const TASKS_DIR = path.join(HERE, "tasks");
const RESULTS_DIR = path.join(HERE, "results");
const FIXTURES_ROOT = HERE;

const DEFAULT_BASE_URL = "https://llm.example.test";
const DEFAULT_MODEL = "qwen3.6-35b-iq3s-mtp";

function die(msg) {
  console.error(msg);
  process.exit(1);
}

function parseArgs(argv) {
  const pos = [];
  const flags = {};
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a.startsWith("--")) {
      const [k, v] = a.slice(2).split("=");
      if (v !== undefined) flags[k] = v;
      else if (argv[i + 1] && !argv[i + 1].startsWith("--")) flags[k] = argv[++i];
      else flags[k] = true;
    } else {
      pos.push(a);
    }
  }
  return { pos, flags };
}

function loadTasks() {
  if (!fs.existsSync(TASKS_DIR)) die(`Missing tasks dir: ${TASKS_DIR}`);
  return fs
    .readdirSync(TASKS_DIR, { withFileTypes: true })
    .filter((d) => d.isDirectory())
    .map((d) => {
      const taskPath = path.join(TASKS_DIR, d.name, "task.json");
      if (!fs.existsSync(taskPath)) die(`Missing ${taskPath}`);
      const raw = JSON.parse(fs.readFileSync(taskPath, "utf8"));
      return { ...raw, dir: path.join(TASKS_DIR, d.name), taskPath };
    })
    .sort((a, b) => String(a.id).localeCompare(String(b.id)));
}

function resolveFixture(rel) {
  const p = path.isAbsolute(rel) ? rel : path.join(FIXTURES_ROOT, rel);
  return p;
}

function authHeaders() {
  // Only when the bench target /v1 itself requires auth — not a ContextMemory gateway key.
  const key = (process.env.CM_BENCH_API_KEY || process.env.CM_BENCH_BEARER || "").trim();
  if (!key) return {};
  return { Authorization: `Bearer ${key}` };
}

function cmdList() {
  const tasks = loadTasks();
  console.log("id\texpectBaselineFail\ttitle");
  for (const t of tasks) {
    console.log(`${t.id}\t${Boolean(t.expectBaselineFail)}\t${t.title || ""}`);
  }
  console.log(`\n${tasks.length} task(s).`);
}

function scoreProbe(task, probe) {
  const answer = String(probe.answer ?? "");
  const steps = Array.isArray(probe.steps) ? probe.steps : [];
  const facts = task.hiddenChecks?.facts || {};
  const tools = task.hiddenChecks?.tools || {};
  let fail = false;
  let reason = "";

  if (tools.forbiddenFinishWithoutTools && steps.length === 0 && answer.trim()) {
    fail = true;
    reason = "forbiddenFinishWithoutTools";
  }
  if (facts.forbidUnsourcedYesNo) {
    const yesNo = /\b(yes|no)\b/i.test(answer);
    const evidence = tools.evidenceRequiredAny || [];
    const hasEvidence = evidence.some((e) =>
      steps.some((s) => String(s.tool || "").toLowerCase().includes(String(e).toLowerCase()))
    );
    if (yesNo && !hasEvidence) {
      fail = true;
      reason = reason || "forbidUnsourcedYesNo";
    }
  }
  if (tools.rejectEmptyArgs && steps.some((s) => !s.args || s.args === "{}" || s.args === "null")) {
    fail = true;
    reason = reason || "rejectEmptyArgs";
  }
  const stuckAt = tools.stuckIfIdenticalAbove ?? tools.maxIdenticalCalls;
  if (stuckAt != null) {
    const counts = new Map();
    for (const s of steps) {
      const key = `${s.tool}|${s.args || "{}"}`;
      counts.set(key, (counts.get(key) || 0) + 1);
    }
    for (const n of counts.values()) {
      if (n > stuckAt) {
        fail = true;
        reason = reason || "stuck";
      }
    }
  }
  const must = facts.mustContain || [];
  for (const needle of must) {
    if (needle && !answer.toLowerCase().includes(String(needle).toLowerCase()) && steps.length === 0) {
      fail = true;
      reason = reason || "missing mustContain";
    }
  }
  const forbiddenChosen = facts.forbiddenAsChosenHost || [];
  for (const needle of forbiddenChosen) {
    if (needle && answer.trim().toLowerCase() === String(needle).toLowerCase()) {
      fail = true;
      reason = reason || "forbiddenAsChosenHost";
    }
  }
  if (facts.chosenHostMustBe?.length && !facts.chosenHostMustBe.some((h) => answer.includes(h))) {
    // only fail probe when answer is wrong host — probes that mustFail often use wrong host
  }

  return { fail, reason };
}

function cmdSelftest() {
  const tasks = loadTasks();
  const errors = [];
  const warnings = [];

  if (tasks.length === 0) errors.push("No tasks found under benchmarks/tasks/");

  for (const t of tasks) {
    if (!t.id) errors.push(`${t.taskPath}: missing id`);
    if (!t.prompt || !String(t.prompt).trim()) errors.push(`${t.id}: missing prompt`);
    if (t.expectBaselineFail !== true) {
      errors.push(
        `${t.id}: expectBaselineFail must be true on seed tasks (got ${JSON.stringify(t.expectBaselineFail)})`
      );
    }
    if (!t.hiddenChecks || typeof t.hiddenChecks !== "object") {
      errors.push(`${t.id}: missing hiddenChecks`);
    } else if (!t.hiddenChecks.facts && !t.hiddenChecks.tools) {
      errors.push(`${t.id}: hiddenChecks must include facts and/or tools`);
    }
    if (!t.scoring?.weights) {
      warnings.push(`${t.id}: scoring.weights missing (notes only until live scorer)`);
    }
    if (!t.baselineProbe || t.baselineProbe.mustFail !== true) {
      errors.push(`${t.id}: missing baselineProbe.mustFail=true (CodeForge-style honesty)`);
    } else {
      const probe = scoreProbe(t, t.baselineProbe);
      if (!probe.fail) {
        errors.push(
          `${t.id}: baselineProbe did not fail scoring (${t.baselineProbe.reason || "no reason"}) — suite is soft`
        );
      }
    }

    const fixtures = Array.isArray(t.fixtures) ? t.fixtures : [];
    for (const rel of fixtures) {
      const abs = resolveFixture(rel);
      if (!fs.existsSync(abs)) {
        errors.push(`${t.id}: fixture missing: ${rel} → ${abs}`);
      }
    }
  }

  const requiredShared = [
    "fixtures/wiki/staging-host.md",
    "fixtures/wiki/staging-host-distractor.md",
    "fixtures/mcp/query_objects.schema.json",
  ];
  for (const rel of requiredShared) {
    if (!fs.existsSync(resolveFixture(rel))) {
      errors.push(`shared fixture missing: ${rel}`);
    }
  }

  const staging = fs.readFileSync(resolveFixture("fixtures/wiki/staging-host.md"), "utf8");
  if (!staging.includes("postgres-staging-01")) {
    errors.push("fixtures/wiki/staging-host.md must contain fact postgres-staging-01");
  }

  console.log(`selftest: ${tasks.length} task(s)`);
  for (const w of warnings) console.warn(`  warn: ${w}`);
  if (errors.length) {
    for (const e of errors) console.error(`  fail: ${e}`);
    die(`selftest FAILED (${errors.length} error(s))`);
  }
  console.log("selftest OK — fixtures present; every baselineProbe fails scoring (suite not soft).");
}

function emptyMetrics() {
  return {
    score: null,
    pass: null,
    falseDone: null,
    stuck: null,
    badArgs: null,
    familyLifts: null,
    repairs: null,
    loopExit: null,
    steps: null,
    latencyMs: null,
    failureClass: null,
  };
}

function buildAbDocument({ label, reachable, baseUrl, model, tasks }) {
  const stamp = new Date().toISOString().replace(/[:.]/g, "-");
  const runId = `${label || "ab"}-${stamp}`;
  const taskRows = tasks.map((t) => ({
    id: t.id,
    expectBaselineFail: t.expectBaselineFail === true,
    prompt: t.prompt,
    off: {
      harness: "off",
      ...emptyMetrics(),
      note: reachable ? "pending live / in-process harness run" : "endpoint unreachable — structure only",
    },
    on: {
      harness: "on",
      ...emptyMetrics(),
      note: reachable ? "pending live / in-process harness run" : "endpoint unreachable — structure only",
    },
  }));

  return {
    schemaVersion: 1,
    kind: "cm-bench-ab",
    runId,
    createdAt: new Date().toISOString(),
    reachable,
    protocol: {
      description:
        "A/B harness proof against an OpenAI-compatible /v1 model host — no ContextMemory API key. " +
        "Harness off vs on is applied by the local CM harness (in-process), not by gateway auth. " +
        "Publish README Measured only after honest Δ (off must hurt when expectBaselineFail).",
      steps: [
        "1. Point CM_BENCH_BASE_URL at any OpenAI-compatible /v1 (default llm.example.test).",
        "2. Run each task with harness OFF (no family recovery / arg repair / finish gate / Continue).",
        "3. Repeat with harness ON (full CM harness against the same model).",
        "4. Score hidden checks; compare Δ score / stuck / badArgs / falseDone.",
        "5. If off scores high, invalidate the task — do not market.",
      ],
      expectBaselineFail: true,
      auth:
        "CM_BENCH_API_KEY is optional and only used when the API under test requires a key. Not a ContextMemory app/gateway key.",
    },
    env: {
      CM_BENCH_BASE_URL: baseUrl,
      CM_BENCH_MODEL: model,
      CM_BENCH_API_KEY:
        (process.env.CM_BENCH_API_KEY || process.env.CM_BENCH_BEARER || "").trim()
          ? "(set — target API auth)"
          : "(none — target API open)",
    },
    endpoint: `${baseUrl.replace(/\/$/, "")}/v1/chat/completions`,
    model,
    tasks: taskRows,
    summary: {
      meanScoreOff: null,
      meanScoreOn: null,
      deltaScore: null,
      stuckOff: null,
      stuckOn: null,
      badArgsOff: null,
      badArgsOn: null,
      falseDoneOff: null,
      falseDoneOn: null,
      note: reachable
        ? "Endpoint reachable. Fill metrics via in-process harness A/B (no CM API key)."
        : "Could not reach /v1 — wrote protocol stub only. Check CM_BENCH_BASE_URL (and CM_BENCH_API_KEY only if that API requires auth).",
    },
  };
}

function printAbInstructions(baseUrl, model) {
  console.log(`
A/B protocol (harness off → on)
-------------------------------
Inference: ${baseUrl.replace(/\/$/, "")}/v1/chat/completions
Model:     ${model}

  run 1: harness OFF  — baseline (expectBaselineFail)
  run 2: harness ON   — same model / prompts, CM harness enabled in-process

Env:
  set CM_BENCH_BASE_URL=${baseUrl}
  set CM_BENCH_MODEL=${model}
  # optional — ONLY if this API requires a key:
  # set CM_BENCH_API_KEY=<key for the API under test>

Then: node benchmarks/bench.mjs ab
      node benchmarks/bench.mjs report

Honesty: if off already passes, harden the task — do not publish vanity Δ.
`);
}

async function probeEndpoint(baseUrl) {
  const headers = {
    Accept: "application/json",
    ...authHeaders(),
  };
  const controllers = [
    () => fetch(`${baseUrl}/v1/models`, { headers, signal: AbortSignal.timeout(8000) }),
    () => fetch(`${baseUrl}/health`, { headers, signal: AbortSignal.timeout(8000) }),
    () => fetch(`${baseUrl}/`, { headers, signal: AbortSignal.timeout(8000) }),
  ];
  for (const run of controllers) {
    try {
      const res = await run();
      if (res && res.status > 0 && res.status < 500) {
        return { ok: true, status: res.status };
      }
    } catch {
      /* try next */
    }
  }
  return { ok: false, status: null };
}

async function cmdAb(flags) {
  const baseUrl = (process.env.CM_BENCH_BASE_URL || DEFAULT_BASE_URL).replace(/\/$/, "");
  const model = process.env.CM_BENCH_MODEL || DEFAULT_MODEL;
  const tasks = loadTasks();
  const label = typeof flags.label === "string" ? flags.label : "ab";
  const stubOnly = flags.stub === true || flags["stub-only"] === true;

  cmdSelftest();
  printAbInstructions(baseUrl, model);

  console.log(`Probing ${baseUrl} (no CM API key)…`);
  const probe = await probeEndpoint(baseUrl);
  if (probe.ok) {
    console.log(`Inference probe: HTTP ${probe.status}`);
  } else {
    console.warn("Inference probe failed — writing protocol stub (scores stay null).");
  }

  if (!stubOnly && probe.ok) {
    const proj = path.join(HERE, "ContextMemory.Bench", "ContextMemory.Bench.csproj");
    if (!fs.existsSync(proj)) {
      die(`Missing live runner project: ${proj}`);
    }
    console.log("Starting in-process harness A/B (dotnet)…");
    const args = ["run", "--project", proj, "-c", "Release", "--", "--label", label];
    if (typeof flags.only === "string") {
      args.push("--only", flags.only);
    }
    const r = spawnSync("dotnet", args, {
      cwd: path.resolve(HERE, ".."),
      env: process.env,
      stdio: "inherit",
      shell: true,
    });
    if (r.status !== 0) {
      die(`Live A/B runner failed with exit ${r.status}`);
    }
    return;
  }

  fs.mkdirSync(RESULTS_DIR, { recursive: true });
  const doc = buildAbDocument({
    label,
    reachable: probe.ok,
    baseUrl,
    model,
    tasks,
  });
  const outPath = path.join(RESULTS_DIR, `${doc.runId}.json`);
  fs.writeFileSync(outPath, JSON.stringify(doc, null, 2) + "\n", "utf8");
  fs.writeFileSync(
    path.join(RESULTS_DIR, "latest.json"),
    JSON.stringify({ path: outPath, runId: doc.runId }, null, 2) + "\n"
  );
  console.log(`Wrote ${outPath}`);
  console.log(
    stubOnly || !probe.ok
      ? "Scores left null (stub / unreachable)."
      : "Scores left null until harness A/B fills metrics (honest — no placeholder numbers)."
  );
}

function loadResultDocs(target) {
  const docs = [];
  if (!target) {
    const latestPtr = path.join(RESULTS_DIR, "latest.json");
    if (fs.existsSync(latestPtr)) {
      const ptr = JSON.parse(fs.readFileSync(latestPtr, "utf8"));
      const p = ptr.path || path.join(RESULTS_DIR, `${ptr.runId}.json`);
      if (fs.existsSync(p)) docs.push(JSON.parse(fs.readFileSync(p, "utf8")));
    }
    if (docs.length === 0 && fs.existsSync(RESULTS_DIR)) {
      const files = fs
        .readdirSync(RESULTS_DIR)
        .filter((f) => f.endsWith(".json") && f !== "latest.json")
        .sort();
      for (const f of files) {
        docs.push(JSON.parse(fs.readFileSync(path.join(RESULTS_DIR, f), "utf8")));
      }
    }
    return docs;
  }
  const abs = path.isAbsolute(target) ? target : path.join(process.cwd(), target);
  if (fs.statSync(abs).isDirectory()) {
    for (const f of fs.readdirSync(abs).filter((x) => x.endsWith(".json") && x !== "latest.json")) {
      docs.push(JSON.parse(fs.readFileSync(path.join(abs, f), "utf8")));
    }
  } else {
    docs.push(JSON.parse(fs.readFileSync(abs, "utf8")));
  }
  return docs;
}

function fmt(v) {
  if (v === null || v === undefined) return "—";
  if (typeof v === "boolean") return v ? "yes" : "no";
  return String(v);
}

function cmdReport(pos) {
  const docs = loadResultDocs(pos[0]);
  if (!docs.length) {
    die("No results found. Run: node benchmarks/bench.mjs ab");
  }

  for (const doc of docs) {
    console.log(`\n# ${doc.runId || "result"}`);
    console.log(
      `Created: ${doc.createdAt || "?"} · reachable=${doc.reachable === true} · model=${doc.model || "?"}`
    );
    if (doc.summary?.note) console.log(`Note: ${doc.summary.note}`);
    console.log("");
    console.log(
      "| Task | Off score | On score | Δ | Off stuck | On stuck | Off badArgs | On badArgs | Off falseDone | On falseDone |"
    );
    console.log("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
    for (const t of doc.tasks || []) {
      const off = t.off || {};
      const on = t.on || {};
      const delta =
        off.score != null && on.score != null ? (Number(on.score) - Number(off.score)).toFixed(1) : "—";
      console.log(
        `| ${t.id} | ${fmt(off.score)} | ${fmt(on.score)} | ${delta} | ${fmt(off.stuck)} | ${fmt(on.stuck)} | ${fmt(off.badArgs)} | ${fmt(on.badArgs)} | ${fmt(off.falseDone)} | ${fmt(on.falseDone)} |`
      );
    }
    const s = doc.summary || {};
    console.log("");
    console.log(
      `Summary mean: off=${fmt(s.meanScoreOff)} on=${fmt(s.meanScoreOn)} Δ=${fmt(s.deltaScore)}`
    );
  }
}

function usage() {
  console.log(`Usage:
  node benchmarks/bench.mjs list
  node benchmarks/bench.mjs selftest
  node benchmarks/bench.mjs ab [--label name]
  node benchmarks/bench.mjs report [results.json|results/]

CM_BENCH_API_KEY is optional — set it only when the API under test requires a key.
`);
}

async function main() {
  const { pos, flags } = parseArgs(process.argv.slice(2));
  const cmd = (pos[0] || "").toLowerCase();
  if (!cmd || flags.help || flags.h) {
    usage();
    process.exit(cmd ? 0 : 1);
  }
  if (cmd === "list") cmdList();
  else if (cmd === "selftest") cmdSelftest();
  else if (cmd === "ab") await cmdAb(flags);
  else if (cmd === "report") cmdReport(pos.slice(1));
  else {
    usage();
    die(`Unknown command: ${cmd}`);
  }
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
