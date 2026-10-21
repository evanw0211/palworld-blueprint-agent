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

重复运行覆盖同名输出。预览要先起 Chrome 调试端口（默认 `127.0.0.1:9222`，可用环境变量 `RENDER_CDP_PORT` 覆盖；`RENDER_CDP_TIMEOUT_MS`/`RENDER_PAGE_TIMEOUT_MS` 可调整单次 CDP 请求与整页等待的超时，默认 10 s / 90 s）。Chrome 默认装在 Program Files：

~~~powershell
& "$env:ProgramFiles\Google\Chrome\Application\chrome.exe" --remote-debugging-port=9222 --user-data-dir="$env:TEMP\dsh-chrome-profile" --no-first-run --headless=new about:blank
node tooling/render_offline.mjs out/demo_2x2_east_door.json out/demo.png
~~~

每次运行用 `PUT /json/new` 自建一个后台标签页并在结束后关闭；**不会 navigate 你已经在用的标签页**。

`render_offline.mjs <bp.json> <out.png> [w] [h]` 之后还可以跟两类**可选**参数，都不影响原有调用：

| 参数 | 作用 |
|---|---|
| `--set k=v`（可重复） | 透传查询参数给渲染页面：`mat=0` 回到旧类型色（回归用）、`az`/`el`/`dist`/`fit` 换机位、`top=1` 俯视、`ortho=1` 正交。键必须非空、值不能为空；`az`/`el`/`dist`/`fit`/`w`/`h` 必须是数字 |
| `--views N` 或 `--views "az,el;az,el"` | 一次出**多视角检查图**：N 个等分方位角（2–16），或指定机位（`el` 在 −90..90）；输出一张网格拼图，每格独立取景并带 az/el 标注 |

参数不合法（`--views abc`、`--views 1`、`--set dist=abc`、`--set dist` 缺 `=`、`--set` 缺值、未知 `--flag`、位置参数非法）属于**用法错误**：脚本打印 `usage:`、**exit 2、不启动 Chrome、不写 PNG**。

示例：

~~~powershell
node tooling/render_offline.mjs out/kotori_keep.json out/kotori_keep_views.png 1800 1200 --views 6
node tooling/render_offline.mjs examples/hut_wood_3x3.json out/legacy.png --set mat=0
~~~

页面接受的完整参数表、材质判定规则与 `status` 字段由 [../render/README.md](../render/README.md) 拥有，本页只写命令契约。

## 失败与恢复

成败标记：`PASS <文件名>` / `"ok": true`，或 `FAIL` / `"ok": false` 加 `problems`。退出码：

| 脚本 | 0 | 1 | 2 |
|---|---|---|---|
| `export.py` | 成功写出 `.json` + `.pwb9d.txt`（`--check-only` 只报告、不写文件也退 0） | 未写出：有 `problems`、authoring 非法（`pieces` 空 / `anchor` 越界 / 缺 `id`）、输入读不了 | 参数错误 |
| `validate.py` | PASS | FAIL、无参数、`.txt` 非 PWB9D 码、帧长度/CRC 不符（`PWB9D frame mismatch`） | JSON 解析失败、帧解压失败、不是 schema-9 |
| `render_offline.mjs` | 截图完成并写出 PNG | 未完成：Chrome 端点不可用、页面内致命错误（`status.error`）、超时、CDP 断连；不写成功 JSON、不写 PNG（页面超时除外，会写出可能不完整的 PNG 供排查） | 用法错误：`--views`/`--set` 非法、位置参数非法；不启动 Chrome、不写 PNG |

`--allow-problems` 是「有问题也写并退 0」的唯一例外。

| 现象 | 原因 | 处理 |
|---|---|---|
| `FileNotFoundError: ...` 退 1 | 路径不存在 | 改用存在的路径 |
| `not a schema-9 blueprint ...` 退 2 | authoring 直接喂 `validate.py` | 先导出再校验 |
| `foundation off the 400 grid` 退 1 | 网格超差 | 按件号改 authoring |
| `connect ECONNREFUSED 127.0.0.1:9222` / `无法在 … 新建标签页` 退 1 | 没有 Chrome 调试端点，或端点拒绝 `PUT /json/new` | 按上面命令起端点；必要时用 `RENDER_CDP_PORT` 指定端口 |
| `render failed: … status.error` 退 1、不写 PNG | 页面内致命错误（蓝图或清单 fetch 4xx、渲染异常） | 按 error 文本修输入；不再产出空白 PNG |
| `usage:` 打头、退 2、不启动 Chrome | `--views`/`--set`/位置参数非法 | 按提示改参数 |

## 边界与安全

- 不接触游戏，只读输入、只写输出目录。

## 了解实现

<details>
<summary>实现内部 — 点击展开</summary>

源文件：[`export.py`](export.py)、[`validate.py`](validate.py)、[`render_offline.mjs`](render_offline.mjs)。

</details>

## 限制

- `render_offline.mjs` 需要本机有 Chrome 调试端点（默认 9222，`RENDER_CDP_PORT` 可覆盖）；上一轮已实测可跑（`Chrome/155.0.8059.84`）。本轮错误传播/参数校验的退出码在无 Chrome 环境用 mock CDP 与桩测试复现；像素回归在 Ego Lite（Chromium）内对 HEAD 版做 A/B 确认 `--set mat=0` 逐像素相同；`render_offline.mjs` 经真实 CDP 端点截图这一段仍待有 Chrome 的机器复跑（见 [../render/README.md](../render/README.md) 的 Dev Note）。
- `validate.py` 只吃 schema-9 产出，不读 authoring 输入。

-----

## Dev Note

<details>
<summary>维护者工作记录 — 点击展开</summary>

退出码按实跑改写：文件不存在时 `export.py` 抛 `FileNotFoundError`（退 1）；“2 参数/文件错误”仅 argparse。体量：authoring 契约与退出码按所有权只能住本页，已合并重复句，预算上调为 4 KB。

本轮（2026-10-20）`render_offline.mjs` 退出码实跑：`--views abc` / `--views 1` / `--set dist=abc` / `--set dist`（缺 `=`）/ `--set`（缺值）/ 未知 `--flag` → 全部 exit 2、不写 PNG；合法参数但端点缺失 → exit 1、不写 PNG。mock CDP（`PUT /json/new` + WebSocket）验证：成功 exit 0 写 PNG；页面 `status.error` → exit 1、不写图；页面 `__renderExit=2` → exit 2、不写图；模拟标签页崩溃 1 s 内 exit 1（不再挂起）；页面超时 exit 1。静态服务只开放 `examples/ out/ authoring/ render/ vendor/ meshes/`，`%2e%2e`/`..%2f`/根目录文件/兄弟前缀均 404（同一 mock 运行内实测）。像素回归另在 Ego Lite（Chromium）内用 HEAD 版 `modsrender.js` 做 A/B：`--set mat=0` 的 1280×720 canvas 与改动前 **0/921600 像素不同**（原始像素 sha256 相同），证明默认/旧类型色渲染路径未被本轮改动触碰。

</details>
