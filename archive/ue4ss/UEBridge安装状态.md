# UEBridge / UE4SS MCP 安装状态（2026-10-18 23:5x）

## 已完成（只写文件，未启动/未重启任何进程）

1. **游戏侧 UEBridge 1.2.0 已装回**
   - 位置：`D:\SteamLibrary\steamapps\common\Palworld\Mods\NativeMods\UE4SS\Mods\UEBridge`
   - 文件：`enabled.txt`、`README.txt`、`LICENSE.txt`、`scripts\{main.lua,json.lua,settings.lua}`
   - `Mods\mods.txt` 末尾已加 `UEBridge : 1`（原文件备份为 `mods.txt.bak_<时间>`）
   - `scripts\settings.lua` 里 `bridge_dir` 指到：
     `D:\SteamLibrary\steamapps\common\Palworld\Pal\Binaries\Win64\ue4ss\bridge`（已创建该目录）
     —— 因为这套 UE4SS 装在 `Mods\NativeMods\UE4SS`，不在 `Pal\Binaries\Win64\ue4ss`，默认路径对不上。

2. **DSH 侧启用补丁**
   - `帕鲁\enable-ue4ss.yml`：把 desktop profile 用户层里 `- id: ue4ss-mcp` 的 `disabled: true` 覆盖成 `false`。
   - 用法（不改用户层，临时验证）：
     ```
     dsh --profile desktop --patch "C:\Users\25849\Documents\deepseek-harness\帕鲁\enable-ue4ss.yml"
     ```
   - 永久启用：把 `%USERPROFILE%\.dsh\profiles\desktop\cordis.patch.yml` 里的
     `- id: ue4ss-mcp` / `disabled: true` 改 `disabled: false`（或删掉这两行），重启 DSH desktop。
   - 注：当前 web profile 没装这个 bundle；MCP 工具名形如 `mcp__ue4ss__eval_lua`。

3. **备用客户端（不依赖 MCP / 不用重启 DSH）**
   - `scripts\ue4ss\uebridge_client.py`：直接走文件协议（`request.json` / `response.json`，protocol 3）
     ```
     python scripts/ue4ss/uebridge_client.py --ping
     python scripts/ue4ss/uebridge_client.py --eval "return 1+1"
     python scripts/ue4ss/uebridge_client.py --file scripts/ue4ss/ue_probe_meshes.lua
     ```
   - `scripts\ue4ss\ue_probe_meshes.lua`：拿木墙/木顶/木地基的真实 `GetBounds()`（mod 渲缩略图用的就是它）。

## 还差一步：重启游戏

UE4SS 只在启动时加载 Lua mod。重启后验证：
- `D:\...\Mods\NativeMods\UE4SS\UE4SS.log` 出现 `[UEBridge] v1.2.0 ready`
- 然后 `python scripts/ue4ss/uebridge_client.py --ping` 应返回 `{"ok": true, ...}`

## 我拿到桥之后会做的三件事（调用和 mod 一样的接口）

1. `mesh:GetBounds()`：核对 15 类建筑真实包围盒，校准离线渲染的取景。
2. 用 `SceneCaptureComponent2D` + `PalUtility` 同款相机直接在引擎里拍蓝图缩略图 → 写 PNG 到磁盘，**和 mod 自己渲的逐像素可比**。
3. 放置预检：对目标坐标做 overlap / support 查询（mod 的 `PalBuildObject` 校验路径），在不动手建造的前提下判断蓝图能不能放。

## 回滚

- 删除 `Mods\NativeMods\UE4SS\Mods\UEBridge`
- `mods.txt` 去掉 `UEBridge : 1`（或用备份还原）
- 删除 `Pal\Binaries\Win64\ue4ss\bridge`（可选）
- 撤销 DSH 侧：把 `disabled` 改回 `true` 或不用 `--patch`

---

## 2026-10-18 23:5x 实测通过

