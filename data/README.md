---
description: "data/ 目录索引：说明格式规范、catalog、图标与拼图、原始表转储、生成脚本各做什么，并标出哪些文件由生成器产出、不能手改。"
kind: index
---

# data/ - 蓝图数据资产索引

## 摘要

`data/` 存放产出蓝图所需的全部离线数据资产：格式规范、可放置物目录、官方图标与分类拼图、原始数据表转储，以及生成这些产物的脚本。读者的通常顺序是先读 `format.md` 拿规则，再用 catalog 与图标选件，游戏更新后才需要刷新。本页只做子项导航与生成关系说明；格式规则、刷新步骤、catalog 字段分别由对应子项拥有，本页不复述。

## 目录

- [子项](#子项)
- [各部分如何配合](#各部分如何配合)
- [Further Exploration](#further-exploration)
- [Dev Note](#dev-note)

-----

## 子项

下表每个直接子项一行；「细节归属」列写该子项自己拥有的内容，以及它是否由生成器产出。

| 子项 | 用途 | 细节归属 |
|---|---|---|
| [format.md](format.md) | 拿格式规则：schema-9 字段、PWB9D 编码、网格与高度、连接属性、BuildObjectId | 这些规则的唯一手写 owner；手写文档，可编辑 |
| [catalog.json](catalog.json) | 按 id、中英文名、分类、材料、蓝图类选件 | 版本锁定的目录数据；生成文件，由 `build_catalog.py` 产出，勿手改 |
| [build_ids.json](build_ids.json) | 查 id 清单与 id → BP 类别名，给脚本查表用 | 生成文件，由 `build_catalog.py` 产出，勿手改 |
| [catalog_summary.md](catalog_summary.md) | 扫可放置物统计：总量与按分类、按风格族的分布 | 生成文件，由 `build_catalog.py` 产出，勿手改 |
| [icons/](icons/) | 看单件图标选件，文件名对应 `_icons.json` 里的 id → 文件映射 | 图标与 `_icons.json` 由上游 `exporticons` 产出，勿手改 |
| [sheets/](sheets/) | 看按分类分组的图标拼图，一次浏览一类 | 拼图由 `make_icon_sheets.py` 生成；索引 `sheets/_index.json` 是生成文件，勿手改 |
| [raw/](raw/) | 查看官方数据表转储（建造物、地图物、中英文文本、图标表） | 生成脚本的输入；由上游 `dumptable` 导出，勿手改 |
| [build_catalog.py](build_catalog.py) | 从 `raw/` 生成 `catalog.json`、`build_ids.json`、`catalog_summary.md` | 生成器脚本，改动它才改得动产物 |
| [make_icon_sheets.py](make_icon_sheets.py) | 从 `icons/` 与 `catalog.json` 生成 `sheets/` 拼图与索引 | 生成器脚本，改动它才改得动产物 |
| [refresh.md](refresh.md) | 游戏更新后刷新数据：前置、步骤、产物覆盖范围、过期判断 | 刷新流程的唯一 owner；手写文档，可编辑 |

## 各部分如何配合

生成链是单向的，先有输入再跑生成器：

1. `raw/` 里的官方表转储是唯一数据源；`build_catalog.py` 读它，并参考 `icons/_icons.json`。
2. `build_catalog.py` 写出 `catalog.json`、`build_ids.json`、`catalog_summary.md`。
3. `make_icon_sheets.py` 读 `icons/` 与生成好的 `catalog.json`，写出 `sheets/` 下的拼图与索引。

所以上表里标注为生成文件的产物都不要手改：手改会在下次生成时丢失，游戏更新后按 [refresh.md](refresh.md) 重跑生成器才是持久改法。上表里 `format.md` 与 `refresh.md` 是手写文档，其余子项是生成器、输入或产物；`catalog.json` 与 `sheets/` 之间隔着一次生成，先跑 `build_catalog.py`，拼图才会有最新的分类归属。

## Further Exploration

- [../AGENT.md](../AGENT.md) - 这些数据的使用者：从选件到交付蓝图的完整工作流。
- [../tooling/README.md](../tooling/README.md) - 读 data/ 的脚本：校验、导出与离线预览怎么用。
- [../README.md](../README.md) - 回到工作区总览与最短验证路径。

## Dev Note

None.
