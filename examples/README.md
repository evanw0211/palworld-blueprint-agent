---
description: "examples/ 索引：5 个已交付样本与 3 个社区参考蓝图的件数、材料与校验结果，以及每个文件是什么、能拿来做什么。"
kind: index
---

# examples/ - 可校验的蓝图样本

## 摘要

`examples/` 放能直接跑校验的蓝图样本：5 个本仓库交付的样本，和 3 个从 Reddit 社区收来的参考样例。每个样本同时提供 schema-9 JSON 与同名 PWB9D 文本码，`tooling/validate.py` 两种格式都能校验；下表件数与材料取自本页作者的实跑 stats。你可以用这些样本确认工具链可用、对照重导，或拿社区样例作设计与渲染对照。边界：社区样例来自 mod 导出，只作参考，既不代表本仓库的设计规范，也不保证在不同 mod 版本下还能通过校验。

## 目录

- [子项](#子项)
- [各部分如何配合](#各部分如何配合)
- [Further Exploration](#further-exploration)
- [Dev Note](#dev-note)

-----

## 子项

每个样本一行；链接指向可读的 `.json`，同目录下有同名 `.pwb9d.txt`。两种格式在本修订实跑均 PASS。

### 已交付样本

| 样本 | 用途 | 实跑 stats（`validate.py --json`） |
|---|---|---|
| [hut_wood_2x2.json](hut_wood_2x2.json) | 最小全木屋基线，对照重导首选 | PASS；16 件 / 5 类 / 19 连接；材料 Wood ×32 |
| [hut_wood_3x3.json](hut_wood_3x3.json) | 3×3 木屋，比 2×2 多一层地基与屋顶面积 | PASS；30 件 / 5 类 / 33 连接；材料 Wood ×60 |
| [hut_japanese_2x2.json](hut_japanese_2x2.json) | 日式 2×2，换材质族 | PASS；16 件 / 5 类 / 19 连接；材料 Wood_Fine ×48 |
| [hut_japanese_3x3.json](hut_japanese_3x3.json) | 日式 3×3 | PASS；30 件 / 5 类 / 33 连接；材料 Wood_Fine ×84 |
| [demo_2x2_east_door.json](demo_2x2_east_door.json) | 东向开门的 2×2 演示，README 的首次结果用它 | PASS；16 件 / 5 类 / 20 连接；材料 Wood ×32 |

### 社区参考样例

来自 Reddit r/PalworldSBB 的用户作品，元数据见 [community/summary.json](community/summary.json)。三条在本修订实跑全部 PASS（JSON 与 `.pwb9d.txt` 各一遍）；但社区码是别人用 mod 导出的，网格/连通假设与版本一旦变化就可能 FAIL，**不保证可过校验，也不要当成品交付**。

| 样例 | 来源与看点 | 实跑 stats |
|---|---|---|
| [reddit_basic_base.json](community/reddit_basic_base.json) | Basic Base（u/MadsDK21）：日式围院 + 中央泳池 + 石柱围墙，学布局与材料混搭 | PASS；313 件 / 13 类 / 1466 连接；材料 Wood_Fine×678、Stone×247、Pal_crystal_S×32 |
| [reddit_four_base_complex.json](community/reddit_four_base_complex.json) | Four-Base Complex（u/leafnwind）：1656 件的超大平台群，看规模上限 | PASS；1656 件 / 49 类 / 8836 连接；stats 另报 71 组同位重复，校验本身不判失败 |
| [reddit_grovers_first_house.json](community/reddit_grovers_first_house.json) | Grover's First House（u/ThePunishersHarp）：262 件两层日式住宅，anchor 是第 261 件 `Stone_Gate`；也是 render/compare 的对照对象 | PASS；262 件 / 55 类 / 874 连接；整包有 -0.21° 旋帧偏差、地基 z 偏移 -1.74 cm |

### 其他文件

- [community/summary.json](community/summary.json) - 社区样例的来源与规模摘要（标签、件数、尺寸、Top 类型）；随样例从原工作区带过来，件数与实跑一致。
- [community/reddit_grovers_first_house.png](community/reddit_grovers_first_house.png) - Reddit 原帖整页截图（1264×805），记录作者、标签与帖子正文。
- [community/reddit_grovers_first_house_render.png](community/reddit_grovers_first_house_render.png) - 帖子内嵌的 320×180 缩略图（mod 渲染、真材质），用来和离线真网格渲染对照，见 [render/README.md](../render/README.md)。
- 每个样本的 `.pwb9d.txt` - 可粘贴进 mod 的文本码，与 `.json` 是同一份蓝图的两个形态。

## 各部分如何配合

同一个名字的 `.json` 与 `.pwb9d.txt` 是同一份蓝图的两个形态：JSON 是 schema-9 权威数据，PWB9D 是压缩并带 CRC 的文本码；校验两者结果一致（本页全部实跑）。交付用 `.pwb9d.txt`，复核与重导用 `.json`：先把样本喂给 `validate.py` 确认链路，再按 [AGENT.md](../AGENT.md) 的 5 步工作流写自己的 authoring JSON。社区样例和渲染图只用于设计参考与对照，不参与工具链生成；`summary.json` 是元数据，不是校验输入。要给样本重出缩略图，走 [render/README.md](../render/README.md) 的离线渲染页。

## Further Exploration

- [../AGENT.md](../AGENT.md) - 用这些样本跑通从选件到交付的工作流。
- [../tooling/README.md](../tooling/README.md) - `validate.py` / `export.py` 的命令与退出码。
- [../data/format.md](../data/format.md) - 看懂样本里的 schema-9 字段与 PWB9D 编码。
- [../render/README.md](../render/README.md) - 把样本渲染成缩略图，以及和引擎内渲染的对照证据。
- [../archive/research/agent_blueprint_requirements.md](../archive/research/agent_blueprint_requirements.md) - 社区样本背后的「可用/美观」约束研究（冻结历史）。

## Dev Note

5 个交付样本与 3 个社区样例的 stats 都来自本页作者实跑（`--json` 一把，`.pwb9d.txt` 再一把）。社区样例在本修订全部 PASS；后续若网格或版本规则变化导致 FAIL，以 `validate.py` 实跑为准并回填上表。
