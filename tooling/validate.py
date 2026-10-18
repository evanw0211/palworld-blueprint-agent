#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Verify a Simple Building Blueprints schema-9 blueprint without touching the game.

Checks
  1. schema (v9, type/building arrays, anchor, closed set of keys) - mirrors
     BlueprintJsonSchema.validate()
  2. connectivity of the saved-connection graph (the build planner needs every
     piece reachable from the anchor)
  3. grid / height alignment in the anchor frame (400 pitch, 200 wall edges,
     foundation plane, wall plane, roof = wall plane + 325)
  4. duplicate pieces, and structural links (wall->foundation, roof->wall)

Usage:
  python blueprints/tooling/verify.py <bp.json|bp.pwb9d.txt> [--json] [--quiet]
exit code: 0 = ok, 1 = problems found, 2 = unreadable input
"""
import base64
import json
import math
import os
import sys
import zlib

SCHEMA = 9
CATALOG_PATH = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "data", "catalog.json")
CATALOG_DATA = {}
try:
    CATALOG_DATA = json.load(open(CATALOG_PATH, encoding="utf-8"))
except Exception:                               # noqa: BLE001
    CATALOG_DATA = {}
CATALOG = CATALOG_DATA.get("entries", CATALOG_DATA) if isinstance(CATALOG_DATA, dict) else {}
CATALOG_VERSION = CATALOG_DATA.get("version", {}) if isinstance(CATALOG_DATA, dict) else {}
CELL, EDGE, WALL_H = 400.0, 200.0, 325.0


def load(path):
    if path.lower().endswith(".txt"):
        code = open(path, encoding="utf-8").read().strip()
        if not code.startswith("PWB9D."):
            raise SystemExit("not a PWB9D code: %s" % path)
        b = code[6:]
        raw = base64.urlsafe_b64decode(b + "=" * ((4 - len(b) % 4) % 4))
        i = shift = size = 0
        while True:
            v = raw[i]; i += 1; size |= (v & 0x7F) << shift; shift += 7
            if not v & 0x80:
                break
        crc = int.from_bytes(raw[i:i + 4], "little")
        js = zlib.decompress(raw[i + 4:], -15)
        if len(js) != size or (zlib.crc32(js) & 0xFFFFFFFF) != crc:
            raise SystemExit("PWB9D frame mismatch")
        return json.loads(js.decode("utf-8"))
    return json.load(open(path, encoding="utf-8"))


DECOR = ("curtain", "light", "mirror", "clock", "sign", "shelf", "torch",
         "rug", "plant", "lamp", "painting", "fan", "rack", "candle", "picture")


def family(name):
    low = name.lower()
    if any(k in low for k in DECOR):
        return "prop"
    if "foundation" in low: return "foundation"
    if "door" in low: return "door"
    if "window" in low: return "window"
    if "roof" in low: return "roof"
    if "stair" in low: return "stairs"
    if "pillar" in low: return "pillar"
    if "fence" in low or "gate" in low: return "fence"
    if "diagonal" in low or "triangle" in low: return "diagonal"
    if "wall" in low: return "wall"
    return "prop"


def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return (aw*bx + ax*bw + ay*bz - az*by,
            aw*by - ax*bz + ay*bw + az*bx,
            aw*bz + ax*by - ay*bx + az*bw,
            aw*bw - ax*bx - ay*by - az*bz)


def qconj(q): return (-q[0], -q[1], -q[2], q[3])


def qrot(q, v):
    p = qmul(qmul(q, (v[0], v[1], v[2], 0.0)), qconj(q))
    return (p[0], p[1], p[2])


def quat(rec): return (rec[4], rec[5], rec[6], rec[7])


def yaw(q):
    x, y, z, w = q
    return math.degrees(math.atan2(2.0 * (w * z + x * y), 1.0 - 2.0 * (y * y + z * z)))


def check_schema(bp):
    problems = []
    if not isinstance(bp, dict):
        return ["root is not an object"]
    if bp.get("v") != SCHEMA:
        problems.append("schema version %r != %d" % (bp.get("v"), SCHEMA))
    t = bp.get("t")
    if not isinstance(t, list) or not t:
        problems.append("type dictionary missing")
        t = []
    for i, name in enumerate(t, 1):
        if not isinstance(name, str) or not name:
            problems.append("t[%d] is not a BuildObjectId string" % i)
    b = bp.get("b")
    if not isinstance(b, list) or not b:
        problems.append("building array missing")
        b = []
    a = bp.get("a")
    if not isinstance(a, int) or not (1 <= a <= len(b) or not b):
        problems.append("anchor index invalid: %r" % (a,))
    for i, rec in enumerate(b, 1):
        if not isinstance(rec, list) or len(rec) not in (8, 11):
            problems.append("b[%d] must have 8 or 11 numbers" % i); continue
        if not isinstance(rec[0], int) or not (1 <= rec[0] <= len(t) or not t):
            problems.append("b[%d] type index out of range" % i)
        for v in rec[1:]:
            if not isinstance(v, (int, float)) or not math.isfinite(v):
                problems.append("b[%d] has a non-finite number" % i); break
    for key in ("c", "x", "p"):
        if key in bp and not isinstance(bp[key], list):
            problems.append("%s must be an array" % key)
    if bp.get("g") is not None:
        problems.append("legacy terrain-anchor field g is unsupported by the mod")
    for edge in bp.get("c") or []:
        if not isinstance(edge, list) or len(edge) != 5:
            problems.append("connection record malformed: %r" % (edge,)); continue
        for idx in (edge[0], edge[1]):
            if not isinstance(idx, int) or not (1 <= idx <= len(b)):
                problems.append("connection building index out of range: %r" % (edge,))
        if not isinstance(edge[2], int) or not (1 <= edge[2] <= 17):
            problems.append("connection property index out of range: %r" % (edge,))
        if edge[3] is None or edge[4] is None:
            problems.append("connection index is nil: %r" % (edge,))
    return problems


def check_geometry(bp):
    types, recs = bp.get("t") or [], bp.get("b") or []
    if not recs:
        return [], {}, []
    a = bp.get("a", 1)
    anchor = recs[a - 1]
    q_anchor = anchor[4:8]
    mag = math.sqrt(sum(v * v for v in q_anchor)) or 1.0
    q_a = tuple(v / mag for v in q_anchor)
    q_inv = qconj(q_a)
    fams = [family(types[r[0] - 1]) for r in recs]

    def local(rec):
        off = qrot(q_inv, (rec[1], rec[2], rec[3]))
        return (off[0] + anchor[1], off[1] + anchor[2], off[2] + anchor[3])

    raw_pts = [local(r) for r, f in zip(recs, fams) if f == "foundation"]
    if len(raw_pts) < 2:
        raw_pts = [local(r) for r, f in zip(recs, fams) if f in ("wall", "door", "window")]
    angles = []
    for i in range(min(len(raw_pts), 80)):
        for j in range(i + 1, min(len(raw_pts), 80)):
            dx = raw_pts[j][0] - raw_pts[i][0]; dy = raw_pts[j][1] - raw_pts[i][1]
            d = math.hypot(dx, dy)
            if 385.0 <= d <= 415.0:
                angles.append(math.degrees(math.atan2(dy, dx)) % 90.0)
    base_theta = sorted(angles)[len(angles) // 2] if angles else 0.0
    if base_theta > 45.0:
        base_theta -= 90.0
    yaws = [yaw(quat(r)) % 90.0 for r, f in zip(recs, fams)
            if f in ("wall", "door", "window", "roof", "foundation")]
    yaw_theta = sorted(yaws)[len(yaws) // 2] if yaws else 0.0
    if yaw_theta > 45.0:
        yaw_theta -= 90.0
    candidates = {round(base_theta, 2), round(yaw_theta, 2),
                  round(base_theta + 90, 2), round(base_theta - 90, 2),
                  round(yaw_theta + 90, 2), round(yaw_theta - 90, 2)}
    f_pts = [local(r) for r, f in zip(recs, fams) if f == "foundation"] or raw_pts

    def grid_cost(deg):
        c, s = math.cos(math.radians(-deg)), math.sin(math.radians(-deg))
        cost = 0.0
        for (x, y, _z) in f_pts:
            rx = c * x - s * y
            ry = s * x + c * y
            for v in (rx, ry):
                r = v % CELL
                cost += min(r, CELL - r)
        return cost

    theta = min(candidates, key=grid_cost)
    ct, st = math.cos(math.radians(-theta)), math.sin(math.radians(-theta))

    # the anchor piece does not have to sit on a cell centre (it can be a door,
    # a wall corner, ...), so estimate the grid phase from the foundations
    def median_residue(values, step):
        residues = sorted(v % step for v in values)
        if not residues:
            return 0.0
        med = residues[len(residues) // 2]
        if med > step / 2:
            med -= step
        return med

    f_rot = [(ct * p[0] - st * p[1], st * p[0] + ct * p[1]) for p in f_pts] if f_pts else []
    off_x = median_residue([p[0] for p in f_rot], CELL) if f_rot else 0.0
    off_y = median_residue([p[1] for p in f_rot], CELL) if f_rot else 0.0

    def rot_local(rec):
        x, y, z = local(rec)
        return (ct * x - st * y, st * x + ct * y, z)

    problems, stats = [], {
        "pieces": len(recs), "types": len(types),
        "edges": len(bp.get("c") or []),
        "anchor_index": a, "frame_rotation_deg": round(theta, 2),
        "families": {f: fams.count(f) for f in sorted(set(fams))},
    }
    tol = 8.0
    def resid(v, step):
        r = v % step
        return min(r, step - r)

    axis_aligned = abs(theta) <= 3.0
    bad = []
    for i, (rec, f) in enumerate(zip(recs, fams), 1):
        x, y, z = rot_local(rec)
        if not all(math.isfinite(v) and abs(v) <= 1e7 for v in (x, y, z)):
            bad.append((i, types[rec[0] - 1], "non-finite transform")); continue
        if not axis_aligned:
            continue                      # rotated blueprint: structural checks only
        if f in ("diagonal", "fence", "prop"):
            continue
        gx = resid(x - off_x, CELL)
        gy = resid(y - off_y, CELL)
        if f == "foundation":
            if gx > tol or gy > tol:
                bad.append((i, types[rec[0] - 1], "foundation off the 400 grid"))
        elif f in ("wall", "door", "window"):
            # valid wall = one axis on a cell centre, the other on a cell edge
            on_x_edge = abs(gy - EDGE) <= tol and gx <= tol
            on_y_edge = abs(gx - EDGE) <= tol and gy <= tol
            if not (on_x_edge or on_y_edge):
                bad.append((i, types[rec[0] - 1], "wall not on a foundation edge"))
        elif f == "roof":
            if gx > 10 or gy > 10:
                bad.append((i, types[rec[0] - 1], "roof not on a cell centre"))
    if bad:
        problems.append("grid/height anomalies on %d piece(s), e.g. %s"
                        % (len(bad), "; ".join("#%d %s %s" % b for b in bad[:6])))

    zf = sorted({round(z, 2) for z in (rot_local(r)[2] for r, f in zip(recs, fams) if f == "foundation")})
    zw = sorted({round(z, 2) for z in (rot_local(r)[2] for r, f in zip(recs, fams) if f in ("wall", "door", "window"))})
    zr = sorted({round(z, 2) for z in (rot_local(r)[2] for r, f in zip(recs, fams) if f == "roof")})
    stats["foundation_z"], stats["wall_z"], stats["roof_z"] = zf[:4], zw[:4], zr[:4]
    if len(zf) > 1:
        problems.append("foundations are not coplanar: z=%s" % zf[:6])
    if zw and zr:
        ground = zw[0]                     # lowest wall plane = the ground storey
        if not any(abs(rz - (ground + WALL_H)) <= 12 for rz in zr):
            problems.append("no roof %.0f cm above the ground wall plane z=%.1f"
                            % (WALL_H, ground))

    # duplicates
    seen = {}
    for i, rec in enumerate(recs, 1):
        key = (types[rec[0] - 1], round(rec[1]), round(rec[2]), round(rec[3]))
        seen.setdefault(key, []).append(i)
    dups = [v for v in seen.values() if len(v) > 1]
    if dups:
        stats["duplicates"] = len(dups)

    # graph
    adj = {i: set() for i in range(1, len(recs) + 1)}
    for e in bp.get("c") or []:
        adj[e[0]].add(e[1]); adj[e[1]].add(e[0])
    start = a if 1 <= a <= len(recs) else 1
    seen_nodes, stack = {start}, [start]
    while stack:
        cur = stack.pop()
        for nb in adj[cur]:
            if nb not in seen_nodes:
                seen_nodes.add(nb); stack.append(nb)
    stats["reachable_from_anchor"] = len(seen_nodes)
    if len(seen_nodes) != len(recs):
        problems.append("%d piece(s) unreachable from anchor: %s"
                        % (len(recs) - len(seen_nodes),
                           sorted(set(range(1, len(recs) + 1)) - seen_nodes)[:8]))

    # structural links
    link = {(e[0], e[1]) for e in bp.get("c") or []}
    def linked(i, kinds):
        for j in range(1, len(recs) + 1):
            if j == i or fams[j - 1] not in kinds:
                continue
            if (i, j) in link or (j, i) in link:
                return True
        return False
    orphan_walls = [i for i, f in enumerate(fams, 1)
                    if f in ("wall", "door", "window", "stairs", "pillar")
                    and not linked(i, ("foundation", "wall", "door", "window", "stairs", "pillar"))]
    orphan_roofs = [i for i, f in enumerate(fams, 1)
                    if f == "roof" and not linked(i, ("wall", "door", "window", "pillar", "roof"))]
    warnings = []
    if orphan_walls:
        warnings.append("%d wall-like piece(s) have no saved structural link (the "
                        "mod/support gate may still accept them): %s"
                        % (len(orphan_walls), orphan_walls[:8]))
    if orphan_roofs:
        warnings.append("%d roof piece(s) have no saved support link: %s"
                        % (len(orphan_roofs), orphan_roofs[:8]))
    return problems, stats, warnings


def check_catalog(bp):
    """Objective checks that need the build catalog: known ids, material budget,
    and a conservative same-slot overlap test for pieces with known sizes."""
    problems, warnings = [], []
    types = bp.get("t") or []
    known = {k.lower() for k in CATALOG}
    unknown = sorted({t for t in types if t.lower() not in known})
    if unknown:
        warnings.append("ids not in catalog.json (untested types): %s"
                        % ", ".join(unknown[:8]))
    materials = {}
    missing_mats = []
    for t in types:
        entry = CATALOG.get(t) or CATALOG.get(t.lower()) or {}
        mats = entry.get("materials") or []
        if not mats and t not in [x for x in types if x not in unknown]:
            missing_mats.append(t)
        for m in mats:
            item = m.get("id") or m.get("item")
            materials[item] = materials.get(item, 0) + m["count"] * sum(
                1 for r in bp["b"] if bp["t"][r[0] - 1] == t)
    # conservative overlap: same family, same height, centres closer than a piece
    # width, and both sizes known -> almost certainly the same slot twice
    recs, fams = bp.get("b") or [], [family(t) for t in types]
    hits = []
    for i in range(len(recs)):
        for j in range(i + 1, len(recs)):
            fa, fb = fams[recs[i][0] - 1], fams[recs[j][0] - 1]
            if fa != fb or fa not in ("foundation", "roof", "wall", "door", "window"):
                continue
            a, b = recs[i], recs[j]
            dx, dy, dz = a[1] - b[1], a[2] - b[2], a[3] - b[3]
            if abs(dz) > 20:
                continue
            if math.hypot(dx, dy) < 60:
                hits.append((i + 1, j + 1))
    if hits:
        warnings.append("possible duplicate slots (same family/height, <60cm apart): %s"
                        % hits[:6])
    stats = {"materials": materials, "unknown_types": len(unknown),
             "catalog_buildid": CATALOG_VERSION.get("steam_buildid")}
    return problems, warnings, stats


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if not args:
        raise SystemExit(__doc__)
    path = args[0]
    try:
        bp = load(path)
    except Exception as exc:                       # noqa: BLE001
        print(json.dumps({"file": path, "ok": False, "error": str(exc)}))
        return 2
    if not isinstance(bp, dict) or "t" not in bp or "b" not in bp:
        print(json.dumps({"file": path, "ok": False,
                          "error": "not a schema-9 blueprint (authoring file? run export.py first)"},
                         ensure_ascii=False))
        return 2
    problems = check_schema(bp)
    geo_problems, stats, warnings = check_geometry(bp)
    problems += geo_problems
    cat_problems, cat_warnings, cat_stats = check_catalog(bp)
    problems += cat_problems
    warnings += cat_warnings
    stats.update(cat_stats)
    report = {"file": os.path.basename(path), "ok": not problems,
              "problems": problems, "stats": stats}
    if "--json" in sys.argv:
        print(json.dumps(report, ensure_ascii=False, indent=1))
    elif "--quiet" not in sys.argv:
        print(("PASS " if report["ok"] else "FAIL ") + report["file"])
        for p in problems:
            print("  x " + p)
        print("  " + json.dumps(stats, ensure_ascii=False))
    return 0 if report["ok"] else 1


if __name__ == "__main__":
    sys.exit(main())









