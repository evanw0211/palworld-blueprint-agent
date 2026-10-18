#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Build contact sheets from the exported build-object icons.

The model cannot "see" an id — it can look at a PNG. This script groups the official
512x512 icons by TypeA category and writes readable sheets under data/sheets/.

  python blueprints/data/make_icon_sheets.py
"""
import json
import os

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ICONS = os.path.join(HERE, "icons")
SHEETS = os.path.join(HERE, "sheets")
CATALOG = os.path.join(HERE, "catalog.json")
COLS = 8
TILE = 128
PAD = 6
LABEL_H = 26


def font(size):
    for path in ("C:/Windows/Fonts/msyh.ttc", "C:/Windows/Fonts/simhei.ttf"):
        try:
            return ImageFont.truetype(path, size)
        except Exception:
            continue
    return ImageFont.load_default()


def main():
    os.makedirs(SHEETS, exist_ok=True)
    mapping = {}
    try:
        mapping = json.load(open(os.path.join(ICONS, "_icons.json"), encoding="utf-8"))
    except Exception:                                  # noqa: BLE001
        raise SystemExit("run `exporticons` first (data/icons/_icons.json missing)")
    catalog = json.load(open(CATALOG, encoding="utf-8"))
    entries = catalog.get("entries", {})
    groups = {}
    for bid, file in mapping.items():
        entry = entries.get(bid) or {}
        cat = entry.get("category", {}) or {}
        group = cat.get("type_a_label") or cat.get("type_a") or "Other"
        name = (entry.get("name") or {}).get("zh") or bid
        groups.setdefault(group, []).append((bid, name, os.path.join(ICONS, file)))

    f_label = font(13)
    f_title = font(24)
    index = {}
    for group, items in sorted(groups.items(), key=lambda kv: -len(kv[1])):
        items.sort(key=lambda x: x[1])
        rows = (len(items) + COLS - 1) // COLS
        W = COLS * (TILE + PAD) + PAD
        H = rows * (TILE + LABEL_H + PAD) + PAD + 40
        img = Image.new("RGB", (W, H), (26, 27, 30))
        d = ImageDraw.Draw(img)
        d.text((PAD, 8), "%s  (%d)" % (group, len(items)), font=f_title, fill=(240, 236, 228))
        for i, (bid, name, path) in enumerate(items):
            r, c = divmod(i, COLS)
            x = PAD + c * (TILE + PAD)
            y = 40 + PAD + r * (TILE + LABEL_H + PAD)
            try:
                icon = Image.open(path).convert("RGBA").resize((TILE, TILE), Image.LANCZOS)
                img.paste(icon, (x, y), icon)
            except Exception:                          # noqa: BLE001
                d.rectangle([x, y, x + TILE, y + TILE], outline=(80, 80, 80))
            label = name if len(name) <= 9 else name[:8] + "…"
            d.text((x + 2, y + TILE + 3), label, font=f_label, fill=(210, 205, 195))
        safe = "".join(ch if ch.isalnum() or ch in "-_" else "_" for ch in group)
        out = os.path.join(SHEETS, "sheet_%s.png" % safe)
        img.save(out)
        index[group] = {"file": os.path.relpath(out, SHEETS), "count": len(items)}
        print("%-14s %3d icons -> %s" % (group, len(items), out))
    json.dump({"groups": index, "tile_px": TILE, "cols": COLS},
              open(os.path.join(SHEETS, "_index.json"), "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)
    print("sheets:", len(index), "icons:", sum(v["count"] for v in index.values()))


if __name__ == "__main__":
    main()
