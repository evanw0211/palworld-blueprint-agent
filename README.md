---
description: "离线校验并导出可导入 Simple Building Blueprints 的帕鲁蓝图：先用两条命令确认工具链可用，再按 data/format.md 写 authoring JSON。"
kind: overview
---

# 帕鲁蓝图 Agent 工作区

## 摘要

本工作区让你在不启动游戏的情况下，把一份 authoring JSON 校验并导出为可导入的帕鲁蓝图（schema-9 JSON 与 PWB9D 码）。数据、校验、导出全部离线完成；布局与风格由你决定，工具只给客观结果。最短路径是两步命令：先校验现成样本，再导出 `authoring/` 里的示例，看到 `out/` 下出现 `.json` 与 `.pwb9d.txt` 即说明链路可用。边界：这里不操作游戏进程，蓝图要由你在游戏内导入并放置。

## 目录

- [能做什么](#能做什么)
- [首次结果](#首次结果)
- [边界](#边界)
- [下一步去哪](#下一步去哪)
- [Further Exploration](#further-exploration)
- [Dev Note](#dev-note)

-----

## 能做什么

本工作区把「选件 → 写 authoring JSON → 校验 → 导出」固化成可离线重复的工具链；每条能力下方的页面是它的契约 owner。

- 从零产出一份可导入蓝图：照工作流写 authoring JSON、导出、复核 - [AGENT.md](AGENT.md)
- 查命令行怎么用：导出、校验与预览脚本的参数和 authoring 字段 - [tooling/README.md](tooling/README.md)
- 查文件格式规则：schema-9 字段、PWB9D 编码、网格与连接、BuildObjectId - [data/format.md](data/format.md)
- 浏览数据资产：可放置物目录、官方图标、分类拼图、原始表转储 - [data/README.md](data/README.md)
- 游戏更新后刷新数据：重新生成 catalog，或判断当前快照是否过期 - [data/refresh.md](data/refresh.md)

## 首次结果

两条命令都在仓库根目录执行，用来确认工具链可用。下面只摘录判定结果；工具打印的完整报告字段较多，本页不复述。

1. 校验一个已交付样本：

   ```powershell
   python tooling/validate.py examples/hut_wood_2x2.json
   ```

   ```text
   PASS hut_wood_2x2.json
   ```

   第一行是判定行；其后还有一行校验统计。

2. 从 authoring 示例导出一份蓝图：

   ```powershell
   python tooling/export.py authoring/demo_2x2_east_door.json -o out
   ```

   ```text
   {
    "name": "demo_2x2_east_door",
    ...
    "ok": true,
    "problems": [],
    "warnings": [],
    ...
    "json": "out\\demo_2x2_east_door.json",
    "code": "out\\demo_2x2_east_door.pwb9d.txt"
   }
   ```

   上面的 `...` 是省略的中间字段；这次运行输出了 `ok: true`，且 `problems` 与 `warnings` 都为空。

3. 打开 `out/`：报告末尾写出的两个文件就在那里。`demo_2x2_east_door.json` 是 schema-9 蓝图，`demo_2x2_east_door.pwb9d.txt` 是可粘贴的 PWB9D 码。

## 边界

本仓库只做离线数据、校验与导出：不操作游戏进程，不建造、不注入、不调用游戏引擎渲染。蓝图必须在游戏内导入并放置，离线预览也不能代替游戏内观感。

## 下一步去哪

| 读者目标 | Owner 页面 |
|---|---|
| 开始产出第一份蓝图（工作流与红线） | [AGENT.md](AGENT.md) |
| 查格式契约（schema-9 / PWB9D / 网格 / 连接 / ID） | [data/format.md](data/format.md) |
| 查导出/校验/预览脚本的命令行用法与 authoring 字段 | [tooling/README.md](tooling/README.md) |
| 浏览 data/ 每个子项做什么 | [data/README.md](data/README.md) |
| 刷新数据或判断快照是否过期 | [data/refresh.md](data/refresh.md) |
| 用社区/参考样例做设计对照 | [examples/README.md](examples/README.md) |
| 为什么不用引擎内渲染 | [docs/decisions/0001-offline-only-rendering.md](docs/decisions/0001-offline-only-rendering.md) |

## Further Exploration

- [data/catalog_summary.md](data/catalog_summary.md) - 生成的可放置物统计（按分类与风格），选件时先扫一眼分布。
- [data/sheets/_index.json](data/sheets/_index.json) - 分类拼图索引，决定该打开哪张 `sheet_*.png`。
- [examples/README.md](examples/README.md) - 已交付样本与社区参考样例（json + pwb9d），可直接校验或对照重导。
- [render/README.md](render/README.md#保真度与对照证据) - `render/compare/` 的对照图：离线渲染与引擎内渲染几何投影一致、材质与光照不同的证据。

## Dev Note

None.
