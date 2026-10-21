#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Design source for the "Kotori Keep" (小鳥台) Palworld base blueprint.

This file is the DESIGN, not a tool: it emits `authoring/kotori_keep.json`, the contract
input of `tooling/export.py`.  Every positional rule it obeys is owned by
`data/format.md`: 400 cm cell grid, foundations on cell centres, walls on cell edges
(+/-200), wall height 325, yaw west=0 / east=180 / south=90 / north=270.

Hard constraints found by reading the toolchain (each verified offline):

  * `validate.py` fails any blueprint whose foundation family spans more than one z, so
    the whole base is ONE coplanar foundation plane at z=0.  Single storey is forced --
    and it matches the gameplay advice to keep Pal-worked stations on open flat ground.
  * A roof can only sit at z=325: `export.py` links a roof UP to a wall 325 cm below or
    DOWN to the foundation 325 cm below; any other roof height is unreachable and fails
    validation.  Every roof piece is therefore at z=325, which is why the base is one
    flat-roof/gable tier rather than stacked storeys.
  * Stairs are family "stairs" and likewise only link to a wall 325 cm below, so a
    ground-level staircase cannot exist here.  The base has none.
  * `export.py` only emits foundation<->foundation edges for pieces exactly one CELL
    apart, so the platform must be one 4-connected rectangle.
  * `validate.py` classifies any id containing "wall" as a wall, which then has to sit on
    a cell edge -- so wall-hung items (WallTorch, WallSignboard, ...) are avoided.
  * `JapaneseStyle_SlantedRoof` is a single 4 m shed slope rising 3.94 m from local
    x=+2.007 to x=-2.000 with the ridge along local z (measured from the mesh vertices).
    Two mirrored panels therefore make an 8 m gable, but a WIDER building needs a second
    gable and its eave-to-eave valley dips to ~2.99 m -- BELOW the 3.19 m wall top.  A
    gable end is also open above the wall.  Both facts drive the plan: enclosed volumes
    are only two cells wide in the run direction, and every visible gable end is left
    deliberately open as a colonnade instead of walled.

The base is sized to the ~35 m Palbox build radius: 13x9 cells = 52 m x 36 m, whose
circumradius about the Palbox at the centre is 31.6 m.

Deliverable chain:
    python authoring/gen_kotori_keep.py
    python tooling/export.py authoring/kotori_keep.json -o out
    python tooling/validate.py out/kotori_keep.json
    node tooling/render_offline.mjs out/kotori_keep.json out/kotori_keep.png
"""
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# --------------------------------------------------------------------------
# palette -- every id below has a vendored GLB under meshes/, verified by rendering
# a one-of-each probe; ids without a GLB would show up in the preview's `missing`.
# --------------------------------------------------------------------------
FLOOR_WOOD = "Wooden_foundation"
FLOOR_STONE = "Stone_Foundation"     # renders; catalog.json spells it Stone_foundation
WALL = "Wooden_wall"                 # perimeter palisade
GATE = "JapaneseStyle_DoorWall_01"
JWALL = "JapaneseStyle_wall_01"
JWIN = "JapaneseStyle_WindowWall"
JDOOR = "JapaneseStyle_DoorWall_01"
JROOF_SLOPE = "JapaneseStyle_SlantedRoof"
PILLAR = "Stone_pillar"

W, D = 13, 9
CELL, EDGE, WALL_H = 400, 200, 325
FLOOR_Z, ROOF_Z = 0, 325
X_MAX = (W - 1) // 2 * CELL + EDGE      # perimeter wall planes
Y_MAX = (D - 1) // 2 * CELL + EDGE


def X(i):
    return (i - (W - 1) // 2) * CELL


def Y(j):
    return (j - (D - 1) // 2) * CELL


PIECES = []
WALLS = []          # (x, y) of every wall-plane piece, for the clearance audit
POSTED = set()


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
    """Pillar positions on a footprint's outer lattice (corners + edge points)."""
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


