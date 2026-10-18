#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Export an authoring file to an import-ready blueprint for Simple Building Blueprints.

This tool is deliberately objective: it does not design anything. The model writes
pieces with explicit positions/rotations; the exporter handles the mechanical parts
(anchor-relative transforms, type dictionary, saved-connection graph, schema checks,
PWB9D clipboard code).

Authoring format (JSON):
{
  "name": "my_hut",
  "anchor": 0,                     // index into pieces (optional, default 0)
  "pieces": [
    {"id": "Wooden_foundation", "x": 0,    "y": 0,   "z": 0, "yaw": 0},
    {"id": "Wooden_wall",       "x": 0,    "y": -200,"z": 0, "yaw": 90}
  ]
}
  x/y/z : centimetres in one common frame; any origin, any rotation (anchor frame is
          removed automatically).
  yaw   : degrees, UE yaw (0 = +X, 90 = +Y).

Usage:
  python blueprints/tooling/export.py authoring.json [-o outdir] [--name NAME]
  python blueprints/tooling/export.py authoring.json --check-only

Output: <name>.json (schema 9) + <name>.pwb9d.txt (clipboard code).
"""
import argparse
import base64
import json
import math
import os
import sys
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import validate as VAL            # noqa: E402

SCHEMA = 9
CELL = 400.0
WALL_H = 325.0
P_FRONT, P_BACK, P_LEFT, P_RIGHT, P_UP, P_DOWN = 2, 3, 4, 5, 6, 7


def q_from_yaw(deg):
    h = math.radians(deg) / 2.0
    return (0.0, 0.0, round(math.sin(h), 7), round(math.cos(h), 7))


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


def tidy(v, places=3):
    r = round(float(v), places)
    return int(r) if abs(r - round(r)) < 1e-9 else r


def family(name):
    return VAL.family(name)


def build(authoring):
    pieces = authoring.get("pieces") or []
    if not pieces:
        raise SystemExit("authoring file has no pieces")
    anchor_index = int(authoring.get("anchor", 0))
    if not (0 <= anchor_index < len(pieces)):
        raise SystemExit("anchor index out of range")
    anchor = pieces[anchor_index]
    q_anchor = q_from_yaw(float(anchor.get("yaw", 0.0)))
    q_inv = qconj(q_anchor)
    a_pos = (float(anchor.get("x", 0)), float(anchor.get("y", 0)), float(anchor.get("z", 0)))
    pieces = [pieces[anchor_index]] + [p for i, p in enumerate(pieces) if i != anchor_index]

    types, type_index, buildings = [], {}, []
    for p in pieces:
        bid = p["id"]
        if bid not in type_index:
            types.append(bid)
            type_index[bid] = len(types)
        pos = (float(p.get("x", 0)), float(p.get("y", 0)), float(p.get("z", 0)))
        rel = qrot(q_inv, (pos[0] - a_pos[0], pos[1] - a_pos[1], pos[2] - a_pos[2]))
        q_rel = qmul(q_inv, q_from_yaw(float(p.get("yaw", 0.0))))
        buildings.append([type_index[bid], tidy(rel[0]), tidy(rel[1]), tidy(rel[2]),
                          tidy(q_rel[0], 7), tidy(q_rel[1], 7), tidy(q_rel[2], 7), tidy(q_rel[3], 7)])
    bp = {"v": SCHEMA, "t": types, "b": buildings, "a": 1}

    # ---- objective connection graph (what the mod planner needs) -----------
    fams = [family(t) for t in types]
    bfam = [fams[b[0] - 1] for b in buildings]
    edges, seen = [], set()

    def link(a, b, prop):
        key = (a, b, prop)
        if a == b or key in seen:
            return
        seen.add(key)
        edges.append([a, b, prop, "<nil>", "<nil>"])

    pos2 = [(b[1], b[2], b[3]) for b in buildings]
    for i, f in enumerate(bfam):
        if f != "foundation":
            continue
        for j, g in enumerate(bfam):
            if g != "foundation" or i == j:
                continue
            dx, dy, dz = pos2[j][0] - pos2[i][0], pos2[j][1] - pos2[i][1], pos2[j][2] - pos2[i][2]
            if abs(dz) > 1:
                continue
            if abs(abs(dx) - CELL) <= 6 and abs(dy) <= 6:
                link(i + 1, j + 1, P_FRONT if dx > 0 else P_BACK)
            elif abs(abs(dy) - CELL) <= 6 and abs(dx) <= 6:
                link(i + 1, j + 1, P_RIGHT if dy > 0 else P_LEFT)

    def nearest(i, fams_ok, dz=0.0, max_h=320.0):
        best = None
        for j, g in enumerate(bfam):
            if j == i or g not in fams_ok:
                continue
            dx, dy = pos2[j][0] - pos2[i][0], pos2[j][1] - pos2[i][1]
            d = math.hypot(dx, dy)
            if d > max_h or abs(pos2[j][2] - pos2[i][2] - dz) > 30:
                continue
            if best is None or d < best[0]:
                best = (d, j)
        return best[1] if best else None

    for i, f in enumerate(bfam):
        if f in ("wall", "door", "window", "diagonal"):
            j = nearest(i, ("foundation",))
            if j is not None:
                link(i + 1, j + 1, P_DOWN)
        elif f in ("roof", "stairs"):
            j = nearest(i, ("wall", "door", "window", "diagonal"), dz=-WALL_H)
            if j is not None:
                link(i + 1, j + 1, P_UP)
            else:
                j = nearest(i, ("foundation",), dz=-WALL_H)
                if j is not None:
                    link(i + 1, j + 1, P_DOWN)
        elif f in ("pillar", "fence", "prop"):
            j = nearest(i, ("foundation",))
            if j is not None:
                link(i + 1, j + 1, P_DOWN)

    if edges:
        bp["c"] = edges
    return bp


def encode_pwb9d(text):
    js = text.encode("utf-8")
    co = zlib.compressobj(9, zlib.DEFLATED, -15, 9, zlib.Z_FIXED)
    body = co.compress(js) + co.flush()
    out = bytearray()
    n = len(js)
    while True:
        byte = n & 0x7F
        n >>= 7
        if n:
            byte |= 0x80
        out.append(byte)
        if not n:
            break
    out += (zlib.crc32(js) & 0xFFFFFFFF).to_bytes(4, "little")
    out += body
    return "PWB9D." + base64.urlsafe_b64encode(bytes(out)).decode("ascii").rstrip("=")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("authoring")
    ap.add_argument("-o", "--outdir", default=os.path.dirname(HERE))
    ap.add_argument("--name")
    ap.add_argument("--check-only", action="store_true")
    ap.add_argument("--allow-problems", action="store_true")
    args = ap.parse_args()

    data = json.load(open(args.authoring, encoding="utf-8"))
    name = args.name or data.get("name") or os.path.splitext(os.path.basename(args.authoring))[0]
    bp = build(data)

    problems = VAL.check_schema(bp)
    geo_problems, stats, warnings = VAL.check_geometry(bp)
    cat_problems, cat_warnings, cat_stats = VAL.check_catalog(bp)
    problems += geo_problems + cat_problems
    warnings += cat_warnings
    stats.update(cat_stats)
    report = {"name": name, "pieces": len(bp["b"]), "types": len(bp["t"]),
              "edges": len(bp.get("c") or []), "ok": not problems,
              "problems": problems, "warnings": warnings, "stats": stats}
    if args.check_only:
        print(json.dumps(report, ensure_ascii=False, indent=1))
        return 0 if report["ok"] else 1
    if problems and not args.allow_problems:
        print(json.dumps(report, ensure_ascii=False, indent=1))
        return 1

    os.makedirs(args.outdir, exist_ok=True)
    text = json.dumps(bp, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    json_path = os.path.join(args.outdir, name + ".json")
    code_path = os.path.join(args.outdir, name + ".pwb9d.txt")
    open(json_path, "w", encoding="utf-8", newline="\n").write(text)
    open(code_path, "w", encoding="utf-8", newline="\n").write(encode_pwb9d(text) + "\n")
    report["json"] = json_path
    report["code"] = code_path
    print(json.dumps(report, ensure_ascii=False, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
