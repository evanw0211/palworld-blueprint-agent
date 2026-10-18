# mod 的缩略图渲染是怎么实现的（BlueprintThumbnailRenderer）

> 读的是 `D:\SteamLibrary\steamapps\workshop\content\1623730\3768908799\Scripts\BlueprintThumbnailRenderer.lua`（1748 行）与 `BlueprintPreviewMesh.lua`。结论：**渲染在游戏引擎里做，用的不是蓝图码里的数据，而是真 mesh + 运行时 `GetBounds()`**。所以离线只能复刻“相机/取景”，不能复刻真实外观。

## 一、管线（逐行证据）

1. **数据来源**：`type_templates[BuildObjectId].preview_meshes[]`，每个 descriptor = `{static_mesh_path | skeletal_mesh_path, relative_transform, material_slot_count}`（`BlueprintPreviewMesh.lua` 全文）。这些是保存蓝图时从 CDO/实例上抓的 mesh 路径 + 相对变换，**不含包围盒**。
2. **逐个 mesh 取包围盒**：`mesh:GetBounds()` → `{Origin, BoxExtent, SphereRadius}`（`BlueprintThumbnailRenderer.lua:543`，`copy_asset_bounds` 在 `:328-346`）。
3. **算世界包围盒**：`world_transform = ComposeTransforms(mesh.relative_transform, building.relative_transform)`（`:547-550`）；把 `Origin ± BoxExtent` 的 8 个角变换到世界，逐轴取 min/max（`include_prepared_asset_bounds`，`:348-419`）。没有 BoxExtent 时退化成球半径（`:397-418`）。
4. **相机**：`compute_camera_frame(bounds_min, bounds_max)`（`:62-110`）
   - `center = (min+max)/2`
   - 水平 FOV **40°**，`FRAME_FILL = 0.86`，`vertical_tangent = horizontal_tangent / (320/180)`
   - 距离 = 用 8 个包围盒角点在相机基下的 `horizontal/h_tan - depth`、`vertical/v_tan - depth` 取最大值，下限 `MIN_CAMERA_DISTANCE = 100`、`MIN_CAMERA_DEPTH = 10`
   - 固定机位方向 `CAMERA_FORWARD = normalize(4.2, 4.2, -3.0)`；`location = center - forward * distance`
5. **出图**：`SceneCaptureComponent2D`（`:845` 起）+ **320×180** 渲染目标；两盏灯：暖色 intensity 48、冷色 16（pitch -12°、yaw +115°，`:20-32`）；占位建筑用 `MI_LooksPredicatorDismantle` 材质。
6. **落盘**：`SBB_Thumbnail_<id>.png` 写在蓝图库目录（`%LOCALAPPDATA%\SimpleBuildingBlueprints`），库面板用 `ImportFileAsTexture2D` 读回来显示（`BlueprintLibraryManager.lua:241-254`）。**导入完成 / 保存蓝图时各渲一次**。
7. **放置预览**：`BlueprintGhostRenderer.lua` 用同一批 preview mesh 做幽灵，不重新渲图。

## 二、离线能复刻到哪一步

| 能力 | 离线可行性 |
|---|---|
| 相机位置/朝向/距离/取景（`compute_camera_frame`） | ✅ 已按公式 1:1 移植（`blueprint_tools.py` 的 `mod_camera_frame` / `--panel MOD`） |
| 光照方向/背景色 | ✅ 近似（双灯 + 蓝色渐变底） |
| 真实 mesh 外观（墙板、门窗、屋顶曲面、家具） | ❌ 需要游戏 pak 里的 mesh 几何 + bounds；蓝图码/模板里都没有 |

证据：`compare_grover_mod_vs_offline.png`（左 = Reddit 上 mod 渲的 320×180，右 = 离线代理用同一相机算出来的）——**角度、取景、体量一致**，但右边是方块代理。

## 三、因此“验证建筑”的正确闭环

