// Farm-stack blueprint core: schema-9 build, PWB9D codec, offline checks.
// Offline, dependency-free. Usable from the page (<script src="core.js">),
// from Node (require), or copied into a console. Everything is pure data.
(function (root, factory) {
  if (typeof module === "object" && module.exports) module.exports = factory();
  else root.FarmStack = factory();
})(typeof self !== "undefined" ? self : this, function () {
  "use strict";

  var PITCH = 48.777;               // Wooden Bench seat height (cm), measured mesh bbox
  var PROP_ANY = 1;                 // ConnectInfoAnyPlace
  var CONN_IDX = "254";
  var FARM_HEIGHT = 41.673;         // farm block mesh height (cm)

  var CROPS = [
    { id: "FarmBlockV2_wheet",  label: "小麦",   short: "麦", color: "#d9b64a" },
    { id: "FarmBlockV2_Potato", label: "土豆",   short: "土", color: "#b98b5e" },
    { id: "FarmBlockV2_Onion",  label: "洋葱",   short: "葱", color: "#c98fb3" },
    { id: "FarmBlockV2_Carrot", label: "胡萝卜", short: "萝", color: "#e08a3c" },
    { id: "FarmBlockV2_Lettuce",label: "生菜",   short: "生", color: "#7fbf5f" },
    { id: "FarmBlockV2_tomato", label: "番茄",   short: "茄", color: "#d94f4f" },
    { id: "FarmBlockV2_Berries",label: "野莓",   short: "莓", color: "#8f6bd6" }
  ];
  var CROP_BY_ID = {};
  CROPS.forEach(function (c) { CROP_BY_ID[c.id] = c; });

  var ALIASES = {
    wheet: "FarmBlockV2_wheet", wheat: "FarmBlockV2_wheet",
    potato: "FarmBlockV2_Potato", onion: "FarmBlockV2_Onion",
    carrot: "FarmBlockV2_Carrot", lettuce: "FarmBlockV2_Lettuce",
    tomato: "FarmBlockV2_tomato", berries: "FarmBlockV2_Berries",
    berry: "FarmBlockV2_Berries"
  };

  // Material cost per farm block, from data/catalog.json (game data table).
  var MATERIALS = {
    "FarmBlockV2_wheet":   { "WheatSeeds": 3, "Wood": 15, "stone": 15 },
    "FarmBlockV2_Potato":  { "PotatoSeeds": 3, "Wood": 30, "stone": 20, "PalFluid": 3 },
    "FarmBlockV2_Onion":   { "OnionSeeds": 3, "Wood": 30, "stone": 20, "PalFluid": 2 },
    "FarmBlockV2_Carrot":  { "CarrotSeeds": 3, "Wood": 30, "stone": 20, "PalFluid": 1 },
    "FarmBlockV2_Lettuce": { "LettuceSeeds": 3, "Wood": 100, "stone": 70, "PalFluid": 10 },
    "FarmBlockV2_tomato":  { "TomatoSeeds": 3, "Wood": 70, "stone": 50, "PalFluid": 5 },
    "FarmBlockV2_Berries": { "BerrySeeds": 3, "Wood": 10, "stone": 10 }
  };

  // ---------------------------------------------------------------- numbers

  function roundTo(value, places) {
    var f = Math.pow(10, places);
    var r = Math.round(value * f) / f;
    if (Math.abs(r - Math.round(r)) < 1e-9) return Math.round(r);
    return r;
  }
  function tidy(value, places) { return roundTo(value, places == null ? 3 : places); }
  function qYaw(deg) {
    var h = deg * Math.PI / 360;
    return [0, 0, roundTo(Math.sin(h), 7), roundTo(Math.cos(h), 7)];
  }
  function yawDeg(q) {
    var x = q[0], y = q[1], z = q[2], w = q[3];
    return Math.atan2(2 * (w * z + x * y), 1 - 2 * (y * y + z * z)) * 180 / Math.PI;
  }
  function qmul(a, b) {
    return [
      a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1],
      a[3] * b[1] - a[0] * b[2] + a[1] * b[3] + a[2] * b[0],
      a[3] * b[2] + a[0] * b[1] - a[1] * b[0] + a[2] * b[3],
      a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2]
    ];
  }
  function qconj(q) { return [-q[0], -q[1], -q[2], q[3]]; }
  function qrot(q, v) {
    var p = qmul(qmul(q, [v[0], v[1], v[2], 0]), qconj(q));
    return [p[0], p[1], p[2]];
  }
  function normQ(q) {
    var m = Math.hypot(q[0], q[1], q[2], q[3]) || 1;
    return [q[0] / m, q[1] / m, q[2] / m, q[3] / m];
  }

  function normalizeCrop(value) {
    if (typeof value !== "string" || !value.trim()) throw new Error("作物 id 不能为空");
    var key = value.trim();
    var low = key.toLowerCase();
    if (CROP_BY_ID[key]) return key;
    if (ALIASES[low]) return ALIASES[low];
    for (var i = 0; i < CROPS.length; i++) {
      if (CROPS[i].id.toLowerCase() === low) return CROPS[i].id;
    }
    throw new Error("未知作物: " + value);
  }
  function cropMeta(id) { return CROP_BY_ID[id] || { id: id, label: id, short: "?", color: "#777" }; }

  // ---------------------------------------------------------------- building

  function chainEdges(n) {
    var edges = [], i;
    if (n >= 2) edges.push([1, 2, PROP_ANY, CONN_IDX, CONN_IDX]);
    for (i = 2; i < n; i++) {
      edges.push([i, i + 1, PROP_ANY, CONN_IDX, CONN_IDX]);
      edges.push([i, i - 1, PROP_ANY, CONN_IDX, CONN_IDX]);
    }
    if (n >= 2) edges.push([n, n - 1, PROP_ANY, CONN_IDX, CONN_IDX]);
    return edges;
  }

  function defaultCrops(layers) {
    // The sequence of the known 11-layer code, cycled.
    var base = [
      "FarmBlockV2_wheet", "FarmBlockV2_wheet", "FarmBlockV2_wheet",
      "FarmBlockV2_Potato", "FarmBlockV2_Onion",
      "FarmBlockV2_Carrot", "FarmBlockV2_Carrot",
      "FarmBlockV2_Lettuce",
      "FarmBlockV2_tomato", "FarmBlockV2_tomato", "FarmBlockV2_tomato"
    ];
    var out = [];
    for (var i = 0; i < Math.max(1, layers || 1); i++) out.push(base[i % base.length]);
    return out;
  }

  function build(options) {
    options = options || {};
    var layers = Math.max(1, Math.floor(options.layers == null ? 22 : options.layers));
    var pitch = options.pitch == null ? PITCH : Number(options.pitch);
    if (!isFinite(pitch) || pitch <= 0) throw new Error("层间距必须为正数");
    var crops = (options.crops && options.crops.length ? options.crops : defaultCrops(layers)).map(normalizeCrop);
    var yawMode = options.yaw === "zero" ? "zero" : "alt";

    var types = [], b = [];
    for (var i = 0; i < layers; i++) {
      var crop = crops[i % crops.length];
      var typeIndex = types.indexOf(crop);
      if (typeIndex < 0) { types.push(crop); typeIndex = types.length - 1; }
      var deg = yawMode === "zero" ? 0 : (i % 2 === 0 ? 0 : 180);
      var q = qYaw(deg);
      b.push([
        typeIndex + 1, 0, 0, tidy(i * pitch),
        tidy(q[0], 7), tidy(q[1], 7), tidy(q[2], 7), tidy(q[3], 7)
      ]);
    }
    var bp = { v: 9, t: types, b: b, a: 1, c: chainEdges(layers) };
    if (options.world) bp.w = options.world;
    if (options.support) bp.x = options.support;
    return bp;
  }

  function localFrame(bp, rec) {
    var anchor = bp.b[bp.a - 1];
    var qa = normQ(anchor.slice(4, 8));
    var qinv = qconj(qa);
    var pos = qrot(qinv, [rec[1] - anchor[1], rec[2] - anchor[2], rec[3] - anchor[3]]);
    var quat = qmul(qinv, rec.slice(4, 8));
    return { pos: pos, quat: quat };
  }

  function summarize(bp) {
    var rows = [];
    for (var i = 0; i < bp.b.length; i++) {
      var lf = localFrame(bp, bp.b[i]);
      rows.push({
        index: i + 1,
        type: bp.t[bp.b[i][0] - 1],
        x: lf.pos[0], y: lf.pos[1], z: lf.pos[2],
        yaw: yawDeg(lf.quat)
      });
    }
    rows.sort(function (a, b2) { return a.z - b2.z || a.index - b2.index; });
    var zs = rows.map(function (r) { return r.z; });
    var diffs = [];
    for (var k = 1; k < zs.length; k++) diffs.push(tidy(zs[k] - zs[k - 1]));
    var positive = diffs.filter(function (d) { return d > 0.5; }).sort(function (a, b2) { return a - b2; });
    var pitch = positive.length ? positive[Math.floor(positive.length / 2)] : null;
    var used = {};
    rows.forEach(function (r) { used[r.type] = (used[r.type] || 0) + 1; });
    return {
      rows: rows, layers: rows.length, pitch: pitch, deltas: diffs,
      crops: rows.map(function (r) { return r.type; }),
      counts: used,
      totalHeight: rows.length > 1 ? tidy(rows[rows.length - 1].z - rows[0].z) : 0,
      anchorIndex: bp.a, hasWorld: bp.w != null, hasSupport: !!(bp.x && bp.x.length)
    };
  }

  // Build from an existing blueprint: reuse its layer z / crop cycle / yaw and
  // optionally its world transform (w) and external support stubs (x).
  function buildFromTemplate(template, options) {
    options = options || {};
    var info = summarize(template);
    var rows = info.rows;
    var layers = Math.max(1, Math.floor(options.layers == null ? rows.length : options.layers));
    var pitch = options.pitch == null ? info.pitch : Number(options.pitch);
    if (!isFinite(pitch) || pitch <= 0) throw new Error("模板层间距不可用，请手动填写层间距");
    var keepWorld = options.keepWorld === true && template.w != null;
    var keepSupport = options.keepSupport === true && !!(template.x && template.x.length);
    var yawMode = options.yaw || "template";
    var crops = options.crops && options.crops.length
      ? options.crops.map(normalizeCrop)
      : rows.map(function (r) { return r.type; });
    var yawSeq = rows.map(function (r) { return r.yaw; });
    var baseCount = Math.min(layers, rows.length);
    var zLast = rows.length ? rows[rows.length - 1].z : 0;

    var types = [], b = [], i;
    for (i = 0; i < layers; i++) {
      var z, deg;
      if (i < baseCount) {
        z = rows[i].z;
        deg = yawMode === "template" ? yawSeq[i] : (yawMode === "zero" ? 0 : (i % 2 ? 180 : 0));
      } else {
        z = tidy(zLast + pitch * (i - baseCount + 1));
        deg = yawMode === "template" ? yawSeq[i % yawSeq.length] : (yawMode === "zero" ? 0 : (i % 2 ? 180 : 0));
      }
      var crop = crops[i % crops.length];
      var typeIndex = types.indexOf(crop);
      if (typeIndex < 0) { types.push(crop); typeIndex = types.length - 1; }
      var q = qYaw(deg);
      b.push([typeIndex + 1, 0, 0, z, tidy(q[0], 7), tidy(q[1], 7), tidy(q[2], 7), tidy(q[3], 7)]);
    }

    var out = { v: 9, t: types, b: b, a: 1, c: chainEdges(layers) };
    if (keepSupport) {
      var x = JSON.parse(JSON.stringify(template.x));
      x.forEach(function (edge) {
        if (edge[0] > layers) edge[0] = 1;
        var targetType = template.t[edge[1] - 1];
        var idx = types.indexOf(targetType);
        if (idx < 0) { types.push(targetType); idx = types.length - 1; }
        edge[1] = idx + 1;
      });
      out.x = x;
    }
    if (keepWorld) out.w = JSON.parse(JSON.stringify(template.w));
    return out;
  }

  // ---------------------------------------------------------------- checks

  function validate(bp) {
    var problems = [], warnings = [], stats = {};
    if (!bp || typeof bp !== "object") return { ok: false, problems: ["蓝图不是对象"], warnings: warnings, stats: stats };
    if (bp.v !== 9) problems.push("schema 版本不是 9");
    if (!Array.isArray(bp.t) || !bp.t.length) problems.push("类型表为空");
    if (!Array.isArray(bp.b) || !bp.b.length) problems.push("建筑数组为空");
    var t = bp.t || [], b = bp.b || [];
    for (var i = 0; i < t.length; i++) {
      if (typeof t[i] !== "string" || !t[i]) problems.push("t[" + (i + 1) + "] 不是 BuildObjectId");
    }
    for (var j = 0; j < b.length; j++) {
      var rec = b[j];
      if (!Array.isArray(rec) || (rec.length !== 8 && rec.length !== 11)) {
        problems.push("b[" + (j + 1) + "] 必须是 8 或 11 个数"); continue;
      }
      if (!Number.isInteger(rec[0]) || rec[0] < 1 || rec[0] > t.length) problems.push("b[" + (j + 1) + "] 类型下标越界");
      for (var k = 1; k < rec.length; k++) {
        if (typeof rec[k] !== "number" || !isFinite(rec[k])) { problems.push("b[" + (j + 1) + "] 含非有限数"); break; }
      }
    }
    if (!Number.isInteger(bp.a) || bp.a < 1 || bp.a > b.length) problems.push("锚点下标无效");
    var edges = bp.c || [];
    if (!Array.isArray(edges)) problems.push("c 必须是数组");
    edges.forEach(function (e) {
      if (!Array.isArray(e) || e.length !== 5) { problems.push("连接记录格式错误"); return; }
      if (!Number.isInteger(e[0]) || e[0] < 1 || e[0] > b.length) problems.push("连接源下标越界: " + JSON.stringify(e));
      if (!Number.isInteger(e[1]) || e[1] < 1 || e[1] > b.length) problems.push("连接目标下标越界: " + JSON.stringify(e));
      if (!Number.isInteger(e[2]) || e[2] < 1 || e[2] > 17) problems.push("连接属性越界: " + JSON.stringify(e));
      if (e[3] == null || e[4] == null) problems.push("连接号为 nil: " + JSON.stringify(e));
    });
    if (bp.g != null) problems.push("不支持旧地形锚 g");
    if (bp.x != null) {
      if (!Array.isArray(bp.x)) problems.push("x 必须是数组");
      else bp.x.forEach(function (e) {
        if (!Array.isArray(e) || e.length < 12) { problems.push("外部支撑记录格式错误"); return; }
        if (!Number.isInteger(e[0]) || e[0] < 1 || e[0] > b.length) problems.push("外部支撑源下标越界");
        if (!Number.isInteger(e[1]) || e[1] < 1 || e[1] > t.length) problems.push("外部支撑目标类型下标越界");
        if (!Number.isInteger(e[2]) || e[2] < 1 || e[2] > 17) problems.push("外部支撑属性越界");
        for (var m = 5; m < 12; m++) if (typeof e[m] !== "number" || !isFinite(e[m])) { problems.push("外部支撑变换含非有限数"); break; }
      });
    }
    if (bp.w != null) {
      var w = bp.w;
      var wOk = Array.isArray(w) && (w.length === 7 || w.length === 10);
      if (!wOk) problems.push("w 变换格式错误");
      else for (var n = 0; n < w.length; n++) if (typeof w[n] !== "number" || !isFinite(w[n])) { problems.push("w 含非有限数"); break; }
    }

    // connectivity from the anchor through saved connections
    var reachable = problems.length ? 0 : (function () {
      var adj = {};
      for (var q = 1; q <= b.length; q++) adj[q] = [];
      edges.forEach(function (e) {
        if (adj[e[0]] && adj[e[1]]) { adj[e[0]].push(e[1]); adj[e[1]].push(e[0]); }
      });
      var start = bp.a, seen = {}, stack = [start];
      seen[start] = true;
      while (stack.length) {
        var cur = stack.pop();
        (adj[cur] || []).forEach(function (nb) { if (!seen[nb]) { seen[nb] = true; stack.push(nb); } });
      }
      return Object.keys(seen).length;
    })();
    if (!problems.length && reachable !== b.length) problems.push((b.length - reachable) + " 个构件从锚点不可达");

    // duplicate slot warning + material totals + layer stats
    var dupKeys = {};
    b.forEach(function (rec, index) {
      var key = rec[0] + "|" + Math.round(rec[1]) + "|" + Math.round(rec[2]) + "|" + Math.round(rec[3]);
      dupKeys[key] = (dupKeys[key] || 0) + 1;
    });
    var dups = Object.keys(dupKeys).filter(function (key) { return dupKeys[key] > 1; }).length;
    if (dups) warnings.push(dups + " 个重复槽位（同类型同一位置）");
    var materials = {};
    var counts = {};
    b.forEach(function (rec) {
      var id = t[rec[0] - 1];
      counts[id] = (counts[id] || 0) + 1;
      var m = MATERIALS[id] || {};
      Object.keys(m).forEach(function (item) { materials[item] = (materials[item] || 0) + m[item]; });
    });
    var info = summarize(bp);
    stats = {
      layers: b.length,
      totalHeight: info.totalHeight,
      edges: edges.length,
      types: t.length,
      reachable: reachable,
      pitch: info.pitch,
      counts: counts,
      materials: materials
    };
    return { ok: problems.length === 0, problems: problems, warnings: warnings, stats: stats };
  }

  // ---------------------------------------------------------------- PWB9D frame

  var CRC_TABLE = (function () {
    var table = new Uint32Array(256);
    for (var n = 0; n < 256; n++) {
      var c = n;
      for (var k = 0; k < 8; k++) c = (c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1);
      table[n] = c >>> 0;
    }
    return table;
  })();

  function crc32(bytes) {
    var crc = 0xFFFFFFFF;
    for (var i = 0; i < bytes.length; i++) crc = (crc >>> 8) ^ CRC_TABLE[(crc ^ bytes[i]) & 0xFF];
    return (crc ^ 0xFFFFFFFF) >>> 0;
  }

  function utf8Bytes(text) {
    if (typeof TextEncoder !== "undefined") return new TextEncoder().encode(text);
    var out = [], i, c;
    for (i = 0; i < text.length; i++) {
      c = text.charCodeAt(i);
      if (c < 0x80) out.push(c);
      else if (c < 0x800) out.push(0xC0 | (c >> 6), 0x80 | (c & 63));
      else if (c < 0xD800 || c >= 0xE000) out.push(0xE0 | (c >> 12), 0x80 | ((c >> 6) & 63), 0x80 | (c & 63));
      else { c = 0x10000 + ((c & 0x3FF) << 10) + (text.charCodeAt(++i) & 0x3FF); out.push(0xF0 | (c >> 18), 0x80 | ((c >> 12) & 63), 0x80 | ((c >> 6) & 63), 0x80 | (c & 63)); }
    }
    return new Uint8Array(out);
  }
  function utf8Text(bytes) {
    if (typeof TextDecoder !== "undefined") return new TextDecoder("utf-8", { fatal: true }).decode(bytes);
    var s = "";
    for (var i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]);
    return decodeURIComponent(escape(s));
  }

  function canonicalJson(value) {
    if (value === null) return "null";
    var t = typeof value;
    if (t === "number") {
      if (!isFinite(value)) throw new Error("JSON 不能包含非有限数");
      return JSON.stringify(value);
    }
    if (t === "string") return JSON.stringify(value);
    if (t === "boolean") return value ? "true" : "false";
    if (Array.isArray(value)) {
      return "[" + value.map(canonicalJson).join(",") + "]";
    }
    var keys = Object.keys(value).sort();
    return "{" + keys.map(function (key) { return JSON.stringify(key) + ":" + canonicalJson(value[key]); }).join(",") + "}";
  }

  function reverseBits(code, length) {
    var out = 0;
    for (var i = 0; i < length; i++) { out = (out << 1) | (code & 1); code >>>= 1; }
    return out >>> 0;
  }

  var LEN_BASE = [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258];
  var LEN_EXTRA = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];
  var DIST_BASE = [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577];
  var DIST_EXTRA = [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13];

  function fixedLitCode(sym) {
    if (sym <= 143) return [0x30 + sym, 8];
    if (sym <= 255) return [0x190 + (sym - 144), 9];
    if (sym <= 279) return [sym - 256, 7];
    return [0xC0 + (sym - 280), 8];
  }

  function BitWriter(capacity) {
    this.buf = new Uint8Array(Math.max(64, capacity || 64));
    this.len = 0; this.bitBuf = 0; this.bitCnt = 0;
  }
  BitWriter.prototype.push = function (byte) {
    if (this.len >= this.buf.length) {
      var next = new Uint8Array(this.buf.length * 2);
      next.set(this.buf);
      this.buf = next;
    }
    this.buf[this.len++] = byte;
  };
  BitWriter.prototype.writeBits = function (value, count) {
    for (var i = 0; i < count; i++) {
      this.bitBuf |= ((value >>> i) & 1) << this.bitCnt;
      if (++this.bitCnt === 8) { this.push(this.bitBuf); this.bitBuf = 0; this.bitCnt = 0; }
    }
  };
  BitWriter.prototype.writeBit = function (bit) { this.writeBits(bit, 1); };
  BitWriter.prototype.flush = function () {
    if (this.bitCnt > 0) { this.push(this.bitBuf); this.bitBuf = 0; this.bitCnt = 0; }
    return this.buf.slice(0, this.len);
  };

  function writeSymbol(writer, sym) {
    var c = fixedLitCode(sym);
    writer.writeBits(reverseBits(c[0], c[1]), c[1]);
  }
  function writeLength(writer, length) {
    for (var i = LEN_BASE.length - 1; i >= 0; i--) {
      if (length >= LEN_BASE[i]) {
        writeSymbol(writer, 257 + i);
        if (LEN_EXTRA[i]) writer.writeBits(length - LEN_BASE[i], LEN_EXTRA[i]);
        return;
      }
    }
    throw new Error("长度编码失败: " + length);
  }
  function writeDistance(writer, distance) {
    for (var i = DIST_BASE.length - 1; i >= 0; i--) {
      if (distance >= DIST_BASE[i]) {
        writer.writeBits(reverseBits(i, 5), 5);
        if (DIST_EXTRA[i]) writer.writeBits(distance - DIST_BASE[i], DIST_EXTRA[i]);
        return;
      }
    }
    throw new Error("距离编码失败: " + distance);
  }

  // Fixed-Huffman DEFLATE (the only block type the mod's PWB9D decoder accepts,
  // besides stored blocks). LZ77 with a 32K window and hash chains.
  function deflateFixed(bytes) {
    var writer = new BitWriter(bytes.length >> 1);
    writer.writeBit(1);                 // BFINAL
    writer.writeBits(1, 2);             // BTYPE = 01 (fixed Huffman)
    var n = bytes.length;
    if (n === 0) { writeSymbol(writer, 256); return writer.flush(); }
    var head = new Int32Array(1 << 16);
    head.fill(-1);
    var prev = new Int32Array(n);
    prev.fill(-1);
    var hashAt = function (i) { return ((bytes[i] << 10) ^ (bytes[i + 1] << 5) ^ bytes[i + 2]) & 0xFFFF; };
    var insert = function (i) {
      if (i + 2 >= n) return;
      var h = hashAt(i);
      prev[i] = head[h];
      head[h] = i;
    };
    var pos = 0, MAX_MATCH = 258, MAX_CHAIN = 64;
    while (pos < n) {
      var bestLen = 0, bestDist = 0;
      if (pos + 2 < n) {
        var limit = Math.min(n - pos, MAX_MATCH);
        var cand = head[hashAt(pos)];
        var chain = MAX_CHAIN;
        while (cand >= 0 && chain-- > 0) {
          var dist = pos - cand;
          if (dist > 32768) break;
          if (bytes[cand + bestLen] === bytes[pos + bestLen]) {
            var len = 0;
            while (len < limit && bytes[cand + len] === bytes[pos + len]) len++;
            if (len > bestLen) {
              bestLen = len; bestDist = dist;
              if (len >= limit) break;
            }
          }
          cand = prev[cand];
        }
      }
      if (bestLen >= 3) {
        writeLength(writer, bestLen);
        writeDistance(writer, bestDist);
        for (var k = 0; k < bestLen; k++) insert(pos + k);
        pos += bestLen;
      } else {
        writeSymbol(writer, bytes[pos]);
        insert(pos);
        pos++;
      }
    }
    writeSymbol(writer, 256);           // end of block
    return writer.flush();
  }

  // DEFLATE reader for stored + fixed-Huffman blocks (enough for every legacy
  // PWB9D code; dynamic-Huffman frames are rejected with a clear message).
  function inflateRaw(bytes) {
    var reader = { pos: 0, bitBuf: 0, bitCnt: 0 };
    var out = [];
    function readBit() {
      if (reader.bitCnt === 0) {
        if (reader.pos >= bytes.length) throw new Error("DEFLATE 数据提前结束");
        reader.bitBuf = bytes[reader.pos++];
        reader.bitCnt = 8;
      }
      var bit = reader.bitBuf & 1;
      reader.bitBuf >>>= 1;
      reader.bitCnt--;
      return bit;
    }
    function alignByte() { reader.bitBuf = 0; reader.bitCnt = 0; }
    function readBits(count) {
      var value = 0;
      for (var i = 0; i < count; i++) value |= readBit() << i;
      return value >>> 0;
    }
    var litTable = [], distTable = [];
    (function () {
      for (var sym = 0; sym <= 287; sym++) {
        var c = fixedLitCode(sym);
        if (!litTable[c[1]]) litTable[c[1]] = {};
        litTable[c[1]][c[0]] = sym;
      }
      for (var d = 0; d < 30; d++) {
        if (!distTable[5]) distTable[5] = {};
        distTable[5][d] = d;
      }
    })();
    function readHuff(table, maxLen) {
      var code = 0;
      for (var len = 1; len <= maxLen; len++) {
        code = (code << 1) | readBit();
        var bucket = table[len];
        if (bucket && bucket[code] !== undefined) return bucket[code];
      }
      throw new Error("DEFLATE Huffman 码无效");
    }
    var finished = false;
    while (!finished) {
      finished = readBit() === 1;
      var type = readBits(2);
      if (type === 0) {
        alignByte();
        if (reader.pos + 4 > bytes.length) throw new Error("DEFLATE stored 块头缺失");
        var len = bytes[reader.pos] | (bytes[reader.pos + 1] << 8);
        var nlen = bytes[reader.pos + 2] | (bytes[reader.pos + 3] << 8);
        reader.pos += 4;
        if ((len ^ 0xFFFF) !== nlen) throw new Error("DEFLATE stored 块长度校验失败");
        if (reader.pos + len > bytes.length) throw new Error("DEFLATE stored 块数据缺失");
        for (var i = 0; i < len; i++) out.push(bytes[reader.pos + i]);
        reader.pos += len;
      } else if (type === 1) {
        while (true) {
          var sym = readHuff(litTable, 9);
          if (sym < 256) { out.push(sym); continue; }
          if (sym === 256) break;
          var lenIndex = sym - 257;
          if (lenIndex < 0 || lenIndex >= LEN_BASE.length) throw new Error("DEFLATE 长度码无效");
          var length = LEN_BASE[lenIndex] + readBits(LEN_EXTRA[lenIndex]);
          var distSym = readHuff(distTable, 5);
          var distance = DIST_BASE[distSym] + readBits(DIST_EXTRA[distSym]);
          if (distance > out.length) throw new Error("DEFLATE 距离越界");
          for (var j = 0; j < length; j++) out.push(out[out.length - distance]);
        }
      } else if (type === 2) {
        throw new Error("这段码用了动态 Huffman 压缩块，本页只解固定 Huffman 的 PWB9D；请用 Python 生成器导入模板");
      } else {
        throw new Error("DEFLATE 块类型无效");
      }
    }
    return new Uint8Array(out);
  }

  var B64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
  function base64UrlEncode(bytes) {
    var out = [];
    for (var i = 0; i < bytes.length; i += 3) {
      var a = bytes[i], b = bytes[i + 1], c = bytes[i + 2];
      var triple = (a << 16) | ((b == null ? 0 : b) << 8) | (c == null ? 0 : c);
      out.push(B64[(triple >> 18) & 63], B64[(triple >> 12) & 63]);
      if (b != null) out.push(B64[(triple >> 6) & 63]);
      if (c != null) out.push(B64[triple & 63]);
    }
    return out.join("");
  }
  function base64UrlDecode(text) {
    if (text.length % 4 === 1) throw new Error("Base64URL 长度无效");
    var decode = {};
    for (var i = 0; i < B64.length; i++) decode[B64[i]] = i;
    var out = [];
    for (var p = 0; p < text.length; p += 4) {
      var chunk = text.slice(p, p + 4);
      var values = [];
      for (var q = 0; q < chunk.length; q++) {
        var v = decode[chunk[q]];
        if (v === undefined) throw new Error("Base64URL 含非法字符");
        values.push(v);
      }
      var triple = (values[0] << 18) | (values[1] << 12) | ((values[2] || 0) << 6) | (values[3] || 0);
      out.push((triple >> 16) & 0xFF);
      if (values.length >= 3) out.push((triple >> 8) & 0xFF);
      if (values.length >= 4) out.push(triple & 0xFF);
    }
    return new Uint8Array(out);
  }
  function packVarUInt(value) {
    var out = [];
    do {
      var byte = value & 0x7F;
      value = Math.floor(value / 128);
      if (value) byte |= 0x80;
      out.push(byte);
    } while (value);
    return out;
  }
  function unpackVarUInt(bytes, offset) {
    var value = 0, shift = 0, pos = offset;
    for (var i = 0; i < 5; i++) {
      if (pos >= bytes.length) throw new Error("PWB9D 声明长度截断");
      var byte = bytes[pos++];
      value += (byte & 0x7F) * Math.pow(2, shift);
      if (!(byte & 0x80)) return { value: value, offset: pos };
      shift += 7;
    }
    throw new Error("PWB9D VarUInt 过长");
  }

  function encodePWB9D(bpOrText) {
    var text = typeof bpOrText === "string" ? bpOrText : canonicalJson(bpOrText);
    var json = utf8Bytes(text);
    var compressed = deflateFixed(json);
    var crc = crc32(json);
    var frame = packVarUInt(json.length).concat([
      crc & 0xFF, (crc >>> 8) & 0xFF, (crc >>> 16) & 0xFF, (crc >>> 24) & 0xFF
    ]).concat(Array.prototype.slice.call(compressed));
    return "PWB9D." + base64UrlEncode(new Uint8Array(frame));
  }

  function decodePWB9D(code) {
    if (typeof code !== "string") throw new Error("蓝图码必须是字符串");
    var text = code.trim();
    if (!text) throw new Error("蓝图码为空");
    if (text.slice(0, 6) !== "PWB9D.") {
      if (text.slice(0, 6) === "PWB1:" || text.slice(0, 5) === "PWB1:") {
        throw new Error("这是新版 PWB1 码，本页只处理 PWB9D（旧版剪贴板格式）");
      }
      throw new Error("不是 PWB9D. 开头的蓝图码");
    }
    var frame = base64UrlDecode(text.slice(6));
    var declared = unpackVarUInt(frame, 0);
    var offset = declared.offset;
    if (offset + 4 > frame.length) throw new Error("PWB9D CRC 截断");
    var expected = (frame[offset] | (frame[offset + 1] << 8) | (frame[offset + 2] << 16) | (frame[offset + 3] << 24)) >>> 0;
    offset += 4;
    var json = inflateRaw(frame.slice(offset));
    if (json.length !== declared.value) throw new Error("PWB9D 声明长度不符: " + json.length + " != " + declared.value);
    if (crc32(json) !== expected) throw new Error("PWB9D CRC32 校验失败");
    var bp = JSON.parse(utf8Text(json));
    if (!bp || typeof bp !== "object" || !bp.b) throw new Error("解码结果不是 schema-9 蓝图");
    return bp;
  }

  return {
    VERSION: "1.0.0",
    PITCH: PITCH,
    FARM_HEIGHT: FARM_HEIGHT,
    PROP_ANY: PROP_ANY,
    CONN_IDX: CONN_IDX,
    CROPS: CROPS,
    ALIASES: ALIASES,
    MATERIALS: MATERIALS,
    normalizeCrop: normalizeCrop,
    cropMeta: cropMeta,
    defaultCrops: defaultCrops,
    build: build,
    buildFromTemplate: buildFromTemplate,
    summarize: summarize,
    validate: validate,
    canonicalJson: canonicalJson,
    encodePWB9D: encodePWB9D,
    decodePWB9D: decodePWB9D,
    crc32: crc32,
    deflateFixed: deflateFixed,
    inflateRaw: inflateRaw
  };
});
