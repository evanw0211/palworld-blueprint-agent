#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Build the version-locked blueprint catalogue from the game's own data tables.

The placeable set is fixed per game version, so this script snapshots it and stamps
the version. Re-run after a game update (see README at the bottom).

Inputs (produced with tools/PalAssetExport `dumptable`):
  data/raw/DT_BuildObjectDataTable_Common.json     all placeables + materials/typeA/typeB/install flags
  data/raw/DT_MapObjectMasterDataTable_Common.json MapObjectId -> blueprint class
  data/raw/DT_MapObjectNameText_{en,zh}.json       localized names
  data/raw/DT_BuildObjectCategoryText_{en,zh}.json enum -> localized category labels
Previous catalog.json (optional) contributes measured sizes (glb bbox) and live-probe flags.

Outputs:
  data/catalog.json        entries + version stamp + counts
  data/build_ids.json      id list, aliases, counts
  data/catalog_summary.md  human-readable summary
"""
import datetime
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(HERE, "raw")
CATALOG = os.path.join(HERE, "catalog.json")
BUILD_IDS = os.path.join(HERE, "build_ids.json")
SUMMARY = os.path.join(HERE, "catalog_summary.md")
ACF = r"D:\SteamLibrary\steamapps\appmanifest_1623730.acf"
MOD_INFO = r"D:\SteamLibrary\steamapps\workshop\content\1623730\3768908799\Info.json"


def load(path, default=None):
    try:
        return json.load(open(path, encoding="utf-8-sig"))
    except Exception:                                # noqa: BLE001
        return default


def rows(path):
    data = load(path, {}) or {}
    if not data:
        return {}
    return data[list(data.keys())[0]]


def strip_enum(v):
    if not isinstance(v, str):
        return v
    return v.split("::")[-1] if "::" in v else v


def version_stamp():
    stamp = {"steam_buildid": None, "last_updated": None, "game": "Palworld"}
    if os.path.exists(ACF):
        txt = open(ACF, encoding="utf-8", errors="ignore").read()
        m = re.search(r'"buildid"\s+"([^"]*)"', txt)
        if m:
            stamp["steam_buildid"] = m.group(1)
        m = re.search(r'"LastUpdated"\s+"([^"]*)"', txt)
        if m:
            stamp["last_updated"] = datetime.datetime.utcfromtimestamp(int(m.group(1))).isoformat() + "Z"
    info = load(MOD_INFO, {}) or {}
    stamp["mod"] = {"name": info.get("ModName"), "version": info.get("Version"),
                    "workshop_id": "3768908799", "nexus_id": "4073"}
    exe = r"D:\SteamLibrary\steamapps\common\Palworld\Pal\Binaries\Win64\Palworld-Win64-Shipping.exe"
    if os.path.exists(exe):
        st = os.stat(exe)
        stamp["exe_size"] = st.st_size
        stamp["exe_mtime"] = datetime.datetime.utcfromtimestamp(st.st_mtime).isoformat() + "Z"
    stamp["generated_at"] = datetime.datetime.utcnow().isoformat() + "Z"
    return stamp


def main():
    build_rows = rows(os.path.join(RAW, "DT_BuildObjectDataTable_Common.json"))
    master = rows(os.path.join(RAW, "DT_MapObjectMasterDataTable_Common.json"))
    names_en = rows(os.path.join(RAW, "DT_MapObjectNameText_en.json"))
    names_zh = rows(os.path.join(RAW, "DT_MapObjectNameText_zh.json"))
    cats_en = rows(os.path.join(RAW, "DT_BuildObjectCategoryText_en.json"))
    cats_zh = rows(os.path.join(RAW, "DT_BuildObjectCategoryText_zh.json"))
    if not build_rows:
        raise SystemExit("raw dumps missing - run tools/PalAssetExport dumptable first")

    old = load(CATALOG, {}) or {}
    old_entries = old.get("entries", old) if isinstance(old, dict) else {}
    if not isinstance(old_entries, dict):
        old_entries = {}

    def name_text(table, map_id):
        row = table.get("MAPOBJECT_NAME_" + map_id) or {}
        return row.get("TextData")

    PLACEHOLDERS = {"ex Text", "zh-hans text", ""}

    def label(table, kind, value):
        if not value:
            return None
        prefixes = {"A": ("CATEGORY_TYPE_A_",),
                    "B": ("CATEGORY_TYPE_B_",),
                    "UI": ("CATEGORY_TYPE_UI_", "CATEGORY_UI_")}[kind]
        for prefix in prefixes:
            key = prefix + value
            if key in table:
                text = table[key].get("TextData")
                if text and text.strip() not in PLACEHOLDERS:
                    return text
        return None

    entries = {}
    for bid, row in sorted(build_rows.items()):
        map_id = row.get("MapObjectId") or bid
        mrow = master.get(map_id) or {}
        materials = []
        for i in (1, 2, 3, 4):
            item = row.get("Material%d_Id" % i)
            count = row.get("Material%d_Count" % i)
            if not item or item == "None" or not count:
                continue
            materials.append({"id": item, "count": int(count)})
        type_a = strip_enum(row.get("TypeA"))
        type_b = strip_enum(row.get("TypeB"))
        type_ui = strip_enum(row.get("TypeUIDisplay"))
        old_entry = old_entries.get(bid) or {}
        entry = {
            "id": bid,
            "map_object_id": map_id,
            "name": {"en": name_text(names_en, map_id), "zh": name_text(names_zh, map_id)},
            "category": {
                "type_a": type_a, "type_a_label": label(cats_en, "A", type_a),
                "type_a_label_zh": label(cats_zh, "A", type_a),
                "type_b": type_b, "type_b_label": label(cats_en, "B", type_b),
                "type_ui": type_ui, "type_ui_label": label(cats_en, "UI", type_ui),
                "type_ui_label_zh": label(cats_zh, "UI", type_ui),
            },
            "rank": row.get("Rank"),
            "materials": materials,
            "work_amount": row.get("RequiredBuildWorkAmount"),
            "paintable": bool(row.get("bIsPaintable")),
            "install": {
                "at_reticle": bool(row.get("bInstallAtReticle")),
                "neighbor_threshold": row.get("InstallNeighborThreshold"),
                "only_on_base": bool(row.get("bIsInstallOnlyOnBase")),
                "only_in_door": bool(row.get("bIsInstallOnlyInDoor")),
                "max_in_base_camp": row.get("InstallMaxNumInBaseCamp"),
                "no_obstacle_from_camera": bool(row.get("bInstallableNoObstacleFromCamera")),
            },
            "bp_class": mrow.get("BlueprintClassName"),
            "bp_path": mrow.get("BlueprintClassSoft"),
            "size_cm": old_entry.get("size_cm"),
            "size_source": old_entry.get("size_source"),
            "verified_live": bool(old_entry.get("verified_live")),
            "source": "DT_BuildObjectDataTable+DT_MapObjectMasterDataTable",
        }
        entries[bid] = entry

    # icons exported by tools/PalAssetExport exporticons (optional)
    icons = {}
    icon_index = os.path.join(HERE, "icons", "_icons.json")
    if os.path.exists(icon_index):
        try:
            icons = json.load(open(icon_index, encoding="utf-8"))
        except Exception:                          # noqa: BLE001
            icons = {}
    for bid, entry in entries.items():
        icon = icons.get(bid)
        if icon:
            entry["icon"] = "icons/" + icon

    # aliases where the build row name differs from the blueprint class short name
    aliases = {}
    for bid, e in entries.items():
        cls = (e.get("bp_class") or "").replace("BP_BuildObject_", "")
        if cls and cls.lower() != bid.lower():
            aliases[bid] = cls

    counts_by_a = {}
    counts_by_ui = {}
    for e in entries.values():
        a = e["category"]["type_a"] or "?"
        u = e["category"]["type_ui"] or "?"
        counts_by_a[a] = counts_by_a.get(a, 0) + 1
        counts_by_ui[u] = counts_by_ui.get(u, 0) + 1

    version = version_stamp()
    catalog = {
        "schema": 2,
        "version": version,
        "counts": {
            "build_objects": len(entries),
            "with_size": sum(1 for e in entries.values() if e.get("size_cm")),
            "with_materials": sum(1 for e in entries.values() if e.get("materials")),
            "verified_live": sum(1 for e in entries.values() if e.get("verified_live")),
            "by_type_a": dict(sorted(counts_by_a.items())),
            "by_type_ui": dict(sorted(counts_by_ui.items())),
        },
        "aliases": aliases,
        "entries": entries,
    }
    json.dump(catalog, open(CATALOG, "w", encoding="utf-8"), ensure_ascii=False, indent=1, sort_keys=True)
    ids = {
        "version": version,
        "count": len(entries),
        "ids": sorted(entries),
        "aliases": aliases,
        "verified_live": sorted(k for k, e in entries.items() if e.get("verified_live")),
        "note": "id = DT_BuildObjectDataTable RowName; the mod's FindRow is case-insensitive",
    }
    json.dump(ids, open(BUILD_IDS, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    lines = ["# Build catalogue summary", "",
             "<!-- Generated by build_catalog.py from data/raw/. Do not edit by hand: re-run the generator. -->", "",
             "- game buildid: `%s` (last updated %s)" % (version.get("steam_buildid"), version.get("last_updated")),
             "- mod: %s v%s" % (version["mod"]["name"], version["mod"]["version"]),
             "- placeables: **%d**, with measured size: %d, with materials: %d, live-probed: %d"
             % (catalog["counts"]["build_objects"], catalog["counts"]["with_size"],
                catalog["counts"]["with_materials"], catalog["counts"]["verified_live"]), "",
             "## by TypeA", ""]
    lines += ["- %s: %d" % (k, v) for k, v in sorted(counts_by_a.items(), key=lambda x: -x[1])]
    lines += ["", "## by TypeUIDisplay (material/style family)", ""]
    lines += ["- %s: %d" % (k, v) for k, v in sorted(counts_by_ui.items(), key=lambda x: -x[1])]
    open(SUMMARY, "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print(json.dumps(catalog["counts"], ensure_ascii=False, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())


