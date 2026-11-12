#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Generate a vertical farm-stack blueprint code (schema-9 JSON + PWB9D).

The pattern comes from the in-game plantation stacking trick: every farm block
sits one "scaffold height" above the previous one.  That height is the Wooden
Bench seat height measured from the exported game meshes (0.4877736 m =
48.777 cm), so each layer clears the previous farm block (mesh height
0.4167 m) by ~7.1 cm.  Layers are chained with saved ConnectInfoAnyPlace
connections (property 1, connect index "254"), the same shape a real
mod-saved blueprint uses.

Two modes
  template  --from <pwb9d|schema-9 json>   reuse crop cycle / yaw / pitch of an
            existing stack, then re-emit it with --layers (taller or shorter).
  direct    --layers N --crops a,b,c ...   build a new stack from scratch.

Usage
  python tooling/farm_stack.py --decode out/farm_stack_22.pwb9d.txt
  python tooling/farm_stack.py --from out/source.pwb9d.txt --layers 22 -o out --name farm_22
  python tooling/farm_stack.py --from out/source.pwb9d.txt --layers 11 --keep-world --keep-support -o out --name repro
  python tooling/farm_stack.py --layers 22 --crops tomato -o out --name tomato_22

Output: <out>/<name>.json (schema 9) + <out>/<name>.pwb9d.txt (clipboard code).
Exit: 0 = written and offline checks pass, 1 = problems, 2 = usage/input error.
"""
import argparse
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import validate as VAL            # noqa: E402
import export as EX               # noqa: E402

PITCH = 48.777                    # Wooden Bench height in cm (measured mesh bbox)
PROP_ANY = 1                      # ConnectInfoAnyPlace
CONN_IDX = "254"
DEFAULT_CROPS = ["FarmBlockV2_wheet", "FarmBlockV2_Potato",
                 "FarmBlockV2_Onion", "FarmBlockV2_Carrot",
                 "FarmBlockV2_Lettuce", "FarmBlockV2_tomato"]
CROP_ALIASES = {
    "wheet": "FarmBlockV2_wheet", "wheat": "FarmBlockV2_wheet",
    "potato": "FarmBlockV2_Potato", "onion": "FarmBlockV2_Onion",
    "carrot": "FarmBlockV2_Carrot", "lettuce": "FarmBlockV2_Lettuce",
    "tomato": "FarmBlockV2_tomato", "berries": "FarmBlockV2_Berries",
    "berry": "FarmBlockV2_Berries",
}


def tidy(v, places=3):
    r = round(float(v), places)
    return int(r) if abs(r - round(r)) < 1e-9 else r


def q_yaw(deg):
    h = math.radians(deg) / 2.0
    return (0.0, 0.0, round(math.sin(h), 7), round(math.cos(h), 7))


def yaw_deg(q):
    x, y, z, w = q
    return math.degrees(math.atan2(2.0 * (w * z + x * y), 1.0 - 2.0 * (y * y + z * z)))


def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return (aw*bx + ax*bw + ay*bz - az*by,
            aw*by - ax*bz + ay*bw + az*bx,
            aw*bz + ax*by - ay*bx + az*bw,
            aw*bw - ax*bx - ay*by - az*bz)


def qconj(q):
    return (-q[0], -q[1], -q[2], q[3])


def qrot(q, v):
    p = qmul(qmul(q, (v[0], v[1], v[2], 0.0)), qconj(q))
    return (p[0], p[1], p[2])


def normalize_crop(text):
    key = text.strip()
    low = key.lower()
    if low in CROP_ALIASES:
        return CROP_ALIASES[low]
    for name in DEFAULT_CROPS + ["FarmBlockV2_Berries"]:
        if name.lower() == low:
            return name
    raise SystemExit("unknown crop %r; use one of: %s"
                     % (text, ", ".join(sorted(CROP_ALIASES))))


def local_frame(bp, rec):
    """Return (pos, quat) of a building record in the anchor's local frame."""
    recs = bp["b"]
    anchor = recs[bp["a"] - 1]
    qa = anchor[4:8]
    mag = math.sqrt(sum(v * v for v in qa)) or 1.0
    qa = tuple(v / mag for v in qa)
    qinv = qconj(qa)
    pos = qrot(qinv, (rec[1] - anchor[1], rec[2] - anchor[2], rec[3] - anchor[3]))
    quat = qmul(qinv, tuple(rec[4:8]))
    return pos, quat


