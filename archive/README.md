---
description: "冻结历史索引：research/ 与 ue4ss/ 里那 5 份原始记录为什么值得留（决策证据、事故记录、唯一只读验证手段），以及被取代的代码为什么没搬进来。"
kind: "index"
---

# archive/ — 冻结历史索引

## 摘要

这里保留 2026-10-18/19 那轮探索里**不可再生**的原始记录：离线渲染能做到哪一步的论证、mod 缩略图渲染器的逐行拆解、引擎内实验的两次事故事实，以及一个只读的 id 核对探针。它们解释了「为什么当前工具链只做数据、校验、导出」，可追溯但已冻结、不维护。被现工具链取代的代码没有搬进来——需要原稿时去原工作区 `帕鲁\blueprints\archive\`，不要在本目录执行任何东西。

## 目录

- [收录标准](#收录标准)
- [文件清单](#文件清单)
- [为什么这 5 份值得留](#为什么这-5-份值得留)
- [不要照着跑](#不要照着跑)
- [Further Exploration](#further-exploration)
- [Dev Note](#dev-note)

-----

## 收录标准

本目录只收三类信息，其余一律不进仓库：

1. **决策证据**：能回答「为什么这样设计」的原始论证。
2. **事故记录**：真实发生过的故障与根因，支撑当前红线。
3. **唯一手段**：离线无法替代、且只读的验证途径。

按此标准，被 `tooling/` 与 `render/` 取代的 11 个探索脚本（`aesthetic.py`、`analyze.py`、`blueprint_tools.py`、`draw_plans.py`、`make.py`、`make_blueprints.py`、`render_engine.py`、`render_preview.py`、`verify.py`、`ue_engine_capture.lua`、`ue_engine_prepare.lua`）、环境专用的 `enable-ue4ss.yml` 与网格探针 `ue_probe_allmeshes.lua` 都没有搬入；原稿仍在原工作区 `帕鲁\blueprints\archive\`。

## 文件清单

| 文件 | 类型 | 独有内容 |
|---|---|---|
| [`research/mod_render_notes.md`](research/mod_render_notes.md) | 决策证据 | mod 缩略图渲染器的管线拆解（含 `BlueprintThumbnailRenderer.lua` 行号）、相机常量出处、320×180 目标与缩略图落盘位置、离线复刻的 3 处已知差异 |
| [`research/agent_blueprint_requirements.md`](research/agent_blueprint_requirements.md) | 决策证据 | 五层能力模型、mod 侧硬约束清单（材料门禁 / 重叠门禁 / 支撑 / 可达性）、8 维审美判据与 5 份样本校准分、差距表与风险 |
| [`ue4ss/UEBridge安装状态.md`](ue4ss/UEBridge安装状态.md) | 事故记录 | 引擎内实验的完整时间线：`GetBounds()` 与 pak bbox 一致、`FindRow` 大小写不敏感的实机确证、纯黑渲染根因、临时光源残留把世界冲白的事故与修复 |
| [`ue4ss/probes/uebridge_client.py`](ue4ss/probes/uebridge_client.py) | 唯一手段 | 走文件协议（`request.json` / `response.json`）的只读客户端，不依赖 MCP |
| [`ue4ss/probes/ue_probe_buildids.lua`](ue4ss/probes/ue_probe_buildids.lua) | 唯一手段 | 在运行中的游戏里用 `FindRow` 核对 id 与蓝图类路径——离线工具链唯一做不到的事 |

## 为什么这 5 份值得留

- **取景公式有出处**：`render/` 用的是 FOV 40、`FRAME_FILL` 0.86、`forward (4.2, 4.2, -3.0)`，这些常量不是拍脑袋，而是 `mod_render_notes.md` 从 mod 源码逐行拆出来的。要改渲染器或怀疑取景时先读这份记录。
- **红线有实证**：`UEBridge安装状态.md` 记了两次真实故障——引擎内渲染实验崩过一次游戏；临时 `DirectionalLightComponent` 残留把世界颜色冲白，而 `pcall(DestroyComponent)` 静默失败。这就是「工具链默认不碰游戏」的来源。
- **未验证项有收口手段**：`data/format.md` 里标注「需游戏本体确认」的几条（导入流程、mod 对 DEFLATE 块的接受范围、ConnectInfo 名称），只有 `ue_probe_buildids.lua` + `uebridge_client.py` 这条只读路径能补证。
- **审美判断有参照**：研究记录里的 8 维判据与校准分可当设计自查清单，但它是原型、只校准过 5 份样本，不是验收标准。

## 不要照着跑

- 路径按旧工作区书写（`帕鲁\...`、`blueprints\...`），本仓库里大多不存在；这些 `.md` 保留当时的时间戳与结论，与现状冲突时以当前 owner 页为准。
- 探针是**只读**的（`FindRow`、`eval` 取数），可用于核对 id；任何渲染、建造、注入类操作仍属红线，禁止接进流程。
- 本目录不提供安装步骤的复制粘贴路径，也不保证原工作区的 UEBridge 还在；复现实验前先读 [data/format.md](../data/format.md) 的红线与 [离线路线的决策记录](../docs/decisions/0001-offline-only-rendering.md)。

## Further Exploration

- [../render/compare/](../render/compare/) — 5 张对照图（mod / 离线 / 引擎内），上面结论的原始视觉证据。
- [../render/README.md](../render/README.md) — 当前预览器契约，相机公式的当前 owner。
- [../docs/decisions/0001-offline-only-rendering.md](../docs/decisions/0001-offline-only-rendering.md) — 由这些记录支撑的路线决策。
- [../data/format.md](../data/format.md) — 硬约束的当前 owner（网格、层高、连接、ID）。

## Dev Note

<details>
<summary>维护者工作记录 — 点击展开</summary>

本目录 5 个文件从原工作区 `C:\Users\25849\Documents\deepseek-harness\帕鲁` 逐字节复制（SHA256 与源一致），未改写；`research/` 与 `ue4ss/` 没有独立入口页，本页是它们唯一入口。

收录判断（Lead）：原目录 19 个文件里只有这 5 个符合「不可再生」标准。探索脚本已被 `tooling/`、`render/` 覆盖，环境专用的 overlay 与网格探针也已过时，搬进来只会让读者误以为还能执行，故未收录，原稿仍在原工作区。探针未在本仓库运行。

</details>
