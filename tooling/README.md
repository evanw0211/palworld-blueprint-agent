---
description: "tooling/ 三个脚本的命令与退出码：导出 authoring、离线校验、可选预览。"
kind: "component-reference"
---

# tooling/ — 导出、校验、离线预览

## 摘要

把 authoring JSON 变成 `schema-9 JSON + PWB9D 文本码`，用 `validate.py` 离线复核；`render_offline.mjs` 可选出缩略图。适合导出后立刻复核，也适合交付前当门禁。边界：不判设计好坏，也不验证游戏内能否放置。

## 目录

- [怎么用](#怎么用)
- [运行](#运行)
- [失败与恢复](#失败与恢复)
- [边界与安全](#边界与安全)
- [了解实现](#了解实现)
- [限制](#限制)
- [Dev Note](#dev-note)

-----

## 怎么用

~~~powershell
python tooling/export.py authoring/demo_2x2_east_door.json -o out
python tooling/validate.py examples/hut_wood_2x2.json
~~~

`export.py [-o <dir>] [--name NAME] [--check-only] [--allow-problems]`：`-o` 默认仓库根并写 `<name>.json`/`.pwb9d.txt`，`--name` 覆盖输出名。`validate.py` 吃两者，另有 `--json`/`--quiet`。

authoring 输入是 `export.py` 的唯一输入格式，字段语义以 `build()` 为准：

| 字段 | 必填 | 语义 |
|---|---|---|
| `name` | 否 | 输出名；缺省取 `--name`，再取输入文件名去扩展名 |
| `anchor` | 否 | `pieces` 下标（默认 `0`），该件作为局部帧原点；越界即报错退出 |
| `pieces` | 是 | 非空数组，每件一个对象；为空即报错退出 |
| `pieces[].id` | 是 | BuildObjectId，须来自 `data/catalog.json`；未知 id 只警告 |
| `pieces[].x` `y` `z` | 否 | 厘米（默认 `0`），任意公共原点与朝向，导出时自动移除 anchor 帧 |
| `pieces[].yaw` | 否 | 度（默认 `0`），UE yaw：`0`=+X、`90`=+Y |

本目录还有两个不属于上面三个脚本的组件，契约归各自页面：数据刷新工具（dotnet CLI）见 [PalAssetExport/README.md](PalAssetExport/README.md)，本页不写它的命令；离线渲染页面与保真度对照图见 [../render/README.md](../render/README.md)。

## 运行

重复运行覆盖同名输出。预览要先起 Chrome 调试端口（本机 9222 无监听，未复现，lead）；Chrome 默认装在 Program Files：

~~~powershell
& "$env:ProgramFiles\Google\Chrome\Application\chrome.exe" --remote-debugging-port=9222 --user-data-dir="$env:TEMP\dsh-chrome-profile" --no-first-run
node tooling/render_offline.mjs out/demo_2x2_east_door.json out/demo.png
~~~

## 失败与恢复

成败标记：`PASS <文件名>` / `"ok": true`，或 `FAIL` / `"ok": false` 加 `problems`。退出码：

| 脚本 | 0 | 1 | 2 |
|---|---|---|---|
| `export.py` | 成功写出 `.json` + `.pwb9d.txt`（`--check-only` 只报告、不写文件也退 0） | 未写出：有 `problems`、authoring 非法（`pieces` 空 / `anchor` 越界 / 缺 `id`）、输入读不了 | 参数错误 |
| `validate.py` | PASS | FAIL、无参数、`.txt` 非 PWB9D 码、帧长度/CRC 不符（`PWB9D frame mismatch`） | JSON 解析失败、帧解压失败、不是 schema-9 |
| `render_offline.mjs` | 截图完成 | 未完成 | 用法错误 |

`--allow-problems` 是「有问题也写并退 0」的唯一例外。

| 现象 | 原因 | 处理 |
|---|---|---|
| `FileNotFoundError: ...` 退 1 | 路径不存在 | 改用存在的路径 |
| `not a schema-9 blueprint ...` 退 2 | authoring 直接喂 `validate.py` | 先导出再校验 |
| `foundation off the 400 grid` 退 1 | 网格超差 | 按件号改 authoring |

## 边界与安全

- 不接触游戏，只读输入、只写输出目录。

## 了解实现

<details>
<summary>实现内部 — 点击展开</summary>

源文件：[`export.py`](export.py)、[`validate.py`](validate.py)、[`render_offline.mjs`](render_offline.mjs)。

</details>

## 限制

- `render_offline.mjs` 只在已有 Chrome 调试端点的机器上可用；本机无端点，未复现。
- `validate.py` 只吃 schema-9 产出，不读 authoring 输入。

-----

## Dev Note

<details>
<summary>维护者工作记录 — 点击展开</summary>

退出码按实跑改写：文件不存在时 `export.py` 抛 `FileNotFoundError`（退 1）；“2 参数/文件错误”仅 argparse。体量：authoring 契约与退出码按所有权只能住本页，已合并重复句，预算上调为 4 KB。

</details>
