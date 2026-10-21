// Self-contained offline thumbnail renderer (no game, no MCP, no pak export step).
//
//   node blueprints/tooling/render_offline.mjs <blueprint.json> <out.png> [width] [height]
//
// Serves this workspace over a local port, renders the blueprint with the real GLBs in
// meshes/ using the mod's camera math (BlueprintThumbnailRenderer: FOV 40,
// FRAME_FILL 0.86, forward (4.2,4.2,-3.0)), screenshots through a Chrome DevTools
// endpoint (default 127.0.0.1:9222, override with RENDER_CDP_PORT) and exits.
// Every run opens its own background tab and closes it again; an existing tab is never
// navigated. Exit codes: 0 screenshot written, 1 render failed, 2 usage error.
import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, "..");               // agent kit root
// Positional args keep their original meaning; --set/--views are additive pass-throughs
// to the render page, which is what makes free-camera inspection possible without
// changing any existing invocation.
//
// Exit-code contract (matches tooling/README.md): 0 = screenshot written, 1 = render did
// not produce a valid screenshot, 2 = usage error (nothing rendered, no output file).
const USAGE = 'usage: node tooling/render_offline.mjs <blueprint.json> <out.png> [w] [h] '
  + '[--views N|"az,el;az,el"] [--set k=v ...]';
function usageError(msg) {
  console.error(msg + "\n" + USAGE);
  process.exit(2);
}
const NUMERIC_KEYS = new Set(["az", "el", "dist", "fit", "w", "h"]);
function checkViewsArg(value) {
  if (/^\d+$/.test(value)) {
    const n = parseInt(value, 10);
    if (n < 2 || n > 16) usageError("--views N 须在 2..16，收到 " + JSON.stringify(value));
    return;
  }
  const pairs = value.split(";").map(s => s.split(",").map(Number));
  if (!pairs.length || pairs.some(p => p.length < 2 || !p.every(Number.isFinite))) {
    usageError('--views 须为 N 或 "az,el;az,el"，收到 ' + JSON.stringify(value));
  }
  if (pairs.some(([, el]) => el < -90 || el > 90)) usageError("--views 的 el 须在 -90..90");
}
function parseSize(raw, def, name) {
  if (raw === undefined) return def;
  if (!/^\d+$/.test(raw) || parseInt(raw, 10) < 1) usageError(name + " 须为正整数，收到 " + JSON.stringify(raw));
  return parseInt(raw, 10);
}
const positional = [];
const extra = new URLSearchParams();
const argv = process.argv.slice(2);
for (let i = 0; i < argv.length; i++) {
  const a = argv[i];
  if (a === "--set" || a.startsWith("--set=")) {
    const kv = a === "--set" ? argv[++i] : a.slice("--set=".length);
    if (kv === undefined || kv === "") usageError("--set 需要 k=v");
    if (kv.startsWith("--")) usageError("--set 缺少值: " + kv);
    const j = kv.indexOf("=");
    if (j <= 0) usageError('--set 需要 k=v（缺少 "=" 或键为空）: ' + kv);
    const key = kv.slice(0, j), val = kv.slice(j + 1);
    if (val === "") usageError("--set 的值不能为空: " + key);
    if (NUMERIC_KEYS.has(key) && (val.trim() === "" || !Number.isFinite(Number(val)))) {
      usageError("--set " + key + " 需要数字，收到 " + JSON.stringify(val));
    }
    extra.set(key, val);
  } else if (a === "--views" || a.startsWith("--views=")) {
    const val = a === "--views" ? argv[++i] : a.slice("--views=".length);
    if (val === undefined || val === "") usageError("--views 需要 N 或 \"az,el;az,el\"");
    if (val.startsWith("--")) usageError("--views 缺少值: " + val);
    checkViewsArg(val);
    extra.set("views", val);
  } else if (a === "--help" || a === "-h") {
    console.log(USAGE);
    process.exit(0);
  } else if (a.startsWith("--")) {
    usageError("未知参数: " + a);
  } else {
    positional.push(a);
  }
}
const [bpArg, outArg, wArg, hArg] = positional;
if (!bpArg || !outArg) usageError("缺少 <blueprint.json> 或 <out.png>");
if (positional.length > 4) usageError("位置参数过多: " + positional.slice(4).join(" "));
const W = parseSize(wArg, 1280, "w");
const H = parseSize(hArg, 720, "h");

