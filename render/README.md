---
description: "离线缩略图渲染页：用 pak 导出的真网格与 mod 同款相机公式出图，并给出与引擎内渲染的对照证据和限制。"
kind: "component-reference"
---

# render/ - 离线缩略图渲染页面

## 摘要

`render/` 提供离线出缩略图的渲染页面：读一份 schema-9 蓝图与 `meshes/` 里的真网格，按 mod 的相机公式用 three.js 画出来，再由 `tooling/render_offline.mjs` 经本机 Chrome 截成 PNG。你可以在不启动游戏的情况下检查体量、层数、轮廓与开口位置是否符合预期；默认机位是 mod 那一套，也可以**任意换机位或一次出多视角**。边界：出图是「真网格 + 按材质上色」，几何与取景对齐游戏内，但**默认不带贴图**（可传 `texmap` 挂真贴图）、光照也不代表游戏内观感。启动命令与 Chrome 端点由 [tooling/README.md](../tooling/README.md) 拥有，本页只写页面契约与保真度证据。

## 目录

- [怎么用](#怎么用)
- [保真度与对照证据](#保真度与对照证据)
- [失败与恢复](#失败与恢复)
- [了解实现](#了解实现)
- [限制](#限制)
- [Further Exploration](#further-exploration)
- [Dev Note](#dev-note)

-----

## 怎么用

渲染页面没有自己的 CLI：`tooling/render_offline.mjs` 起一个本地静态服务，把蓝图路径、id→BP 映射、actor 偏移与网格目录拼成查询参数传给 `render/modsrender.html`，再通过 Chrome DevTools 协议截图并写 PNG（源码：[render_offline.mjs](../tooling/render_offline.mjs)）。命令与 9222 端点的启动方式见 [tooling/README.md](../tooling/README.md)；下面是页面实际接受的参数（默认值取自源码）。

| 参数 | 默认 | 作用 |
|---|---|---|
| `bp` | 必填 | 要渲染的 schema-9 JSON 路径（服务根是仓库根） |
| `idmap` | `/meshes/_id_to_bp.json` | BuildObjectId → 蓝图名，决定加载哪个 GLB |
| `offsets` | `/meshes/_actor_offsets.json` | 每类 `actor_relative_to_model` 的平移与旋转 |
| `meshes` | `/meshes` | GLB 目录 |
| `w` / `h` | `1280` / `720` | 截图尺寸；**默认取景**仍按 mod 的 320×180 竖直比例算 |
| `parts` | 开 | `0` 关掉多部件构件的分件加载 |
| `wrot` | 关 | `1` 施加 anchor 的世界旋转 |
| `mat` | `1` | `1` 按 UE 材质名上色；`0` 回到旧的按构件族类型色（回归用） |
| `texmap` | 无 | URL，指向 `{ 材质名或 BuildObjectId: 贴图 }`；值可以是**字符串**（当 albedo 用）或**对象** `{map, normalMap, roughnessMap, metalnessMap}`。给了就上真贴图 |
| `az` / `el` | `225` / `26.8` | 自由机位：UE 方位角（0=+x 东、90=+y 北）与仰角（度） |
| `dist` | 自动 | 机位距离（米）；不给就按包围盒 8 角点自动取景 |
| `fit` | `1` | 自动取景距离的倍率 |
| `top` | 关 | `1` = 俯视（az 225 / el 89.5） |
| `ortho` | 关 | `1` = 正交投影，便于量比例 |
| `views` | 无 | **多视角检查图**：数字（如 `6`）= 等分方位角；`"az,el;az,el"` = 指定机位。输出一张网格拼图，每格独立取景并带 az/el 标注 |

一次运行的判定看脚本最后那行 JSON：`ok`、`output`、`status`，以及进程退出码（0=截图已写出 / 1=渲染失败 / 2=用法错误）。`status` 里是 `pieces`、`placed`、`partial`、`missing`、`failed`、`errors`、`camera`、`bbox`，外加 `materials`（每种材质着色的**图元数**）、`textures`（带贴图的**图元数**，不是去重后的贴图张数——一份 54 张贴图的 texmap 可以打出 361 个带贴图图元）与多视角时的 `views`。恒等式 `pieces = placed + partial + missing + failed`。

- `missing` 只放「`meshes/` 没导出该 id」——判定条件是所有候选 GLB 都回 404。
- `failed` 是请求/解析失败（非 404：5xx、损坏 GLB、网络错误），原因逐条写在 `errors` 里。
- `partial` 是多部件构件只加载到部分部件的件数（**不计入 `placed`**）。
- `errors` 是对象数组 `{stage, id, url, error}`，`stage` ∈ `texmap`/`texture`/`glb`/`part`；超过 80 条时余量记在 `errorsTruncated`。
- 页面内致命错误由 `main().catch` 写成 `{ error }` 这种形状，脚本打印它并 **exit 1、不写 PNG**；`--views`/`--set` 的非法值由页面标成用法错误（exit 2、不写 PNG）。见失败表。

`materials` 是**可计算的验收口径**：它的分项应当和蓝图的构件台账对得上（例：48 件坡屋顶 → `japanese_tile: 48`；63 件石地基每个 2 个图元 + 57 根石柱 → `stone: 183`）。所以"材质有没有生效"不必靠看图。

### 自由检查

默认那一张是 mod 的固定机位。要看别的面：

~~~powershell
# 6 个方位角的检查图（3x2 网格，每格带 az/el 标注）
node tooling/render_offline.mjs out/kotori_keep.json out/kotori_keep_views.png 1800 1200 --views 6
# 任意机位 + 正交投影
node tooling/render_offline.mjs out/kotori_keep.json out/x.png 1200 900 --set az=135 --set el=40 --set ortho=1
# 俯视（配合 ortho 就是平面量测图）
node tooling/render_offline.mjs out/kotori_compact.json out/y.png 1200 900 --set top=1 --set ortho=1
# 轴向对齐的俯视图：top=1 只改仰角、不动方位角，所以要正对还得自己给 az
node tooling/render_offline.mjs out/kotori_compact.json out/y2.png 1200 900 --set top=1 --set ortho=1 --set az=0
# 回归：应逐字节复现改动前的图（见"保真度"）
node tooling/render_offline.mjs examples/hut_wood_3x3.json out/legacy.png --set mat=0
~~~

## 保真度与对照证据

相机不是估出来的：页面逐行移植了 mod 的 `BlueprintThumbnailRenderer.compute_camera_frame`。常量与出处都在冻结研究 [mod_render_notes.md](../archive/research/mod_render_notes.md)：那份记录从 mod 的 `BlueprintThumbnailRenderer.lua` 拆出逐行管线——水平 FOV **40°**、取景填充 `FRAME_FILL = 0.86`、固定机位方向 `normalize(4.2, 4.2, -3.0)`、包围盒 8 个角点求相机距离（下限 100 cm）、`SceneCaptureComponent2D` 出 320×180 渲染目标、缩略图落盘到 `%LOCALAPPDATA%\SimpleBuildingBlueprints`。页面按同一公式在 three.js 里重建：蓝图厘米、GLB 米（缩放 100），轴映射 UE `(x, y, z)` → three `(x, z, -y)`。该记录同时写明离线复刻已知的 3 处差异：没有贴图（只能按 UE 材质名近似上色，见下）、缺 6 件 Fence、anchor 的世界旋转默认不施加。以上事实以那份归档记录为准，本页只引用不复述推导。

为什么是「真网格 + 按材质上色」：从 pak 导出的 GLB **一张贴图都没有**（17 个文件 `images: 0`），20 个 glTF 材质也全部没有 `baseColorFactor`，顶点色 `COLOR_0` 更是**全 0**（旧文档说"偏黑"是低估了，实测是纯 0，拿去乘会得到全黑块）。这条暴露了"只看 JSON 会漏掉什么"：17 个 GLB 的 `COLOR_0` accessor **都没有 `min`/`max`**，读 glTF 的 JSON chunk 根本看不出来，必须解 BIN chunk 里的 accessor 数据——本仓库对全部 17 个文件解码后的 min / max / mean 都是 0.000，且只有 1 个不同取值。**所以顶点色这条路是死的。** GLB 本身也没有贴图，颜色不可能来自资产——**唯一可用的材质信号是 UE 材质实例名**：`MI_PropGlass_Roof_Glass`、`MI_PalProp_Floor_Stone`、`MI_PalProp_Wall_Wood`、`MI_PalProp_JapaneseStyle_*`。页面因此按**图元**（不是整件）读材质名判定材质族，再按构件角色取色：

| 判定 | 结果 |
|---|---|
| 材质名以 `_Parts` 结尾 | 玻璃构件的**框**（不透明）——必须先判，否则会被当成玻璃 |
| 材质名含 `Glass` | 玻璃：半透明 `opacity 0.30` + `depthWrite: false` + 双面 |
| 含 `Stone` / `Wood` / `JapaneseStyle` | 石 / 木 / 和风 |
| 材质名缺失 | 退回按 `BuildObjectId` 判角色（屋顶/地基/柱/墙） |

按图元判定是有实际收益的：`Glass_Roof`、`Glass_Wall`、`Stone_Foundation` 都是**一个网格里两种材质**，只有按图元才能把玻璃面与框、或台面与台身分开。夹具 [`fixtures/glass_2x2.json`](fixtures/glass_2x2.json) 专门覆盖这条分支：4 件木质地基 + 2 片玻璃墙 + 2 片玻璃顶 → `{wood: 4, glass: 4, glass_frame: 4}`（地基 4×1 木图元；玻璃墙/玻璃顶各 1 个框图元 + 1 个玻璃图元）。

**回归口径**：`--set mat=0` 走旧类型色，应当与改动前逐字节相同（实测 `examples/hut_wood_3x3.json` → 两次 sha256 都是 `7f07a2b6…98d5c`）。这一条同时证明机位、几何、取景都没被这次改动碰到。

想要真贴图就上 `texmap` 参数（页面已支持）。注意**贴图在资产里有、是导出器不写**：pak 里这 20 个 `MI_*` 共引用 58 张贴图且全部可解码，但 CUE4Parse-Conversion 的 glTF writer 只写材质名，所以导出的 GLB 里没有 `images`——根因与修复成本见 [tooling/PalAssetExport/README.md](../tooling/PalAssetExport/README.md)。相机与几何一致、外观不一致，就是下面这些对照图要说明的事。

| 对照图 | 它证明什么 |
|---|---|
| [compare_hut_offline_vs_inengine.png](compare/compare_hut_offline_vs_inengine.png) | 同一相机公式下，左为离线真网格渲染、右为引擎内 SceneCapture 实拍。图内标题写明「引擎内图目前未受光（mod 真实路径走 PAK render backend），但几何投影完全一致」：轮廓、檐口、支柱位置逐一对上，差异只在着色。 |
| [compare_offline_vs_inengine_grid.png](compare/compare_offline_vs_inengine_grid.png) | 木屋（A）与日式小屋（B）两行：左列离线真网格、右列引擎内复刻（同款 session actor + 真材质贴图）。同机位几何一致；左列是类型色、右列是游戏材质——这就是「材质不代表观感」的直接证据。 |
| [compare_basic_realmesh.png](compare/compare_basic_realmesh.png) | Basic Base（313 件）：左为 mod 引擎内渲染，右为离线真网格渲染（同相机、无贴图）。两翼楼体、中庭、楼梯与石柱围栏的布局和体量对齐。 |
| [compare_basic_base_mod_vs_offline.png](compare/compare_basic_base_mod_vs_offline.png) | 更早的 Basic Base 对照：左 mod 渲染、右离线代理体块。取景与体量对得上，但代理只到方块级；真网格渲染补齐了轮廓。 |
| [compare_grover_mod_vs_offline.png](compare/compare_grover_mod_vs_offline.png) | Grover's First House（262 件）的同类对照：左 mod 渲染、右离线代理，角度与占画幅一致。 |

![离线真网格渲染 vs 引擎内 SceneCapture：几何投影一致，引擎内未受光](compare/compare_hut_offline_vs_inengine.png)

![离线真网格 vs 引擎内真材质：同机位几何一致，材质与光照不同](compare/compare_offline_vs_inengine_grid.png)

## 失败与恢复

失败分三层：脚本退出码、页面 `status`、Chrome 端点。带 † 的行在没有 Chrome 的沙箱里用 mock CDP 与 three/DOM 桩逐条复现过；其余行沿用上一轮 Chrome 实跑记录。

| 现象 | 原因 | 处理 |
|---|---|---|
| `connect ECONNREFUSED 127.0.0.1:9222`，exit 1，无 PNG † | 本机没有 Chrome 调试端点 | 按 [tooling/README.md](../tooling/README.md) 起端点后重跑 |
| `无法在 … 新建标签页`，exit 1，无 PNG † | 端点拒绝 `PUT /json/new`（旧版 Chrome 或启动参数限制） | 换新版 Chrome；并用 `RENDER_CDP_PORT` 指向正确端口 |
| `status.missing` 非空 † | 所有候选 GLB 都 404：`meshes/` 没导出该 id | 补导出该类型网格；缺件只影响该件显示，`ok` 仍可能为 true |
| `status.failed > 0` † | GLB 请求/解析失败（非 404：5xx、损坏、网络） | 看 `status.errors` 里 `stage=glb` 的条目修资产或服务 |
| `status.partial > 0` † | 多部件构件只加载到部分部件 | 看 `stage=part` 的 errors；该件不计入 `placed` |
| `status.errors` 有 `stage=texture` † | texmap 贴图解码失败或超时（20s） | 该材质回落到族色继续出图；修 texmap URL 后重跑 |
| `status.error` 有内容，exit 1，无 PNG † | 页面内致命错误：蓝图/`_id_to_bp.json`/`_actor_offsets.json` 读不到、渲染异常 | 按 error 文本修输入；这一类不再产出空白 PNG |
| 用法错误，exit 2，不启动 Chrome、无 PNG † | `--views` 非数字或不是合法 `az,el;az,el`、`--views N` 越界、`--set` 缺 `=` 或值为空、`--set` 数值键给非数字、位置参数非法 | 按 `usage:` 提示修正 |

## 了解实现

<details>
<summary>实现内部 - 点击展开</summary>

链路是「静态服务 + 浏览器内 three.js + CDP 截图」：本机起一个只读静态服务（只服务 `examples/`、`out/`、`authoring/`、`render/`、`vendor/`、`meshes/` 六个目录，`..` 与符号链接逃逸被 realpath 判定拒绝）；脚本用 `PUT /json/new` **自建一个后台标签页**（不 navigate 用户已有标签页），完成或失败后关闭它；页面按参数加载蓝图、id 映射与 actor 偏移，逐个把 GLB 实例化到蓝图记录的世界变换上；包围盒由已放置对象的真实 AABB 求并，再套用 mod 的相机公式；`texmap` 的每张贴图在首帧渲染前被 await 完成解码；渲染完成后页面设置 `window.__renderDone`、`window.__renderStatus` 与 `window.__renderExit`（错误时另设 `__renderFailed`），脚本轮询到标记后截图并映射成退出码。多部件构件（如门叶 + 门框）按 `meshes/_parts.json` 逐个部件加载，缺部件记 `partial`。

| 区域 | 源文件 | 职责 |
|---|---|---|
| 启动与截图 | [tooling/render_offline.mjs](../tooling/render_offline.mjs) | 起静态服务、拼页面 URL、连 9222、等渲染标记、写 PNG |
| 页面骨架 | [modsrender.html](modsrender.html) | 画布与 three.js importmap（`vendor/three.module.js`） |
| 场景与相机 | [modsrender.js](modsrender.js) | 载 GLB、套 actor 偏移、按 UE 材质名上色、按 `compute_camera_frame` 或自由机位取景、渲一张或渲成多视角网格 |
| 材质夹具 | [fixtures/glass_2x2.json](fixtures/glass_2x2.json) | 覆盖玻璃分支的最小蓝图（4 地基 + 2 玻璃墙 + 2 玻璃顶 → `{wood:4, glass:4, glass_frame:4}`） |
| 网格与映射 | `meshes/` 下的 `_id_to_bp.json`、`_actor_offsets.json`、`_parts.json` 与 `.glb` | BuildObjectId → 蓝图名、模型偏移、多部件清单 |

</details>

## 限制

- 需要 Chrome 调试端点；默认 `127.0.0.1:9222`，可用 `RENDER_CDP_PORT` 覆盖。起端点的命令由 [tooling/README.md](../tooling/README.md) 拥有。上一轮已实跑（`Chrome/155.0.8059.84`）；本轮改动的错误传播/贴图等待/多视角覆盖由 mock CDP、桩测试与 Ego Lite（Chromium）内实跑覆盖，像素回归用同一浏览器对 HEAD 版做 A/B 逐像素确认（见 Dev Note）；`render_offline.mjs` 经真实 CDP 端点截图这一段仍待有 Chrome 的机器复跑。
- **默认没有贴图**：不传 `texmap` 时，颜色是按 UE 材质名判定的近似色，不是游戏内真实材质；光照也不是游戏内的。传了 `texmap` 才是真贴图，而且页面会**先等所有贴图解码完再渲第一帧**；某张贴图解码失败或超过 20s 会回落到族色并写进 `status.errors`（`stage=texture`），不会静默输出黑块。贴图不在版本库里（见 [tooling/PalAssetExport/README.md](../tooling/PalAssetExport/README.md)）。验收看轮廓、层数、体量、开口位置，以及 `status.materials` 的分项是否与构件台账一致。
- `meshes/_id_to_bp.json` 当前只映射 18 类，社区蓝图用到 49–55 类，未导出的 id 会在 `status.missing` 里列出，图不完整。
- anchor 的世界旋转默认不施加（只有 `?wrot=1` 才施加）；mod 在导入时渲缩略图用的锚点变换等价于蓝图相对坐标系，实测默认关闭才与 mod 缩略图一致。
- 社区样例的渲染图 `examples/community/reddit_grovers_first_house_render.png` 是从原工作区带过来的 Reddit 帖子内嵌图，不是本仓库重跑的产物。

-----

## Further Exploration

- [../tooling/README.md](../tooling/README.md) - 启动命令、Chrome 端点与退出码（命令的唯一 owner）。
- [../tooling/PalAssetExport/README.md](../tooling/PalAssetExport/README.md) - 从 pak 导出 GLB 与数据表的工具契约。
- [../examples/README.md](../examples/README.md) - 可拿来渲染的样本清单与校验结果。
- [../docs/agent-notes/AN-0001-offline-only.md](../docs/agent-notes/AN-0001-offline-only.md) - 为什么只做离线渲染。
- [../archive/research/mod_render_notes.md](../archive/research/mod_render_notes.md) - 冻结研究：mod 缩略图管线逐行证据与离线复刻边界。

## Dev Note

<details>
<summary>维护者工作记录 - 点击展开</summary>

上一轮 Chrome 实跑记录（`Chrome/155.0.8059.84`，端点 `127.0.0.1:9222`，保留）：`examples/hut_wood_3x3.json` 30/30 件、`missing: []`；`out/kotori_keep.json` 298/360 件、`missing` 62 件（全部是无 GLB 的设施）；多视角 `--views 6` 出 3×2 拼图；玻璃夹具 `render/fixtures/glass_2x2.json` 命中 `{wood:4, glass:4, glass_frame:4}`。

上一轮回归口径：`--set mat=0` 渲染 `examples/hut_wood_3x3.json` 得到 sha256 `7f07a2b6…98d5c`，与改动前 `out/pipeline_hut_wood_3x3.png` 逐字节相同。

**本轮（2026-10-20/21，P0-1/P1-8/P1-9/P2-13/P2-14）实跑证据**（本机沙箱内 Chrome 无法启动：crashpad `OpenProcess` / mojo 被拒；改用 Ego Lite（Chromium）直接加载 `render/modsrender.html`，用 `page.evaluate` 读状态、`page.cdp` 与 `canvas.toDataURL` 取像素）：

- `node --check render/modsrender.js` 与 `node --check tooling/render_offline.mjs` → 0。
- 页面致命错误（真实浏览器）：把 `bp` 指到不存在的文件 → `__renderFailed=true`、`__renderExit=1`、`status.error` 含 `404`；同一次会话里 `render_offline.mjs` 对同一信号 exit 1、不写 PNG（mock CDP 复现）。
- 退出码矩阵（实跑）：`--views abc`、`--views 1`、`--set dist=abc`、`--set dist`（缺 `=`）、`--set`（缺值）、未知 `--flag` → 全部 **exit 2 且不写 PNG**；合法参数 + 端点缺失 → **exit 1 且不写 PNG**（消息含 `connect ECONNREFUSED 127.0.0.1:9222`）。
- 像素回归（同一浏览器 A/B，排除浏览器/色管差异）：`examples/hut_wood_3x3.json`，HEAD 版 `modsrender.js`（改动前）对比修复版 `--set mat=0`，canvas 1280×720 **0/921600 像素不同**，原始像素 sha256 相同（`ea9ed0d6…`）——旧类型色/默认机位/几何路径未被本次改动触碰。
- 多视角覆盖（真实浏览器）：`--views 6` + 1280×720 → `camera.tile=[427,360]`（ceil），canvas alpha 全 255、无纯黑未绘制条带（右 2 列与底行都是 tile 内容）；桩测试同时实测 uncovered=0。
- 玻璃夹具（真实浏览器）：`status.materials={wood:4, glass:4, glass_frame:4}`，与 [:131](#) 的断言一致。
- mock CDP（`PUT /json/new` + WebSocket 帧）：成功路径 **exit 0** 并写出 PNG；页面 `status.error` → **exit 1**、不写图；页面 `__renderExit=2` → **exit 2**、不写图；模拟标签页崩溃 → **1s 内 exit 1**（不再挂起）；页面 3s 未完成 → exit 1。mock 日志记录 `PUT /json/new`、`DeviceMetrics 1280x720`、`Page.navigate`、`GET /json/close/TARGET1`，没有 `/json/list` 调用。
- 静态服务边界（同一次 mock 运行实测）：`/render/modsrender.html`、`/meshes/_id_to_bp.json`、`/vendor/three.module.js` → 200；`/out/%2e%2e/.gitignore`、`/out/..%2f.gitignore`、`/%2e%2e/%2e%2e/secret.txt`、根目录 `.gitignore`、`outx/` 兄弟前缀 → 全部 404。
- 页面逻辑桩测试（stub three + DOM，直接 import `modsrender.js`）：**57/57 通过**，覆盖用法错误（不发起 fetch）、默认只渲 1 帧、texmap 解码后才渲染、贴图失败写 `errors` 并回落族色、GLB 404→`missing`、GLB 5xx→`failed`、parts 半成功→`partial`、`--views 6` 像素全覆盖与 scissor 复位。

**仍未端到端跑的一项**：`render_offline.mjs` 经真实 CDP 端点截图（本机沙箱起不了 Chrome）。页面侧与脚本侧各自的信号、退出码、像素都已分别实测，端到端只差这一条连接；有 Chrome 端点时按下面命令复跑即可盖章（Chrome 截图路径的 sha256 预期仍是 `7f07a2b6…98d5c`）：

```powershell
node tooling/render_offline.mjs examples/hut_wood_3x3.json out/_lead_check/legacy2.png --set mat=0
node tooling/render_offline.mjs examples/hut_wood_3x3.json out/_lead_check/views6.png 1280 720 --views 6
node tooling/render_offline.mjs render/fixtures/glass_2x2.json out/_lead_check/glass.png 600 400
```

</details>