# ==========================================================================
# 1. platform -- a stone podium ring around a wooden deck
#    The stone ring is not decoration: Wooden_foundation renders as a deck on
#    legs, and a solid stone perimeter hides that leg forest from outside.
# ==========================================================================
STONE_CELLS = {(i, j) for i in range(W) for j in (0, 1)}            # south forecourt
STONE_CELLS |= {(i, j) for i in range(5, 8) for j in range(2, 6)}    # core plaza
STONE_CELLS |= {(i, j) for i in range(W) for j in (0, D - 1)}        # south + north edge
STONE_CELLS |= {(i, j) for j in range(D) for i in (0, W - 1)}        # west + east edge

for i in range(W):
    for j in range(D):
        add(FLOOR_STONE if (i, j) in STONE_CELLS else FLOOR_WOOD, X(i), Y(j), FLOOR_Z)

# ==========================================================================
# 2. perimeter palisade with a single three-bay gate in the south wall
#    (one deliberate chokepoint per ring, per the raid guidance)
# ==========================================================================
GATE_COLS = (5, 6, 7)
for i in range(W):
    wall(i, 0, "S", GATE if i in GATE_COLS else WALL)
    wall(i, D - 1, "N", WALL)
for j in range(D):
    wall(0, j, "W", WALL)
    wall(W - 1, j, "E", WALL)
for x in sorted({X(5) - EDGE, X(6) - EDGE, X(7) + EDGE, X(4) + EDGE}):
    post(x, Y(0) - EDGE)
for (cx, cy) in ((X_MAX, Y_MAX), (X_MAX, -Y_MAX), (-X_MAX, Y_MAX), (-X_MAX, -Y_MAX)):
    post(cx, cy)

# ==========================================================================
# 3. four roofed halls in a 2x2 arrangement, mirrored about both axes
#    Each hall is 4 cells along x and 3 along y.  Enclosed sides are only the
#    outward ones; the courtyard side (and, for the south pair, the camera-facing
#    side as well) is left open as a colonnade, so every visible gable end reads
#    as a deliberate opening instead of a hole above a wall.
# ==========================================================================
def gable(i0, i1, j0, j1):
    """Double-gable roof over a 4-cell width.

    Two mirrored panels make one 8 m gable: the panel at cell i carries yaw 180
    (ridge 1.94 m EAST of centre) and the panel at cell i+1 carries yaw 0 (ridge
    1.94 m WEST of centre), so their ridges land on the shared cell edge and their
    eaves land on the pair's outer edges.  Repeating the pair tiles the footprint;
    repeating along j extends the ridge.
    """
    i = i0
    while i + 1 <= i1:
        for j in range(j0, j1 + 1):
            add(JROOF_SLOPE, X(i), Y(j), ROOF_Z, 180)
            add(JROOF_SLOPE, X(i + 1), Y(j), ROOF_Z, 0)
        i += 2