const MIME = { ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8",
               ".json": "application/json; charset=utf-8", ".glb": "model/gltf-binary", ".png": "image/png" };

// Only these top-level directories are served. The render page needs render/ + vendor/ +
// meshes/ (+ out/ for texmaps); blueprints may live in examples/, out/ or authoring/.
const ALLOWED_PREFIXES = ["examples/", "out/", "authoring/", "render/", "vendor/", "meshes/"];
const REAL_ROOT = fs.realpathSync(ROOT);

// Resolve a request path to a real file inside one of ALLOWED_PREFIXES, or null (404).
// Rejects `..`, NUL, backslash and symlink escapes via a realpath containment check.
function resolveRequestPath(rawPath) {
  let rel;
  try { rel = decodeURIComponent(rawPath); } catch { return null; }
  if (!rel.startsWith("/") || rel.includes("\0") || rel.includes("\\")) return null;
  const parts = rel.slice(1).split("/");
  if (parts.some(seg => seg === "" || seg === "." || seg === "..")) return null;
  if (!ALLOWED_PREFIXES.includes(parts[0] + "/")) return null;
  const full = path.resolve(REAL_ROOT, ...parts);
  if (!fs.existsSync(full)) return null;
  let real;
  try { real = fs.realpathSync(full); } catch { return null; }
  const relToRoot = path.relative(REAL_ROOT, real);
  if (relToRoot === "" || relToRoot.startsWith("..") || path.isAbsolute(relToRoot)) return null;
  try { if (!fs.statSync(real).isFile()) return null; } catch { return null; }
  return real;
}

function startServer() {
  return new Promise((resolve, reject) => {
    const server = http.createServer((req, res) => {
      let pathname;
      try { pathname = new URL(req.url, "http://x").pathname; } catch { res.writeHead(400); res.end(); return; }
      const file = resolveRequestPath(pathname);
      if (!file) { res.writeHead(404); res.end("not found"); return; }
      res.writeHead(200, { "content-type": MIME[path.extname(file).toLowerCase()] || "application/octet-stream" });
      fs.createReadStream(file).pipe(res);
    });
    server.on("error", reject);
    server.listen(0, "127.0.0.1", () => resolve(server));
  });
}

// ---- CDP client ----------------------------------------------------------
// The script owns a fresh tab (never navigates a tab the user already has), every
// request is bounded by a timeout, and a closed WebSocket rejects all pending calls.
const CDP_PORT = Number(process.env.RENDER_CDP_PORT || process.env.CDP_PORT || 9222);
if (!Number.isInteger(CDP_PORT) || CDP_PORT < 1 || CDP_PORT > 65535) {
  usageError("RENDER_CDP_PORT 须为 1..65535 的整数，收到 " + (process.env.RENDER_CDP_PORT || process.env.CDP_PORT));
}
const CDP_BASE = `http://127.0.0.1:${CDP_PORT}`;
const FETCH_TIMEOUT_MS = Number(process.env.RENDER_CDP_TIMEOUT_MS || 10000);
const PAGE_TIMEOUT_MS = Number(process.env.RENDER_PAGE_TIMEOUT_MS || 90000);
if (!Number.isFinite(FETCH_TIMEOUT_MS) || FETCH_TIMEOUT_MS <= 0) usageError("RENDER_CDP_TIMEOUT_MS 须为正数");
if (!Number.isFinite(PAGE_TIMEOUT_MS) || PAGE_TIMEOUT_MS <= 0) usageError("RENDER_PAGE_TIMEOUT_MS 须为正数");
const sleep = ms => new Promise(r => setTimeout(r, ms));

