---
description: "在离线工作区按 5 步产出可导入的帕鲁蓝图：选件、写 authoring JSON、导出 schema-9 与 PWB9D、复核后交付。"
kind: "how-to"
---

# 如何产出一份可导入的游戏蓝图

## 摘要

本页交付可直接粘贴进 mod 的蓝图：`out/<名字>.pwb9d.txt` 与同名 schema-9 JSON。设计（布局、尺寸、风格、美观）由你决定，工具只负责数据、校验、导出；本页只给工作流、验收与红线；格式常量、字段契约与目录清单归各自的 owner 页。硬约束：全程离线、`id` 只取自 catalog、校验不过不交付。

## 目录

- [开工前](#开工前)
- [工作流（5步）](#工作流5步)
- [验收](#验收)
- [失败与恢复](#失败与恢复)
- [红线](#红线)
- [相关任务](#相关任务)
- [Further Exploration](#further-exploration)
- [Dev Note](#dev-note)

-----

## 开工前

在仓库根目录工作，`python`、`node` 可用，不需启动游戏。先读这些 owner 页：

- schema-9 字段、PWB9D 编码、网格/层高/朝向常量、连接属性、BuildObjectId → [data/format.md](data/format.md)
- authoring 字段契约、CLI 用法与退出码 → [tooling/README.md](tooling/README.md)
- `data/` 目录清单 → [data/README.md](data/README.md)
- 当前数据版本与件数 → [data/catalog_summary.md](data/catalog_summary.md)

## 工作流（5步）

1. **选件**：在 `data/catalog.json` 查 id；看外观打开 `data/sheets/` 的分类拼图，分组见 [data/sheets/_index.json](data/sheets/_index.json)。
2. **写 authoring JSON**，放 `authoring/` 或 `out/`；最小示例仅示意形状，字段契约见 [tooling/README.md](tooling/README.md)：

```json
{"name": "my_hut", "anchor": 0, "pieces": [
  {"id": "Wooden_foundation", "x": 0, "y": 0, "z": 0, "yaw": 0},
  {"id": "Wooden_wall", "x": -200, "y": 0, "z": 0, "yaw": 0},
  {"id": "Wooden_roof", "x": 0, "y": 0, "z": 325, "yaw": 0}
]}
```

3. **导出**：自动归一化 anchor、生成类型表与连接图并校验，写出 `out/<名字>.json` 和 `out/<名字>.pwb9d.txt`；校验不过不写。

```powershell
python tooling/export.py authoring/<名字>.json -o out
```

4. **复核**：

```powershell
python tooling/validate.py out/<名字>.json --json
```

5. **交付** `out/<名字>.pwb9d.txt`；可选预览：

```powershell
node tooling/render_offline.mjs out/<名字>.json out/<名字>.png
```

预览需要 127.0.0.1:9222 上已有 Chrome 调试端点；本页作者环境没有，未复现，只观察到 `connect ECONNREFUSED 127.0.0.1:9222`。验证责任人：本机装有 Chrome 的工作区维护者。

## 验收

用 `demo_2x2_east_door` 实跑的输出（省略处已标注）：

```text
$ python tooling/export.py authoring/demo_2x2_east_door.json -o out
{
 "name": "demo_2x2_east_door",
  …（pieces/types/edges 省略）
 "ok": true,
 "problems": [],
 "warnings": [],
 "stats": {
  …（stats 与文件路径省略）

$ python tooling/validate.py out/demo_2x2_east_door.json --json
{
  …（file 省略）
 "ok": true,
 "problems": [],
 "stats": {
  …（stats 省略）

$ python tooling/validate.py examples/hut_wood_3x3.pwb9d.txt
PASS hut_wood_3x3.pwb9d.txt
```

成功 = `"ok": true` 且 `"problems": []`，或 `PASS <文件名>`；失败 = `"ok": false` 带 `problems`/`error`，不要交付。

## 失败与恢复

| 症状 | 原因 | 恢复 |
|---|---|---|
| `not a schema-9 blueprint (authoring file? run export.py first)` | 直接校验了 authoring 输入 | 先导出，只校验 `out/<名字>.json` |
| `FAIL <文件名>` + `x … off the 400 grid`、`x … unreachable from anchor` | 网格/层高/连通性不通过 | 按提示的件号改 authoring，重跑导出 |
| `fetch failed … ECONNREFUSED 127.0.0.1:9222` | 预览缺 Chrome 调试端点 | 按 [tooling/README.md](tooling/README.md) 起端点；不影响交付 |

## 红线

1. 不操作游戏：不建造、不注入、不调用引擎渲染，全离线。这条边界的由来与被否方案见 [docs/decisions/0001-offline-only-rendering.md](docs/decisions/0001-offline-only-rendering.md)。
2. 不编造 id，只用 `data/catalog.json` 里的。
3. 不跳过校验；`--allow-problems` 仅供调试。
4. 离线预览是真网格加类型色，不代表游戏内观感。
5. 产出文件统一 UTF-8；PowerShell 乱码不影响文件内容。

## 相关任务

- [data/refresh.md](data/refresh.md) - 游戏更新后刷新 catalog 与图标；本页不覆盖。

## Further Exploration

- [tooling/README.md](tooling/README.md) - authoring 字段与 CLI 用法。
- [examples/README.md](examples/README.md) - 可校验的样本清单，写不出形状时先对照重导。
- [render/README.md](render/README.md) - 离线缩略图渲染页面、保真度证据与限制。
- [data/format.md](data/format.md) - schema-9、PWB9D、网格与连接规则。
- [data/catalog_summary.md](data/catalog_summary.md) - 快照版本与分类件数。
- [archive/research/agent_blueprint_requirements.md](archive/research/agent_blueprint_requirements.md) - 选型与布局的 8 维审美自查清单（研究记录，非验收标准）。
- [README.md](README.md) - 项目定位与首次结果。

## Dev Note

None.