def describe(bp):
    """Summarize a farm-stack blueprint: layers, crops, pitch, yaw, extras."""
    recs = bp.get("b") or []
    types = bp.get("t") or []
    rows = []
    for i, rec in enumerate(recs, 1):
        pos, quat = local_frame(bp, rec)
        rows.append({"index": i, "type": types[rec[0] - 1],
                     "x": pos[0], "y": pos[1], "z": pos[2],
                     "yaw": tidy(yaw_deg(quat), 2)})
    rows.sort(key=lambda r: (r["z"], r["index"]))
    zs = [r["z"] for r in rows]
    diffs = [tidy(zs[i + 1] - zs[i]) for i in range(len(zs) - 1)]
    nonzero = [d for d in diffs if d > 0.5]
    return {
        "rows": rows,
        "layers": len(rows),
        "pitch": sorted(nonzero)[len(nonzero) // 2] if nonzero else None,
        "deltas": diffs,
        "crops": [r["type"] for r in rows],
        "yaws": [r["yaw"] for r in rows],
        "anchor_index": bp.get("a"),
        "has_world": bp.get("w") is not None,
        "has_external_support": bool(bp.get("x")),
        "connections": len(bp.get("c") or []),
    }


def summarize_text(summary):
    lines = ["layers: %d  pitch: %s cm  anchor: %s  edges: %d  world(w): %s  external(x): %s"
             % (summary["layers"], summary["pitch"], summary["anchor_index"],
                summary["connections"], summary["has_world"],
                summary["has_external_support"])]
    for row in summary["rows"]:
        lines.append("  %2d  %-24s z=%-10s yaw=%s"
                     % (row["index"], row["type"], row["z"], row["yaw"]))
    return "\n".join(lines)


def chain_edges(n):
    """Saved-connection edges in the same order the real mod serializer emits."""
    edges = []
    if n >= 2:
        edges.append([1, 2, PROP_ANY, CONN_IDX, CONN_IDX])
    for i in range(2, n):
        edges.append([i, i + 1, PROP_ANY, CONN_IDX, CONN_IDX])
        edges.append([i, i - 1, PROP_ANY, CONN_IDX, CONN_IDX])
    if n >= 2:
        edges.append([n, n - 1, PROP_ANY, CONN_IDX, CONN_IDX])
    return edges


def build_from_template(bp, layers, crops=None, pitch=None, yaws=None,
                        keep_world=False, keep_support=False):
    summary = describe(bp)
    rows = summary["rows"]
    base_layers = rows if layers >= summary["layers"] else rows[:layers]
    step = pitch if pitch is not None else summary["pitch"]
    if step is None or step <= 0:
        raise SystemExit("cannot infer pitch from template; pass --pitch")
    crop_seq = [r["type"] for r in base_layers]
    if crops:
        crop_seq = crops
    yaw_seq = [r["yaw"] for r in base_layers]
    if yaws:
        yaw_seq = yaws
    z_last = rows[-1]["z"] if rows else 0.0
    out_types, edges = [], []
    b = []
    for i in range(layers):
        if i < len(base_layers):
            z = base_layers[i]["z"]
            yaw = yaw_seq[i % len(yaw_seq)]
        else:
            z = tidy(z_last + step * (i - len(base_layers) + 1))
            if yaws:
                yaw = yaw_seq[i % len(yaw_seq)]
            else:
                yaw = yaw_seq[i % len(yaw_seq)]
        crop = crop_seq[i % len(crop_seq)]
        if crop not in out_types:
            out_types.append(crop)
        q = q_yaw(yaw)
        b.append([out_types.index(crop) + 1, 0, 0, tidy(z),
                  tidy(q[0], 7), tidy(q[1], 7), tidy(q[2], 7), tidy(q[3], 7)])
    x = None
    if keep_support and bp.get("x"):
        x = json.loads(json.dumps(bp["x"]))
        for edge in x:                       # [source 1-based, target type 1-based, ...]
            if edge[0] > layers:
                edge[0] = 1
            target_type = bp["t"][edge[1] - 1]
            if target_type not in out_types:
                out_types.append(target_type)
            edge[1] = out_types.index(target_type) + 1
    w = json.loads(json.dumps(bp["w"])) if keep_world and bp.get("w") else None
    out = {"v": 9, "t": out_types, "b": b, "a": 1}
    out["c"] = chain_edges(layers)
    if x:
        out["x"] = x
    if w:
        out["w"] = w
    return out


def build_direct(layers, crops, pitch):
    out_types, b = [], []
    for i in range(layers):
        crop = crops[i % len(crops)]
        if crop not in out_types:
            out_types.append(crop)
        q = (0.0, 0.0, 0.0, 1.0) if i % 2 == 0 else (0.0, 0.0, 1.0, 0.0)
        b.append([out_types.index(crop) + 1, 0, 0, tidy(i * pitch),
                  q[0], q[1], q[2], q[3]])
    return {"v": 9, "t": out_types, "b": b, "a": 1, "c": chain_edges(layers)}


def check(bp):
    problems = VAL.check_schema(bp)
    geo, stats, warnings = VAL.check_geometry(bp)
    cat, cat_warnings, cat_stats = VAL.check_catalog(bp)
    problems += geo + cat
    warnings += cat_warnings
    stats.update(cat_stats)
    return problems, warnings, stats


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--from", dest="source", help="template PWB9D code or schema-9 JSON file")
    ap.add_argument("--layers", type=int, help="number of farm layers to emit")
    ap.add_argument("--crops", help="comma-separated crop ids or aliases (cycled)")
    ap.add_argument("--pitch", type=float, default=None, help="layer pitch in cm (default 48.777 or template)")
    ap.add_argument("--yaws", help="comma-separated yaw degrees per layer (cycled)")
    ap.add_argument("--keep-world", action="store_true", help="copy the template anchor world transform (w)")
    ap.add_argument("--keep-support", action="store_true", help="copy the template external support (x)")
    ap.add_argument("--decode", help="only print a summary of an existing code/json")
    ap.add_argument("-o", "--outdir", default=None)
    ap.add_argument("--name")
    args = ap.parse_args()

    if args.decode:
        bp = VAL.load(args.decode)
        print(summarize_text(describe(bp)))
        return 0

    if args.layers is None or args.layers < 1:
        raise SystemExit("--layers must be >= 1")
    crops = [normalize_crop(x) for x in args.crops.split(",")] if args.crops else None
    yaws = [float(x) for x in args.yaws.split(",")] if args.yaws else None

    if args.source:
        bp = VAL.load(args.source)
        if "t" not in bp:
            raise SystemExit("template is not a schema-9 blueprint")
        out = build_from_template(
            bp, args.layers, crops=crops,
            pitch=args.pitch if args.pitch is not None else None,
            yaws=yaws, keep_world=args.keep_world,
            keep_support=args.keep_support)
        default_name = "farm_stack_%d" % args.layers
    else:
        out = build_direct(args.layers, crops or DEFAULT_CROPS,
                           args.pitch if args.pitch is not None else PITCH)
        default_name = "farm_stack_%d" % args.layers

    name = args.name or default_name
    problems, warnings, stats = check(out)
    report = {"name": name, "layers": len(out["b"]), "types": len(out["t"]),
              "edges": len(out.get("c") or []),
              "pitch": args.pitch if args.pitch is not None else PITCH,
              "crops": [out["t"][r[0] - 1] for r in out["b"]],
              "has_world": "w" in out, "has_external_support": "x" in out,
              "ok": not problems, "problems": problems, "warnings": warnings,
              "stats": stats}
    if problems:
        print(json.dumps(report, ensure_ascii=False, indent=1))
        return 1

    outdir = args.outdir or os.path.join(os.path.dirname(HERE), "out")
    os.makedirs(outdir, exist_ok=True)
    text = json.dumps(out, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    json_path = os.path.join(outdir, name + ".json")
    code_path = os.path.join(outdir, name + ".pwb9d.txt")
    open(json_path, "w", encoding="utf-8", newline="\n").write(text)
    code = EX.encode_pwb9d(text)
    open(code_path, "w", encoding="utf-8", newline="\n").write(code + "\n")
    report["json"] = json_path
    report["code"] = code_path
    report["code_chars"] = len(code)
    report["json_bytes"] = len(text.encode("utf-8"))
    print(json.dumps(report, ensure_ascii=False, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