async function cdpFetch(url, opts = {}, ms = FETCH_TIMEOUT_MS) {
  let res;
  try {
    res = await fetch(url, { ...opts, signal: AbortSignal.timeout(ms) });
  } catch (e) {
    const cause = e && e.cause && e.cause.message ? ": " + e.cause.message : "";
    throw new Error(`${url} 失败: ${e && e.message || e}${cause}`);
  }
  if (!res.ok) throw new Error(`${url} -> HTTP ${res.status}`);
  return res;
}
async function cdpJson(url, opts, ms) { return (await cdpFetch(url, opts, ms)).json(); }

async function createTarget() {
  // Chrome 111+ requires PUT /json/new; the query string *is* the URL to open.
  try { return await cdpJson(`${CDP_BASE}/json/new?about:blank`, { method: "PUT" }); }
  catch (e1) {
    try { return await cdpJson(`${CDP_BASE}/json/new?about:blank`); }
    catch (e2) {
      throw new Error(`无法在 ${CDP_BASE} 新建标签页: ${e1.message} / ${e2.message}`);
    }
  }
}
async function closeTarget(id) {
  if (!id) return;
  try { await cdpFetch(`${CDP_BASE}/json/close/${encodeURIComponent(id)}`, {}, 3000); } catch { /* best effort */ }
}

function connectWs(wsUrl) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(wsUrl);
    const pending = new Map();
    let seq = 0, opened = false, closed = false, connectTimer = null;
    const failAll = (err) => {
      for (const p of pending.values()) { clearTimeout(p.timer); p.reject(err); }
      pending.clear();
    };
    const send = (method, params = {}, ms = 30000) => new Promise((res, rej) => {
      if (closed) return rej(new Error("CDP WebSocket 已关闭"));
      const id = ++seq;
      const timer = setTimeout(() => {
        pending.delete(id);
        rej(new Error(`CDP ${method} 超时（${ms}ms）`));
      }, ms);
      pending.set(id, {
        timer,
        resolve: v => { clearTimeout(timer); res(v); },
        reject: e => { clearTimeout(timer); rej(e); },
      });
      try { ws.send(JSON.stringify({ id, method, params })); }
      catch (e) { clearTimeout(timer); pending.delete(id); rej(e); }
    });
    ws.addEventListener("message", (ev) => {
      let msg;
      try { msg = JSON.parse(ev.data); } catch { return; }
      const p = pending.get(msg.id);
      if (!p) return;
      pending.delete(msg.id);
      msg.error ? p.reject(new Error(JSON.stringify(msg.error))) : p.resolve(msg.result);
    });
    ws.addEventListener("error", () => {
      const err = new Error("CDP WebSocket error");
      failAll(err);
      if (!opened) reject(err);
    });
    ws.addEventListener("close", () => {
      closed = true;
      const err = new Error("CDP WebSocket closed");
      failAll(err);
      if (!opened) reject(err);
    });
    ws.addEventListener("open", () => {
      opened = true;
      clearTimeout(connectTimer);
      resolve({ send, close: () => { try { ws.close(); } catch { /* ignore */ } } });
    }, { once: true });
    const connectTimerRef = setTimeout(() => {
      if (!opened) { try { ws.close(); } catch { /* ignore */ } reject(new Error("CDP WebSocket 连接超时")); }
    }, FETCH_TIMEOUT_MS);
    connectTimer = connectTimerRef;
  });
}

