#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Design source for "小鳥台・小" (Kotori Keep Compact) -- the small-footprint variant.

Same design language as `gen_kotori_keep.py`, on a 9 x 7 cell platform instead of 13 x 9:
36 m x 28 m instead of 52 m x 36 m (54% of the ground area, ~46% less), 2280 cm circumradius
about the Palbox instead of 3162 cm, and about half the pieces and materials.

Every positional rule is owned by `data/format.md`: 400 cm cell grid, foundations on cell
centres, walls on cell edges (+/-200), wall height 325, yaw west=0 / east=180 / south=90 /
north=270.  The toolchain constraints are the same ones documented in `gen_kotori_keep.py`:

  * one coplanar foundation plane at z=0 (single storey, forced by validate.py);
  * roofs only at z=325 (forced by export.py's wall/foundation link rule);
  * no stairs (family "stairs" only links to a wall 325 cm below);
  * one 4-connected foundation rectangle (foundation edges are only emitted at 1-CELL range);
  * `JapaneseStyle_SlantedRoof` is a 4 m shed slope rising from local x=+2.007 to x=-2.000,
    so a 2-cell pair makes exactly one 8 m gable, and a gable end left open above the wall
    reads as a colonnade rather than a hole -- hence the halls are open on the court side
    and on the camera-facing south side.

Chain:
    python authoring/gen_kotori_compact.py
    python tooling/export.py authoring/kotori_compact.json -o out
    python tooling/validate.py out/kotori_compact.json
    node tooling/render_offline.mjs out/kotori_compact.json out/kotori_compact.png
"""
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

FLOOR_WOOD = "Wooden_foundation"
FLOOR_STONE = "Stone_Foundation"
WALL = "Wooden_wall"
GATE = "JapaneseStyle_DoorWall_01"
JWALL = "JapaneseStyle_wall_01"
JWIN = "JapaneseStyle_WindowWall"
JROOF_SLOPE = "JapaneseStyle_SlantedRoof"
PILLAR = "Stone_pillar"

W, D = 9, 7                      # 9 x 7 cells = 36 m x 28 m
CELL, EDGE = 400, 200
FLOOR_Z, ROOF_Z = 0, 325
X_MAX = (W - 1) // 2 * CELL + EDGE      # 1800
Y_MAX = (D - 1) // 2 * CELL + EDGE      # 1400


def X(i):
    return (i - (W - 1) // 2) * CELL


def Y(j):
    return (j - (D - 1) // 2) * CELL


PIECES, WALLS, POSTED = [], [], set()


def add(bid, x, y, z, yaw=0, is_wall=False):
    PIECES.append({"id": bid, "x": x, "y": y, "z": z, "yaw": yaw})
    if is_wall:
        WALLS.append((x, y))


def wall(i, j, side, bid):
    table = {"W": (X(i) - EDGE, Y(j), 0), "E": (X(i) + EDGE, Y(j), 180),
             "S": (X(i), Y(j) - EDGE, 90), "N": (X(i), Y(j) + EDGE, 270)}
    x, y, yaw = table[side]
    add(bid, x, y, FLOOR_Z, yaw, is_wall=True)


def post(x, y):
    if (x, y) in POSTED:
        return
    POSTED.add((x, y))
    add(PILLAR, x, y, FLOOR_Z, 0)


def lattice(i0, i1, j0, j1, skip_perimeter=False):
    xs = [X(i) - EDGE for i in range(i0, i1 + 1)] + [X(i1) + EDGE]
    ys = [Y(j) - EDGE for j in range(j0, j1 + 1)] + [Y(j1) + EDGE]
    pts = [(x, ys[0]) for x in xs] + [(x, ys[-1]) for x in xs]
    pts += [(xs[0], y) for y in ys[1:-1]] + [(xs[-1], y) for y in ys[1:-1]]
    if skip_perimeter:
        pts = [(x, y) for (x, y) in pts if abs(x) != X_MAX and abs(y) != Y_MAX]
    out, seen = [], set()
    for p in pts:
        if p not in seen:
            seen.add(p)
            out.append(p)
    return out


# ---------------------------------------------------------------- 1. platform
# stone podium ring hides the wooden deck's 4 m legs; small stone forecourt in the south
STONE_CELLS = {(i, j) for i in range(W) for j in (0,)}          # forecourt row
STONE_CELLS |= {(i, j) for i in range(W) for j in (0, D - 1)}    # south + north edge
STONE_CELLS |= {(i, j) for j in range(D) for i in (0, W - 1)}    # west + east edge
STONE_CELLS |= {(i, 1) for i in range(3, 6)}                     # gate apron

for i in range(W):
    for j in range(D):
        add(FLOOR_STONE if (i, j) in STONE_CELLS else FLOOR_WOOD, X(i), Y(j), FLOOR_Z)

# ------------------------------------------------- 2. perimeter, one 3-bay gate
GATE_COLS = (3, 4, 5)
for i in range(W):
    wall(i, 0, "S", GATE if i in GATE_COLS else WALL)
    wall(i, D - 1, "N", WALL)
for j in range(D):
    wall(0, j, "W", WALL)
    wall(W - 1, j, "E", WALL)
for x in sorted({X(GATE_COLS[0]) - EDGE, X(3) - EDGE, X(4) - EDGE, X(GATE_COLS[-1]) + EDGE}):
    post(x, Y(0) - EDGE)
for (cx, cy) in ((X_MAX, Y_MAX), (X_MAX, -Y_MAX), (-X_MAX, Y_MAX), (-X_MAX, -Y_MAX)):
    post(cx, cy)


# ------------------------------------------------------- 3. two facing halls
def gable(i0, i1, j0, j1):
    """Panels are 4 m single slopes: yaw 180 puts the ridge on the cell's +x edge,
    yaw 0 on its -x edge, so one mirrored pair makes exactly one 8 m gable."""
    i = i0
    while i + 1 <= i1:
        for j in range(j0, j1 + 1):
            add(JROOF_SLOPE, X(i), Y(j), ROOF_Z, 180)
            add(JROOF_SLOPE, X(i + 1), Y(j), ROOF_Z, 0)
        i += 2


def side_walls(i0, i1, j0, j1, side):
    if side in ("W", "E"):
        rng = list(range(j0, j1 + 1))
        ii = i0 if side == "W" else i1
        for k, j in enumerate(rng):
            wall(ii, j, side, JWIN if k == len(rng) // 2 else JWALL)
    else:
        rng = list(range(i0, i1 + 1))
        jj = j0 if side == "S" else j1
        for k, i in enumerate(rng):
            wall(i, jj, side, JWIN if k == len(rng) // 2 else JWALL)


def hall(i0, i1, j0, j1, open_sides):
    for (px, py) in lattice(i0, i1, j0, j1):
        post(px, py)
    for side in ("W", "E", "S", "N"):
        if side not in open_sides:
            side_walls(i0, i1, j0, j1, side)
    gable(i0, i1, j0, j1)


W_HALL = dict(i0=1, i1=2, j0=2, j1=4)      # living / storage, opens east + south
E_HALL = dict(i0=6, i1=7, j0=2, j1=4)      # production, opens west + south
hall(W_HALL["i0"], W_HALL["i1"], W_HALL["j0"], W_HALL["j1"], open_sides={"E", "S"})
hall(E_HALL["i0"], E_HALL["i1"], E_HALL["j0"], E_HALL["j1"], open_sides={"W", "S"})

# ---------------------------------------------------------------- 4. facilities
ZONES = {
    "core":  (3, 5, 2, 4),      # Palbox dead centre + feed boxes + hot springs
    "bedW":  (0, 0, 2, 5),      # pal bed block, west lane
    "bedE":  (8, 8, 2, 5),      # power, east lane
    "hallW": (1, 2, 2, 4),      # living hall interior
    "hallE": (6, 7, 2, 4),      # production hall interior
    "farm":  (1, 7, 5, 6),      # plantation terrace + ranch
    "court": (0, 8, 0, 1),      # forecourt dressing
}

DEMAND = [
    ("core", "PalBoxV2"),
    ("core", "PalFoodBox"), ("core", "CoolerPalFoodBox"), ("core", "PalMedicineBox"),
    ("core", "Spa2"), ("core", "Spa2"),
    ("bedW", "MedicalPalBed_05"), ("bedW", "MedicalPalBed_05"),
    ("bedW", "MedicalPalBed_05"), ("bedW", "MedicalPalBed_05"),
    ("bedE", "ElectricGenerator"), ("bedE", "EnergyStorage_Electric"),
    ("bedE", "ElectricGenerator_Large"),
    ("hallW", "PlayerBed_03"), ("hallW", "GuildChest"),
    ("hallW", "ItemChest_03"), ("hallW", "ItemChest_03"),
    ("hallW", "HugeKitchen"), ("hallW", "Cooler"),
    ("hallE", "WorkBench"), ("hallE", "AncientWorkBench"), ("hallE", "BlastFurnace"),
    ("hallE", "Crusher"), ("hallE", "FlourMill"), ("hallE", "IceCrusher"),
    ("farm", "FarmBlockV2_Berries"), ("farm", "FarmBlockV2_Berries"),
    ("farm", "FarmBlockV2_wheet"), ("farm", "FarmBlockV2_tomato"),
    ("farm", "FarmBlockV2_Lettuce"), ("farm", "FarmBlockV2_Potato"),
    ("farm", "MonsterFarm"), ("farm", "PalFoodBox"),
    ("court", "GuardianDogStatue"), ("court", "GuardianDogStatue"),
    ("court", "Light_LightPole01"), ("court", "Light_LightPole01"),
    ("court", "Plant01_Plant"), ("court", "Plant02_Plant"),
]

CLEAR = EDGE
occupied = set()


def best_cell(zone, prefer):
    i0, i1, j0, j1 = ZONES[zone]
    best, bestd = None, None
    for i in range(i0, i1 + 1):
        for j in range(j0, j1 + 1):
            if (i, j) in occupied:
                continue
            px, py = X(i), Y(j)
            if any(max(abs(px - wx), abs(py - wy)) < CLEAR for wx, wy in WALLS):
                continue
            d = abs(px - prefer[0]) + abs(py - prefer[1])
            if best is None or d < bestd:
                best, bestd = (i, j), d
    return best


CORE = (X(4), Y(3))
unplaced = []
for zone, bid in DEMAND:
    prefer = CORE if zone in ("core", "bedW", "bedE") else (X(4), Y(5) if zone == "farm" else Y(0))
    cell = best_cell(zone, prefer)
    if cell is None:
        unplaced.append((zone, bid))
        continue
    occupied.add(cell)
    add(bid, X(cell[0]), Y(cell[1]), FLOOR_Z, 0)

# ==========================================================================
# emit + audits
# ==========================================================================
auth = {"name": "kotori_compact", "anchor": 0, "pieces": PIECES}
out = os.path.join(ROOT, "authoring", "kotori_compact.json")
with open(out, "w", encoding="utf-8", newline="\n") as fh:
    json.dump(auth, fh, ensure_ascii=False, indent=1)

seen, dups = {}, []
for n, p in enumerate(PIECES, 1):
    k = (p["id"], p["x"], p["y"], p["z"])
    if k in seen:
        dups.append((n, seen[k], p["id"], p["x"], p["y"]))
    seen[k] = n

SLOPE_LX, SLOPE_LZ = (-204.0, 219.0), (-207.6, 207.6)
ROOFS = [(p["x"], p["y"], p["yaw"]) for p in PIECES if p["id"] == JROOF_SLOPE]


def slope_rect(x, y, yaw):
    x0, x1 = SLOPE_LX
    z0, z1 = SLOPE_LZ
    if yaw == 0:
        return (x + x0, x + x1, y + z0, y + z1)
    if yaw == 180:
        return (x - x1, x - x0, y + z0, y + z1)
    return (x - z1, x - z0, y - x1, y - x0)


def coverage(i0, i1, j0, j1, step=50.0):
    total, missed = 0, 0
    y = Y(j0) - EDGE
    while y <= Y(j1) + EDGE + 1e-6:
        x = X(i0) - EDGE
        while x <= X(i1) + EDGE + 1e-6:
            total += 1
            if not any(rx0 <= x <= rx1 and ry0 <= y <= ry1
                       for (rx, ry, ryaw) in ROOFS
                       for (rx0, rx1, ry0, ry1) in (slope_rect(rx, ry, ryaw),)):
                missed += 1
            x += step
        y += step
    return total, missed


print("wrote %s" % out)
print("pieces=%d  types=%d  facilities=%d  walls=%d"
      % (len(PIECES), len({p['id'] for p in PIECES}), len(DEMAND), len(WALLS)))
print("exact duplicate pieces: %d %s" % (len(dups), dups[:4]))
print("unplaced facilities: %s" % (unplaced or "none"))
print("platform: %d x %d cells = %.0f m x %.0f m  (%.0f m2)"
      % (W, D, W * CELL / 100.0, D * CELL / 100.0, W * CELL * D * CELL / 10000.0))
print("circumradius about Palbox: %.0f cm  (build radius ~3500 cm)" % ((X_MAX ** 2 + Y_MAX ** 2) ** 0.5))
for (label, h) in (("W hall", W_HALL), ("E hall", E_HALL)):
    t, m = coverage(h["i0"], h["i1"], h["j0"], h["j1"])
    print("roof coverage %s: %d missed / %d" % (label, m, t))