1. 导入蓝图（B → K → 粘贴 → 导入）→ mod 会自己渲一张 `SBB_Thumbnail_<新id>.png`。
2. 我直接读 `%LOCALAPPDATA%\SimpleBuildingBlueprints\SBB_Thumbnail_*.png` 看图核对（能识别：结构、层数、开口位置、屋顶形态）。
3. 或者你在游戏里截图（`Win+Shift+S` / `F12`）丢给我，我按图比对蓝图数据。
4. 离线工具用于**数据层校验**：`python blueprint_tools.py verify <bp.json>` 检查 400 网格 / 200 边线 / +325 层高 / 连通性 / 重复件；`render --panel TOP|TQ|MOD` 出俯视/三分之四/官方取景代理图。

## 四、已跑过的对照

- `Basic Base`（313 件，你库里那张缩略图）：mod 渲染 = 日式围院 + 中央泳池 + 两栋两层楼 + 石柱围墙 + 楼梯；离线代理同机位能对上轮廓。
- `Grover's First House`（262 件，Reddit 渲染图）：mod 渲染 = 两层日式住宅、木/红瓦坡顶、正面两排窗；离线代理同机位轮廓吻合。
- 我们生成的 `hut_wood_2x2`：`render_hut_wood_2x2_modcam.png`（官方取景）+ `plan_hut_wood_2x2.png`（俯视）+ verify 通过（15/15 连通、地基 z=0、墙 z=0、屋顶 z=325）。

---

# 附：离线真网格渲染已实现（用之前的三维重建管线）

> 2026-10-18 23:5x。`viewer/models` 里已有上一轮从 pak 导出的 **Wooden_Foundation / Wooden_Wall / Wooden_Roof**，`tools/PalAssetExport`（.NET 10 + CUE4Parse）+ `tools/mappings/Palworld.usmap` 可以继续导出别的类型。于是照 mod 的相机做了一套离线缩略图渲染。

## 链路

```
蓝图 JSON ──► meshes/_id_to_bp.json ──► blueprints/meshes/<BP>.glb（CUE4Parse 从 Pal-Windows.pak 导出）
        └──► render/modsrender.html + modsrender.js（three.js）
                · UE→three：(x, z, -y)，glb 米制 ×100 得厘米
                · 每件 = 建筑变换 ∘ actor_relative_to_model（模板里的偏移）
                · 包围盒 = 所有真网格 AABB
                · compute_camera_frame 逐行移植（FOV 40、FRAME_FILL 0.86、forward (4.2,4.2,-3.0)）
        └──► render/render_thumbnail.mjs：起静态服务 + Chrome CDP 截图
```

## 用法

```powershell
# 需要 Chrome 带 --remote-debugging-port=9222（本机 DSH 会话的浏览器就是）
node blueprints/render/render_thumbnail.mjs blueprints/hut_wood_2x2.json out.png 1280 720
```

## 已导出的网格（15 类）

Wooden_Wall / Wooden_Roof / Wooden_Foundation / Stone_Foundation / Stone_Pillar、JapaneseStyle_Wall_01 / DoorWall_01 / WindowWall / roof_01 / SlantedRoof / TriangleWall / stair / Pillar、Glass_Wall / Glass_Roof。
（`JapaneseStyle_Fence` 在 pak 里没匹配到蓝图名，暂时缺件；补别名表即可。）

## 验证结果：与 mod 自己的缩略图逐项对齐

同一份 `Basic Base`（313 件）：
- 几何：两翼日式楼 + 中庭 + 三处楼梯 + 石柱围栏，位置/朝向/层高全部一致（见 `compare_basic_realmesh.png`，左 mod / 右离线）。
- 相机：同 `compute_camera_frame`，视角/取景一致。
- 差异：①离线无贴图（统一按类型上色，`COLOR_0` 顶点色是黑的，不能用）；②缺 6 件 Fence；③`anchor_world` 旋转默认不施加——mod 在**导入**时渲缩略图用的锚点变换等价于本蓝图相对坐标系，加了 `?wrot=1` 反而转歪，实测默认关闭才和 mod 缩略图一致。

结论：**现在可以在离线状态给任意蓝图出真网格缩略图，并据此验收建筑。**
