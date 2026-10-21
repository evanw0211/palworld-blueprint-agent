---
description: "帕鲁 Simple Building Blueprints 的蓝图文件契约：schema-9 字段、PWB9D 文本码帧、400/325 网格与墙朝向、连接属性、BuildObjectId 与 catalog.json 结构。"
kind: "library-reference"
---

# 蓝图格式契约（schema-9 / PWB9D）

## 摘要

一份 schema-9 compact JSON 加 PWB9D 文本码就是一座可导入的建筑，消费者是 mod Simple Building Blueprints。本页给字段表、编码帧、网格与朝向常量、连接属性、id 与 catalog 结构，写 authoring、写解码器、排查导入失败时都对着这里核对。边界：只管文件契约，布局与美观由调用方决定；游戏内导入与 mod 侧枚举名称未在本仓库复现。

## 目录

- [怎么用它](#怎么用它)
- [schema-9 公开契约](#schema-9-公开契约)
- [网格、高度与朝向](#网格高度与朝向)
- [连接属性](#连接属性)
- [PWB9D 文本码](#pwb9d-文本码)
- [BuildObjectId 与 catalog.json](#buildobjectid-与-catalogjson)
- [版本与环境](#版本与环境)
- [集成边界](#集成边界)
- [了解实现](#了解实现)
- [限制](#限制)
- [Further Exploration](#further-exploration)
- [Dev Note](#dev-note)

-----

## 怎么用它

选 id（`catalog.json`）→ 写 authoring → 导出 → 校验 → 交付 PWB9D 码。命令、参数与退出码见 [tooling/README.md](../tooling/README.md)。

~~~powershell
python tooling/export.py authoring/demo_2x2_east_door.json -o out
~~~

成功：报告 `"ok": true`，写出 `out/demo_2x2_east_door.json` 与 `.pwb9d.txt`。失败：列出 `problems`（实测样例 `foundation off the 400 grid`）、不写文件。退出码归 [tooling/README.md](../tooling/README.md)。

mod 接受 PWB9D 文本码（推荐）与裸 schema-9 JSON，游戏内 `B`→`K`→粘贴→导入；**未在本仓库复现**（需游戏本体与 mod），责任人：lead。

## schema-9 公开契约

`validate.py` 要求 `v == 9` 且 `t`、`b` 非空，否则判“不是 schema-9 蓝图”。

| 字段 | 稳定性 | 契约 |
|---|---|---|
| `v` | `stable` | 必须 `9`。 |
| `t` | `stable` | 非空 BuildObjectId 数组（`b` 从 1 引用）。 |
| `b` | `stable` | 非空；每条 `[类型下标, tx,ty,tz, qx,qy,qz,qw, (sx,sy,sz)]`（8 或 11 个数）；相对 anchor，`a` 那条为零变换。 |
| `a` | `stable` | anchor 下标，`1..#b`。 |
| `c` | `stable` | 每条 `[源下标, 目标下标, 属性(1..17), 源连接号, 目标连接号]`；连接号用字符串 `"<nil>"`。 |
| `x` `p` `s` `w` | `experimental` | 可选键，本仓库不产出；`validate.py` 第 129–131 行只对 `x`、`p` 查数组类型，`s`、`w` 完全不检查。 |
| `g` | `forbidden` | 旧地形锚；非 null 即判问题。 |

厘米为单位；位置保留 3 位小数、四元数保留 7 位（`export.py` 的 `tidy()`）；11 元素记录里的缩放三元组本仓库不产出，`validate.py` 只是接受。文件 UTF-8、`\n` 换行、JSON 紧凑且 `sort_keys`。`validate.py` 不拒绝未列出的额外键（docstring 的 "closed set of keys" 与实现不符；mod 侧未验证）。

## 网格、高度与朝向

| 量 | 值 |
|---|---|
| 格距 `CELL` | 400 cm |
| 墙位置 | 格心 ± 200（`EDGE`） |
| 墙高 `WALL_H` | 325 cm |
| 地基平面 | z=0 且共面 |

常量见 `export.py`/`validate.py`；实测 z：地基 0、墙 0、屋顶 325。容差 8 cm（屋顶 10 cm）；旋转蓝图先估角度与相位，角 > 3° 时跳过网格检查。

yaw：西（x−200）=0°、东（x+200）=180°、南（y−200）=90°、北（y+200）=−90°；旋转周期：地基与平屋顶 90°、墙 180°、楼梯/斜屋顶/三角墙 360°。**未复现**：原工作区实机标定，工具不校验朝向值；`authoring/demo_2x2_east_door.json` 按此写且 PASS。责任人：lead。

## 连接属性

名称枚举来自 mod，共 1–18（9 未使用）；**本仓库无该定义文件、未复现**（责任人：lead）：

~~~text
1 ConnectInfoAnyPlace 2 FrontConnectInfo 3 BackConnectInfo 4 LeftConnectInfo
5 RightConnectInfo 6 UpConnectInfo 7 DownConnectInfo 8 DiagonalUpConnectInfo
9 (未使用) 10 DiagonalDownConnectInfo 11 DiagonalLeftConnectInfo 12 DiagonalRightConnectInfo
13 DiagonalBackConnectInfo 14 DiagonalConnectInfo 15 CornerFrontLeftConnectInfo
16 CornerFrontRightConnectInfo 17 CornerBackLeftConnectInfo 18 CornerBackRightConnectInfo
~~~

`validate.py`（第 140 行）只接受属性 1..17，所以 **18 在本仓库不可产出、也不可校验**；`export.py` 只会自动生成 2–7。`2/3/4/5`（地基间 Front/Back/Left/Right 串树）、`6`（Up：屋顶/楼梯 → 下方墙）、`7`（Down：墙类/柱/围栏/摆件 → 地基）由 `export.py` 的 `build()` 生成（已核对源码）；mod 只看“谁连到谁”，建造时服务器重建连接（未复现）。

## PWB9D 文本码

~~~text
PWB9D. + Base64URL( VarUInt(json 字节数) | CRC32LE(json) | raw DEFLATE(json) )
~~~

- `PWB9D.` + **无填充** Base64URL（`A-Z a-z 0-9 - _`）；raw DEFLATE（wbits −15），`export.py` 固定 `zlib.compressobj(9, DEFLATED, -15, 9, Z_FIXED)`（fixed-Huffman）。
- 解码核对声明长度与 CRC32LE，不符报 `PWB9D frame mismatch`；实测解码 `examples/hut_wood_2x2.pwb9d.txt` 得 991 字节、CRC 一致、重编码逐字节相同。
- mod 是否接受 stored 或 dynamic-Huffman 块**未复现**（责任人：lead）。

## BuildObjectId 与 catalog.json

- `id` = `DT_BuildObjectDataTable` 的 RowName；mod 的 `FindRow` 大小写不敏感，整串必须匹配。
- id 不在 catalog 时 `validate.py` 只给 warning，不 FAIL。
- 别名 owner：[`data/build_ids.json`](build_ids.json) 的 `aliases`（59 条）。例：`Wooden_foundation→Wood_Foundation`、`Wooden_wall→Wood_Wall_V2`、`Wooden_roof→Wood_Roof`、`ElectricGenerator→EnergyGenerator_Electric`、`Heater→HeaterMedieval`（已核对）。

~~~json
{"schema":2,"version":{"steam_buildid":"…","mod":{"name":"…","version":"…"}},
 "counts":{"build_objects":498,"with_size":16,"with_materials":498,"verified_live":23},
 "aliases":{"Wooden_wall":"Wood_Wall_V2"},
 "entries":{"Wooden_wall":{"name":{"en":"…","zh":"…"},"category":{"type_a":"…"},
   "materials":[{"id":"Wood","count":2}],"bp_class":"…","size_cm":[…],"icon":"…"}}}
~~~

当前快照 498 件（`by_type_a` 分布见生成文件 catalog_summary.md）；`size_cm` 仅 16 条（glb bbox），`verified_live` 23。图标 `data/icons/<id>.png`（558 张 + `_icons.json`）；拼图 `data/sheets/sheet_<分类>.png`（10 张 + `_index.json`）。**计数以 [data/catalog_summary.md](catalog_summary.md) 为准**；这些文件由生成器写出，不手改（见 [data/refresh.md](refresh.md)）。

## 版本与环境

| 要求 | 支持 |
|---|---|
| schema 版本 | `9`（唯一） |
| mod 快照 | Simple Building Blueprints 0.21.1（见 `catalog.json.version.mod`） |
| Python | 3.14.7（实测；`tooling/*.py` 只用标准库） |
| 平台 | Windows（刷新依赖 Steam 路径） |

兼容承诺：schema 变更必须升 `v`，本仓库只产出 `v: 9`；`catalog.json` 随游戏或 mod 更新整体重生成。

## 集成边界

- 本仓库**不**接触游戏进程：不建造、不注入、不渲染；调用方负责布局、美观、id 选择与坐标/yaw。
- warning（未知 id、缺结构链接、疑似重复槽位）不是失败，只看 `problems`。
- 工具不校验墙朝向与 mod 的建造/支撑门槛；只有游戏内导入能确认（责任人：lead）。

-----

## 了解实现

<details>
<summary>实现内部 — 点击展开</summary>

概念：authoring（厘米/角度、任意原点）→ anchor 局部帧 → schema-9 → 客观检查 → PWB9D。

| 源文件 | 职责 |
|---|---|
| [`tooling/export.py`](../tooling/export.py) | anchor 归一化、类型表、连接图、编码 |
| [`tooling/validate.py`](../tooling/validate.py) | schema、连通、网格、catalog、解码 |
| [`data/build_catalog.py`](build_catalog.py) | 官方表 → catalog / build_ids / summary |
| [`data/make_icon_sheets.py`](make_icon_sheets.py) | 图标 → `sheets/` + `_index.json` |

</details>

## 限制

- PWB9D 帧损坏没有降级路径；`s`、`w` 本仓库完全不校验。
- `validate.py` 只吃 schema-9 产出，喂 authoring 会被判“不是 schema-9 蓝图”。

-----

## Further Exploration

- [tooling/README.md](../tooling/README.md) — 命令与退出码契约。
- [data/refresh.md](refresh.md) — 更新后重生成 catalog 与图标。
- [data/catalog_summary.md](catalog_summary.md) — 计数权威文件。

-----

## Dev Note

<details>
<summary>维护者工作记录 — 点击展开</summary>

非权威。kind 取 `library-reference`：本页是蓝图格式的对外契约（消费者是 mod、产出方是本仓库工具），无 format-spec 模板，按“契约 / 公开面 / 版本 / 边界 / 限制”最贴近该模板。

未复现、待验证：游戏内导入与两种粘贴输入、墙朝向与旋转周期、ConnectInfo 名称、mod 对 DEFLATE 块的接受范围、`x/p/s/w` 语义。责任人：lead（需游戏 + mod + pak）。

本仓库有 git（提交历史即修订记录）、无 CI，改动靠重跑命令复核；`validate.py` 的 docstring 与实现不一致（未知键不拒绝），已按实现写。

</details>