async function cdpRender(pageUrl) {
  const target = await createTarget();
  let conn = null;
  try {
    if (!target || !target.webSocketDebuggerUrl) throw new Error("新标签页没有 webSocketDebuggerUrl");
    conn = await connectWs(target.webSocketDebuggerUrl);
    const { send } = conn;
    const evalJs = async (expr, ms = 30000) => {
      const r = await send("Runtime.evaluate",
        { expression: expr, returnByValue: true, awaitPromise: true, timeout: ms }, ms + 2000);
      if (r && r.exceptionDetails) return "ERR:" + (r.exceptionDetails.exception && r.exceptionDetails.exception.description || "");
      return r && r.result ? r.result.value : undefined;
    };
    await send("Page.enable");
    await send("Runtime.enable").catch(() => { /* exceptions are reported via status */ });
    await send("Emulation.setDeviceMetricsOverride", { width: W, height: H, deviceScaleFactor: 1, mobile: false });
    await send("Page.navigate", { url: pageUrl });
    const deadline = Date.now() + PAGE_TIMEOUT_MS;
    let done = false;
    while (Date.now() < deadline) {
      await sleep(1000);
      if (await evalJs("window.__renderDone === true") === true) { done = true; break; }
    }
    // The page reports fatal errors through `__renderFailed` + `__renderStatus.error`,
    // usage errors through `__renderExit = 2`; none of those may count as success.
    const failed = (await evalJs("window.__renderFailed === true")) === true;
    const pageExit = Number(await evalJs("window.__renderExit || 0")) || 0;
    const status = await evalJs("JSON.stringify(window.__renderStatus || null)");
    const shot = failed ? null : await send("Page.captureScreenshot", { format: "png", fromSurface: true }, 60000);
    return { done, failed, pageExit, status, data: shot ? shot.data : null };
  } finally {
    if (conn) conn.close();
    await closeTarget(target && target.id);
  }
}

const bpAbs = path.resolve(bpArg);
const bpRel = "/" + path.relative(ROOT, bpAbs).replace(/\\/g, "/");
if (!fs.existsSync(bpAbs) || !fs.statSync(bpAbs).isFile()) {
  console.error("render failed: 蓝图不存在: " + bpAbs);
  process.exit(1);
}
if (bpRel.startsWith("/..") || !ALLOWED_PREFIXES.includes(bpRel.slice(1).split("/")[0] + "/")) {
  console.error("render failed: 蓝图须位于 " + ALLOWED_PREFIXES.join(" ") + " 之下（收到 " + bpRel + "）");
  process.exit(1);
}

const server = await startServer();
const port = server.address().port;
const bust = Date.now();
// Defaults first, then --set overrides them. This is what lets a caller point the page at
// an alternate asset set (e.g. a complete-geometry overlay dir) without another flag.
const q = new URLSearchParams();
q.set("bp", bpRel);
q.set("idmap", "/meshes/_id_to_bp.json?v=" + bust);
q.set("offsets", "/meshes/_actor_offsets.json?v=" + bust);
q.set("meshes", "/meshes");
q.set("w", String(W));
q.set("h", String(H));
for (const [k, v] of extra) q.set(k, v);          // --set wins over the default above
const pageUrl = `http://127.0.0.1:${port}/render/modsrender.html?${q.toString()}`;
// Exit by setting process.exitCode and letting the loop drain: calling process.exit()
// while the WebSocket/CDP handles are closing aborts libuv on Windows (uv_async.c:94).
let exitCode = 1;
try {
  const { done, failed, pageExit, status, data } = await cdpRender(pageUrl);
  const parsed = JSON.parse(status || "null");
  const pageError = parsed && parsed.error ? String(parsed.error) : null;
  if (failed || pageExit || pageError) {
    exitCode = pageExit || 1;
    console.error(JSON.stringify({ ok: false, exit: exitCode, output: null, status: parsed }));
    console.error("render failed: " + (pageError || "页面报告 __renderFailed"));
  } else if (!done) {
    fs.mkdirSync(path.dirname(path.resolve(outArg)), { recursive: true });
    fs.writeFileSync(outArg, Buffer.from(data || "", "base64"));
    console.error(JSON.stringify({ ok: false, exit: 1, output: path.resolve(outArg), status: parsed }));
    console.error("render failed: 页面在 " + PAGE_TIMEOUT_MS + "ms 内没有设置 __renderDone（截图可能不完整）");
  } else {
    fs.mkdirSync(path.dirname(path.resolve(outArg)), { recursive: true });
    fs.writeFileSync(outArg, Buffer.from(data, "base64"));
    console.log(JSON.stringify({ ok: true, output: path.resolve(outArg), status: parsed }));
    exitCode = 0;
  }
} catch (e) {
  console.error("render failed: " + (e && e.stack || e));
} finally {
  server.close();
  if (typeof server.closeAllConnections === "function") server.closeAllConnections();
}
process.exitCode = exitCode;

