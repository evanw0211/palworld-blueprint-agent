// Self-contained offline thumbnail renderer (no game, no MCP, no pak export step).
//
//   node blueprints/tooling/render_offline.mjs <blueprint.json> <out.png> [width] [height]
//
// Serves this workspace over a local port, renders the blueprint with the real GLBs in
// blueprints/meshes/ using the mod's camera math (BlueprintThumbnailRenderer:
// FOV 40, FRAME_FILL 0.86, forward (4.2,4.2,-3.0)), screenshots through an existing
// Chrome DevTools endpoint on 127.0.0.1:9222 and exits.
import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, "..");               // agent kit root
const [, , bpArg, outArg, wArg, hArg] = process.argv;
if (!bpArg || !outArg) { console.error("usage: <blueprint.json> <out.png> [w] [h]"); process.exit(2); }
const W = parseInt(wArg || "1280", 10);
const H = parseInt(hArg || "720", 10);

const MIME = { ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8",
               ".json": "application/json; charset=utf-8", ".glb": "model/gltf-binary", ".png": "image/png" };

function startServer() {
  return new Promise((resolve, reject) => {
    const server = http.createServer((req, res) => {
      let rel;
      try { rel = decodeURIComponent(new URL(req.url, "http://x").pathname); }
      catch { res.writeHead(400); res.end(); return; }
      const file = path.join(ROOT, rel);
      if (!file.startsWith(ROOT) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) {
        res.writeHead(404); res.end("not found"); return;
      }
      res.writeHead(200, { "content-type": MIME[path.extname(file).toLowerCase()] || "application/octet-stream" });
      fs.createReadStream(file).pipe(res);
    });
    server.on("error", reject);
    server.listen(0, "127.0.0.1", () => resolve(server));
  });
}

async function cdpTarget(url) {
  const list = await (await fetch("http://127.0.0.1:9222/json/list")).json();
  const page = list.find(t => t.type === "page" && t.url.includes("reddit"))
            || list.find(t => t.type === "page");
  if (!page) throw new Error("no page target on 9222");
  const ws = new WebSocket(page.webSocketDebuggerUrl);
  let id = 0; const pending = new Map();
  const send = (method, params = {}) => new Promise((res, rej) => {
    const mid = ++id; pending.set(mid, { res, rej });
    ws.send(JSON.stringify({ id: mid, method, params }));
  });
  ws.addEventListener("message", (ev) => {
    const msg = JSON.parse(ev.data);
    if (msg.id && pending.has(msg.id)) {
      const { res, rej } = pending.get(msg.id); pending.delete(msg.id);
      msg.error ? rej(new Error(JSON.stringify(msg.error))) : res(msg.result);
    }
  });
  await new Promise(r => ws.addEventListener("open", r));
  await send("Page.enable");
  await send("Emulation.setDeviceMetricsOverride", { width: W, height: H, deviceScaleFactor: 1, mobile: false });
  await send("Page.navigate", { url });
  const evalJs = async (expr) => {
    const r = await send("Runtime.evaluate", { expression: expr, returnByValue: true, awaitPromise: true, timeout: 30000 });
    if (r.exceptionDetails) return "ERR:" + (r.exceptionDetails.exception?.description || "");
    return r.result.value;
  };
  let done = false;
  for (let i = 0; i < 90; i++) {
    await new Promise(r => setTimeout(r, 1000));
    if (await evalJs("window.__renderDone === true") === true) { done = true; break; }
  }
  const status = await evalJs("JSON.stringify(window.__renderStatus || null)");
  const shot = await send("Page.captureScreenshot", { format: "png", fromSurface: true });
  ws.close();
  return { done, status, data: shot.data };
}

const server = await startServer();
const port = server.address().port;
const bpRel = "/" + path.relative(ROOT, path.resolve(bpArg)).replace(/\\/g, "/");
const bust = Date.now();
const pageUrl = `http://127.0.0.1:${port}/render/modsrender.html`
  + `?bp=${encodeURIComponent(bpRel)}`
  + `&idmap=${encodeURIComponent("/meshes/_id_to_bp.json?v=" + bust)}`
  + `&offsets=${encodeURIComponent("/meshes/_actor_offsets.json?v=" + bust)}`
  + `&meshes=${encodeURIComponent("/meshes")}`
  + `&w=${W}&h=${H}`;
try {
  const { done, status, data } = await cdpTarget(pageUrl);
  fs.mkdirSync(path.dirname(path.resolve(outArg)), { recursive: true });
  fs.writeFileSync(outArg, Buffer.from(data, "base64"));
  console.log(JSON.stringify({ ok: done, output: path.resolve(outArg), status: JSON.parse(status || "null") }));
  process.exit(done ? 0 : 1);
} finally {
  server.close();
}