- 游戏重启后 `UE4SS.log`：`[UEBridge] v1.2.0 ready (protocol 3, generation 1), polling .../ue4ss/bridge/request.json every 50 ms`
- `python scripts/ue4ss/uebridge_client.py --ping` → `{"ok": true, "result": {"pong": true, "poll_ms": 50, "handled": 2}}`
- `SM_Wall_Wood` / `SM_Roof_Wood` / `SM_Floor_Wood` 的 `mesh:GetBounds()` 与 pak 导出的 glb bbox 完全一致（wall 29.18×401.33×333.54 cm 等）
- `DT_BuildObjectDataTable:FindRow(name)` 对大小写不敏感：`Wooden_foundation` / `Wooden_Foundation` 命中同一行；实机确证 `Wooden_foundation`、`Wooden_wall`(→`Wood_Wall_V2`)、`Wooden_DoorWall`(→`Wood_DoorWall`)、`Wood_WindowWall`、`Wood_SlantedRoof`、`Wooden_roof`、`Wooden_stair`、`Wooden_pillar`、`Stone_Foundation`、`JapaneseStyle_*` 全部存在，并拿到了各自的 `BlueprintClassSoft` 路径。
- 据此重出蓝图 A：全木（地基/墙/门/窗/屋顶，16 件，木头 ×32），真网格缩略图见 `blueprints/thumb_hut_wood_realmesh_1280.png`。

---

## 2026-10-19 00:0x 引擎内缩略图（SceneCapture）进度

**已跑通的部分**
- `scripts/ue_thumb_prepare.lua` + `ue_thumb_capture2.lua`（arm/shoot 两段）：解析蓝图 → 按 `DT_BuildObjectDataTable` 解析每类 BP → 从 CDO 组件取 mesh 相对变换 → 建 `InstancedStaticMeshComponent`、两盏 `DirectionalLightComponent`、`SceneCaptureComponent2D`（FOV 40、320×180、双灯、show-only）→ `KismetRenderingLibrary:ExportRenderTarget` 写 PNG → 销毁全部临时组件。
- 输出：`C:\Users\25849\Documents\dsh-pal\inengine_wood.png`（320×180，真投影、真 mesh、同 `compute_camera_frame`）。
- 投影与离线渲染完全吻合（见 `blueprints/compare_hut_offline_vs_inengine.png`），说明相机/坐标/缩放链路正确。

**未解决**
- 画面里模型是纯黑（无光照）。已排除：灯强度（×100 仍黑）、贴图材质（强塞 placeholder 材质后几何直接不出现）、时机（拆分 arm/shoot 等一帧仍黑）。
- 根因判断：mod 的真实渲染走 **PAK render backend**（`main.lua:1166 use_pak_backend=true`，`BlueprintThumbnailSessionRuntime` 自己 `SpawnActor(BP_SBBThumbnailSessionActor_C)` 并管理 render state）。我们复刻的是 `renderer.render_prepared` 里 `use_pak_backend ~= true` 的** player 兜底路径**，show-only 场景拿不到动态光。
- 下一步要么读 `BlueprintThumbnailSessionRuntime.lua` 把 session actor 的组件/渲染设置补齐；要么直接导入蓝图让 mod 自己渲缩略图（最省事、最权威）。

---

## 2026-10-19 00:1x 事故与修复：临时光源残留把世界颜色冲白

**症状**：用户看到桃花的粉色变白（世界光照异常）。

**根因（我的问题）**：早期"player 兜底路径"实验在玩家角色上 `AddComponentByClass` 创建了 6 盏 `DirectionalLightComponent`（强度 16/48，`bAffectsWorld=true`）。脚本里的 `pcall(DestroyComponent)` **静默失败**，组件残留 → 世界叠加 224 强度直射光 → 颜色被冲淡（粉→白）。

**修复**：
1. 软清理：6 盏 `SetIntensity(0)`、`SetVisibility(false)`、`bAffectsWorld=false`；
2. 硬清理：`K2_DestroyComponent(player)` + `DestroyComponent()`；
3. 复查：`FindAllOf("DirectionalLightComponent")` → `count=10, live=4, pending=6, contributing=4`。
   4 盏 live = 原生世界光（DirectionalLight 7.25 / SunSky 75000 / PalSkyCreator sun 30 / moon 0.2）；6 盏 pending 已无 owner、强度 0，不参与渲染。

**验证**：引擎内相机截图 `blueprints/world_lighting_after_cleanup.png`（桃花恢复粉白色）。

**预防（脚本已改）**：
- 渲染脚本结束一律走 `zero intensity → bAffectsWorld=false → destroy → 复查 contributing 数量`；
- session 路径的灯挂在临时的 `BP_SBBThumbnailSessionActor` 上，随 actor `K2_DestroyActor` 一起消失；
- 若仍有异常，重启游戏即可清空待销毁组件与任何瞬时状态。

**教训**：UE4SS 里 `DestroyComponent` 失败不抛错，必须"置零 + 复查"，不能只 pcall 销毁。