def side_walls(i0, i1, j0, j1, side):
    """One enclosed side of a hall: solid panels with a window bay at the middle."""
    if side in ("W", "E"):
        rng = list(range(j0, j1 + 1))
        ii = i0 if side == "W" else i1
        for k, j in enumerate(rng):
            wall(ii, j, side, JWIN if k == len(rng) // 2 else JWALL)
    else:
        rng = list(range(i0, i1 + 1))
        jj = j0 if side == "S" else j1
        mid = {len(rng) // 2}
        if len(rng) % 2 == 0:
            mid.add(len(rng) // 2 - 1)
        for k, i in enumerate(rng):
            wall(i, jj, side, JWIN if k in mid else JWALL)


def hall(i0, i1, j0, j1, open_sides):
    """Pillars on the lattice, walls only on the enclosed sides, gable on top."""
    for (px, py) in lattice(i0, i1, j0, j1):
        post(px, py)
    if "W" not in open_sides:
        side_walls(i0, i1, j0, j1, "W")
    if "E" not in open_sides:
        side_walls(i0, i1, j0, j1, "E")
    if "S" not in open_sides:
        side_walls(i0, i1, j0, j1, "S")
    if "N" not in open_sides:
        side_walls(i0, i1, j0, j1, "N")
    gable(i0, i1, j0, j1)


# courtyard side of the north pair is S; the south pair is open on both N and S
NW = dict(i0=1, i1=4, j0=5, j1=7)
NE = dict(i0=8, i1=11, j0=5, j1=7)
SW = dict(i0=1, i1=4, j0=2, j1=4)
SE = dict(i0=8, i1=11, j0=2, j1=4)

hall(NW["i0"], NW["i1"], NW["j0"], NW["j1"], open_sides={"S"})
hall(NE["i0"], NE["i1"], NE["j0"], NE["j1"], open_sides={"S"})
hall(SW["i0"], SW["i1"], SW["j0"], SW["j1"], open_sides={"N", "S"})
hall(SE["i0"], SE["i1"], SE["j0"], SE["j1"], open_sides={"N", "S"})

# colonnade around the central Palbox plaza, so the axis reads as a court
for (px, py) in lattice(5, 7, 3, 5):
    post(px, py)

# ==========================================================================
# 4. facilities
#    Props carry no vendored mesh, so they do not appear in the offline preview;
#    the placement rule below is what keeps their siting honest.
# ==========================================================================
ZONES = {
    "core":    (5, 7, 3, 5),      # Palbox dead centre, feed boxes, hot springs
    "farm":    (5, 7, 6, 8),      # nine plantation plots on the north axis
    "court":   (0, 12, 0, 1),     # forecourt: gate court, statues, planting
    "bedW":    (4, 4, 2, 4),      # pal beds beside the core
    "bedE":    (8, 8, 2, 4),
    "westLane": (0, 0, 2, 8),     # west garden lane
    "eastLane": (12, 12, 2, 4),   # east garden lane
    "ranch":   (12, 12, 5, 8),    # ranch strip in the east garden
    "hallNW":  (1, 4, 5, 7),      # under the north-west roof: beds + storage
    "hallNE":  (8, 11, 5, 7),     # under the north-east roof: production
    "hallSW":  (1, 4, 2, 4),      # under the south-west roof: kitchen + power
    "hallSE":  (8, 11, 2, 4),     # under the south-east roof: logistics
}

DEMAND = [
    ("core", "PalBoxV2"),
    ("core", "PalFoodBox"), ("core", "CoolerPalFoodBox"),
    ("core", "Spa2"), ("core", "Spa2"), ("core", "Spa2"), ("core", "Spa2"),
    ("core", "PalMedicineBox"),
    ("bedW", "MedicalPalBed_05"), ("bedW", "MedicalPalBed_05"), ("bedW", "MedicalPalBed_05"),
    ("bedE", "MedicalPalBed_05"), ("bedE", "MedicalPalBed_05"), ("bedE", "MedicalPalBed_05"),
    ("hallNW", "PlayerBed_03"), ("hallNW", "GuildChest"),
    ("hallNW", "ItemChest_03"), ("hallNW", "ItemChest_03"),
    ("hallSW", "HugeKitchen"), ("hallSW", "ElectricGenerator"),
    ("hallSW", "EnergyStorage_Electric"), ("hallSW", "Cooler"), ("hallSW", "Heater"),
    ("hallNE", "WorkBench"), ("hallNE", "AncientWorkBench"),
    ("hallNE", "BlastFurnace"), ("hallNE", "BlastFurnace2"),
    ("hallNE", "Crusher"), ("hallNE", "FlourMill"), ("hallNE", "IceCrusher"),
    ("hallNE", "ElectricGenerator_Large"), ("hallNE", "CoolerBox"),
    ("hallSE", "ItemChest_03"), ("hallSE", "ItemChest_03"), ("hallSE", "ItemChest_03"),
    ("hallSE", "Silo"), ("hallSE", "CoolerPalFoodBox"),
    ("eastLane", "Silo"), ("eastLane", "Signboard"), ("eastLane", "ItemChest_03"),
    ("farm", "FarmBlockV2_Berries"), ("farm", "FarmBlockV2_Berries"),
    ("farm", "FarmBlockV2_wheet"), ("farm", "FarmBlockV2_wheet"),
    ("farm", "FarmBlockV2_tomato"), ("farm", "FarmBlockV2_tomato"),
    ("farm", "FarmBlockV2_Lettuce"), ("farm", "FarmBlockV2_Potato"),
    ("farm", "FarmBlockV2_Carrot"),
    ("ranch", "MonsterFarm"), ("ranch", "BreedFarm"), ("ranch", "MonsterFarm"),
    ("ranch", "PalFoodBox"),
    ("court", "GuardianDogStatue"), ("court", "GuardianDogStatue"),
    ("court", "FlowerBed"), ("court", "Light_LightPole01"), ("court", "Light_LightPole01"),
    ("westLane", "Plant01_Plant"), ("westLane", "Plant02_Plant"),
    ("westLane", "FurnitureBush01_Flower"), ("westLane", "Light_LightPole01"),
]

# A facility cell is "clear" when its centre is at least one wall-edge from every wall
# plane.  Walls sit on cell edges, so 200 cm is the closest a cell centre can be to one;
# a 400x400 plantation plot centred in its own cell then abuts the wall plane exactly.
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


CORE = (X(6), Y(4))
unplaced = []
for zone, bid in DEMAND:
    prefer = CORE if zone in ("core", "bedW", "bedE") else (X(6), Y(6))
    cell = best_cell(zone, prefer)
    if cell is None:
        unplaced.append((zone, bid))
        continue
    occupied.add(cell)
    add(bid, X(cell[0]), Y(cell[1]), FLOOR_Z, 0)

# ==========================================================================
# emit + audits
# ==========================================================================
auth = {"name": "kotori_keep", "anchor": 0, "pieces": PIECES}
out = os.path.join(ROOT, "authoring", "kotori_keep.json")
with open(out, "w", encoding="utf-8", newline="\n") as fh:
    json.dump(auth, fh, ensure_ascii=False, indent=1)

# --- duplicate audit ------------------------------------------------------
seen, dups = {}, []
for n, p in enumerate(PIECES, 1):
    k = (p["id"], p["x"], p["y"], p["z"])
    if k in seen:
        dups.append((n, seen[k], p["id"], p["x"], p["y"]))
    seen[k] = n

# --- roof plan-coverage audit --------------------------------------------
SLOPE_LX = (-204.0, 219.0)
SLOPE_LZ = (-207.6, 207.6)
ROOFS = [(p["x"], p["y"], p["yaw"]) for p in PIECES if p["id"] == JROOF_SLOPE]


def slope_rect(x, y, yaw):
    x0, x1 = SLOPE_LX
    z0, z1 = SLOPE_LZ
    if yaw == 0:
        return (x + x0, x + x1, y + z0, y + z1)
    if yaw == 180:
        return (x - x1, x - x0, y + z0, y + z1)
    if yaw == 90:
        return (x - z1, x - z0, y + x0, y + x1)
    return (x - z1, x - z0, y - x1, y - x0)


def coverage(i0, i1, j0, j1, step=50.0):
    total, missed, first = 0, 0, []
    y = Y(j0) - EDGE
    while y <= Y(j1) + EDGE + 1e-6:
        x = X(i0) - EDGE
        while x <= X(i1) + EDGE + 1e-6:
            total += 1
            if not any(rx0 <= x <= rx1 and ry0 <= y <= ry1
                       for (rx, ry, ryaw) in ROOFS
                       for (rx0, rx1, ry0, ry1) in (slope_rect(rx, ry, ryaw),)):
                missed += 1
                if len(first) < 3:
                    first.append((round(x), round(y)))
            x += step
        y += step
    return total, missed, first


print("wrote %s" % out)
print("pieces=%d  types=%d  facilities=%d  walls=%d"
      % (len(PIECES), len({p['id'] for p in PIECES}), len(DEMAND), len(WALLS)))
print("exact duplicate pieces: %d %s" % (len(dups), dups[:4]))
print("unplaced facilities: %s" % (unplaced or "none"))
print("platform circumradius: %.0f cm (Palbox build radius ~3500 cm)"
      % ((X_MAX ** 2 + Y_MAX ** 2) ** 0.5))
print("roof plan coverage per hall (samples missed / total):")
for (label, h) in (("NW", NW), ("NE", NE), ("SW", SW), ("SE", SE)):
    t, m, first = coverage(h["i0"], h["i1"], h["j0"], h["j1"])
    print("  %s: %d / %d  %s" % (label, m, t, first if m else ""))
