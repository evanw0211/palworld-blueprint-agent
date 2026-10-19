---
description: "决策：catalog、id 清单、统计摘要、图标与拼图由生成器拥有——改内容或加标记都必须先改生成器，且不得为验证而重跑生成器。"
kind: "decision-record"
---

# 生成文件由生成器拥有

- **状态：** accepted
- **日期：** 2026-10-19
- **取代：** None
- **被取代：** None

`data/` 下的目录、id 清单、统计摘要、图标与拼图都是产物，不是手写文档。任何改动（包括加一行说明标记）先改生成器、再让产物与生成器一致；生成器本身不得为了「验证文档」而重跑，因为它覆盖的是版本锁定数据。

## 背景

- `data/catalog_summary.md` 曾被当成可手写文档：手改会在下次生成时丢失，而它同时又是计数的事实来源。
- 数据快照按游戏版本锁定（`catalog.json.version`：Steam buildid + mod 版本）；重跑生成器需要本机 Steam 与工坊路径存在，并会重写版本戳与全部条目。
- 2026-10-19 给它加「勿手改」说明时没有重跑生成器：改为同步修改生成器模板与产物，并在沙箱副本里重跑生成器证明两者逐字节一致。

## 决策

1. 产物与生成器的对应关系：

| 产物 | 生成器 | 输入 |
|---|---|---|
| `data/catalog.json`、`data/build_ids.json`、`data/catalog_summary.md` | [data/build_catalog.py](../../data/build_catalog.py) | `data/raw/` 的官方表转储 + `data/icons/_icons.json` |
| `data/sheets/*.png`、`data/sheets/_index.json` | [data/make_icon_sheets.py](../../data/make_icon_sheets.py) | `data/icons/` + `data/catalog.json` |
| `data/icons/*.png`、`data/icons/_icons.json` | `tooling/PalAssetExport exporticons` | 游戏 pak |

2. 改产物内容或说明标记 → 先改生成器，再同步产物；验证在沙箱副本里跑生成器，不在仓库里跑。
3. 禁止为验证文档而运行会写 `data/` 的命令；刷新只在游戏或 mod 更新后按 [data/refresh.md](../../data/refresh.md) 执行。
4. 文档引用产物内容时注明「以生成文件为准」；产物本身不写摘要与目录，但必须带生成说明。

## 被否方案

| 方案 | 否掉原因 |
|---|---|
| 只改产物、不改生成器 | 下次刷新即丢失，且产物与生成器从此不一致 |
| 为加标记重跑 `build_catalog.py` | 会重写版本戳与全部条目、依赖 Steam 与工坊路径，风险大于收益 |
| 把产物改写成手写文档（补摘要、目录、owner 段落） | 生成器每次都会覆盖，等于给读者一个必然漂移的事实源 |

## 后果

### 正面

- 刷新后标记与内容都不会丢；沙箱重跑能证明产物与生成器逐字节一致。
- 版本锁定数据不会被「顺手验证」意外改写。

### 负面

- 产物的任何小改都要动 Python 或 C#，生成器成为产物的单点。

### 后续

- 新增生成产物时，同一轮把生成说明写进生成器模板。

## 合规

不变量：被标注为生成的文件的任何变更都必须伴随生成器变更；不得在仓库内直接运行会覆盖版本锁定数据的命令。执行者：评审 + [data/refresh.md](../../data/refresh.md) 的证据边界。

## Dev Note

None.
