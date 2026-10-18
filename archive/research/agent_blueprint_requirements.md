> 注：本文是研究记录。工具链已收敛为「数据 + 校验 + 导出」三件事（见 `../tooling/README.md`）；esthetic.py / make.py 等主观生成/评分工具已归档到 ../archive/。

# 让 Agent 产出「可用且美观」的蓝图，需要什么？

> 研究范围：Palworld 官方模组 **Simple Building Blueprints**（Nexus 4073 / 工坊 3768908799）。
> 结论来自三处实证：mod 源码（schema / 规划器 / 渲染器）、实机接口探针（DT 数据表 / GetBounds / 会话渲染）、
> 社区蓝图（r/PalworldSBB 3 份 + 我们生成的 4 份）。

---

## 一、TL;DR：五层能力 + 一个循环

| 层 | Agent 需要什么 | 现状 |
|---|---|---|
| ① 数据 | 建筑目录：BuildObjectId、尺寸、**连接 socket**、材料、科技解锁、风格标签 | 部分（15 类有尺寸/材料；socket 未采） |
| ② 生成 | 蓝图 DSL + 约束求解（吸附/支撑/开洞/屋顶）+ 风格模块 + 参数搜索 | 只有"小屋"模板 |
| ③ 校验 | schema、连通性、支撑、重叠、网格/层高、材料/科技、地形 | schema/连通/网格已有；重叠/地形缺 |
| ④ 感知 | 真网格渲染（离线）+ 多视角 + 与 mod 缩略图对比 | 离线渲染已通 |
| ⑤ 审美 | 启发式评分 + 可选 VLM 评审 + 人类反馈 | 8 维评分原型已写 |
| 循环 | 生成 → 校验 → 渲染 → 评分 → 修复 →（可选）导入实测 | 手工循环，未自动化 |

**一句话**：Agent 缺的不是"会不会摆方块"，而是 **①每个件的连接规则 + ②结构化的设计语言 + ③可量化的审美/可用性反馈**。

---

## 二、「可用」的硬约束（mod 会拒绝或建不出来）

| 约束 | 数值/规则 | 证据 |
|---|---|---|
| schema | `v=9`，`t/b/a` 必有，`c` 连接数组，禁止 `g` | `BlueprintJsonSchema.lua:221-303` |
| ID | 必须是 `DT_BuildObjectDataTable` 行名；`FindRow` **大小写不敏感** | 实机探针 |
| 网格 | 地基 400cm；墙在格边 ±200；墙高/层高 **325**；屋顶 = 墙基 +325 | 社区蓝图 + 解码统计 |
| anchor | `a` 为 1..#b；坐标全部相对 anchor；anchor 可在地基、门、墙上（≠网格中心） | 社区样本（268/261/188…） |
| 连通性 | `c` 必须把所有件连到 anchor，否则规划器报 "unreachable" | `BlueprintWavePlanner.lua:541-578` |
| 支撑 | 地基要落地成 root；墙 Down→地基、屋顶 Up→墙 | 实机 + 社区 `c` 统计 |
| 材料 | 默认材料门禁开启，按配方扣材料；被服务器拒会退料 | modconfig + 工坊评论 |
| 重叠 | 默认重叠门禁开启 | `blueprint_overlap_gate_enabled=true` |
| 落地 | 4 块地基都要在平整地面（支撑门禁） | 实机诊断日志 |

> 结论：**"可用"是可以完全离线判定的**，除了地形/服务器接受度这两项。

---

## 三、「美观」拆成可计算的 8 个维度

评分原型 `tooling/aesthetic.py`（100 分制），权重与判据：

| 维度 | 权重 | 判据 | 社区/攻略依据 |
|---|---|---|---|
| 外墙闭合 | 15 | 外轮廓每条边都有墙/门/窗 | 围合感是"房子"的第一信号 |
| 开口节奏 | 10 | ≥1 门；窗数 ≈ 周长/6 | 实墙盒子 vs 有门窗的房子 |
| 屋顶 | 15 | 覆盖主体；**≥2 种屋顶**（平屋顶 + 斜/三角） | 屋顶教程普遍强调层次 |
| 对称 | 15 | 地面层墙体/门窗的镜像匹配率 | 社区蓝图对称度普遍高 |
| 比例 | 10 | 长宽比 ≤1.6；≥2×2 格；多层要有楼板 | 3×3 比细长条好看 |
| 构件 | 15 | 四角柱、楼梯、内墙隔断 | 日式/塔楼教程必提柱与檐口 |
| 材质 | 10 | 1 种主导 + 1 种点缀（2 家族最佳） | 木/石/硬木/玻璃混搭要有主次 |
| 装饰 | 10 | 装饰件占比 ≈ 15–20% | 案例里的灯、植物、家具显著提分 |

**原型打分（校准样本）**：

| 蓝图 | 分 | 等级 | 主要扣分 |
|---|---|---|---|
| hut_wood_2x2 / 3x3 | 58.5 | C | 无柱、无装饰、单一屋顶、不对称 |
| hut_japanese_3x3 | 60.5 | C | 同上 |
| community basic base | 74.8 | B | 开放外廊（外墙不闭合）、材质无主导 |
| community Grover's house | 65.6 | B | 材质 4 家族、地面层不对称 |
| community four-base | 60.0 | C | 超大规模、无门窗（纯平台） |

> 说明：原型对"亭子/中庭/开放回廊"这类**故意不闭合**的设计会误扣分，需要后续加"是否有屋顶/柱廊"的白名单判断。分数用于**同类型互相比较**，不是绝对值。

---

## 四、Agent 需要的能力栈（详细）

### ① 数据层（当前最大缺口）
- **建筑目录**：每个 BuildObjectId 的 `family / footprint(cm) / height / 安装策略 / 连接 socket / 材料 / 科技等级 / 风格标签 / 变体(平/斜/三角/门/窗/柱/梯)`。
  - 现有：15 类有 glb 实测尺寸 + 材料；`catalog_exported.json` 31 类有 bbox。
  - **缺**：每类的连接点语义（Front/Back/Left/Right/Up/Down 对应哪个物理边）与 `actor_relative_to_model`（决定"门/墙"本地坐标系，Grover 这类门做 anchor 的蓝图就靠它才能解析）。
  - 获取方式：pak 导出（`tools/PalAssetExport`）+ 实机 CDO 探针（`MainMesh/AllMeshes` 的 bounds 与相对变换）。
- **规则库**：网格步长、层高、支撑传播、门洞/窗洞占位、屋顶覆盖规则。
- **风格预设**：木屋 / 日式 / 玻璃现代 / 石堡：主材、屋顶组合、开窗密度、装饰清单。

### ② 生成层
- **DSL**：`{footprint: NxM, storeys: k, rooms: [...], openings: [...], roof: flat|sloped|pyramid, trim: pillars|fence, decor: [...]}`。
- **求解器**：格点吸附 → 墙/门窗落边 → 地基/楼板/屋顶逐层 → 支撑链 → 自动补 `c` 连接数组。
- **风格模块**：可参数化的"一个房间/一栋小屋/一个中庭"模板，按尺寸拼接。
- **搜索**：在 DSL 参数空间做随机/贪心搜索，以 ③④⑤ 为打分函数，选最优。

### ③ 校验层
- 已有：schema、连通性、网格/层高（`verify.py`）。
- 需补：**包围盒重叠检查**（墙/屋顶/家具之间）、**支撑链自检**（每件到 anchor 的路径）、**材料/科技预算**、**地形平整度**（需要实机或存档高程）、**门洞可达性**（从外部进入内部）。

### ④ 感知层
- 已有：离线真网格渲染（`render_offline.mjs`，pak 导出的 glb + mod 相机公式）。
- 需补：多视角（正/侧/俯/轴测）、按材质上色（贴图未导出）、与 mod 缩略图并排对比。
- 可选（有风险）：引擎内用 mod 自己的 session actor 渲缩略图——已验证可行，但**不要在自动循环里默认开**（今天崩过一次游戏）。

### ⑤ 审美层
- 启发式（已写）：8 维打分 + 问题清单，直接给生成器当奖励信号。
- VLM 评审（建议）：把渲染图丢给多模态模型，问"像不像一栋房子/哪里怪"，与启发式互补。
- 人类反馈：用户对样式的偏好（木/日式/现代）作为权重先验。

### ⑥ 循环（Agent 工作流）
```
design brief (尺寸/风格/预算/地形)
   ↓
catalog + 风格预设 → DSL 参数
   ↓
生成器 → 蓝图 JSON (+c)
   ↓
verify.py（硬约束）──失败──→ 修复/换参数
   ↓ 通过
aesthetic.py 打分 + 渲染图
   ↓ 达标？
导入游戏（人工或受控脚本）→ mod 生成官方缩略图 / 放置诊断
   ↓ 反馈
更新风格预设与权重（记忆）
```

---

## 五、现状 vs 目标（差距表）

| 能力 | 现状 | 缺口 |
|---|---|---|
| 建筑目录 | 15 类尺寸 + 31 类 bbox + 材料 | 全部 ~560 类的 socket/安装策略/科技 |
| 生成 | 2×2 / 3×3 木屋/日式（`make.py`） | 多层、房间、斜屋顶、装饰、任意尺寸 |
| 校验 | schema/连通/网格/层高 | 重叠、支撑链、预算、地形、可达性 |
| 渲染 | 离线真网格 | 多视角、贴图、批量对比 |
| 审美 | 8 维启发式 | 校准、VLM、风格权重 |
| 循环 | 人工 | 自动化搜索 + 记忆 |

---

## 六、建议路线图

1. **Phase 0（半天）数据补齐**：用 pak 导出 + 实机 CDO 探针，产出 `catalog.json`（id/family/size/socket/actor_offset/material/tech/style）。
2. **Phase 1（1–2 天）生成 DSL**：把 `make.py` 从"小屋模板"升级为 `house(n,m,storeys,style,roof,trim,decor)`，自动补连接。
3. **Phase 2（1 天）校验补全**：重叠/支撑链/预算检查接入 `verify.py`。
4. **Phase 3（1–2 天）闭环**：搜索参数 → 校验 → 渲染 → 打分 → 选优；输出 top-3 蓝图 + 预览图。
5. **Phase 4（可选）实测反馈**：人工导入一次，读 mod 缩略图/放置诊断，回收为评分权重。

---

## 七、风险与红线

- **不要自动在游戏里建造/放组件**：今天引擎内渲染实验（未拦截空场景）导致游戏崩溃一次；渲染类操作必须默认离线。
- **ID 必须实机核对**：`FindRow` 大小写不敏感，但整串必须存在；未验证的 ID 会让导入直接失败。
- **纹理缺失**：离线渲染是"真网格 + 假材质"，颜色深浅不代表游戏内观感；审美以布局/比例为主。
- **截图不可信**：HDR 下抓图会发白（粉→白），不要用抓图判断颜色。
- **编码**：PowerShell 默认控制台会把中文输出成乱码；脚本读写统一 UTF-8。

---

## 八、参考

- 屋顶层次：[Mastering Roof Building in Palworld](https://palworldcorner.com/articles/mastering-roof-building-in-palworld-a-2026-guide-to-stunning-bases)
- 日式墙面/屋顶组合：[Japanese-Style Base guide](https://www.switchbladegaming.com/palworld/japanese-style-base/)
- 高楼基地构造：[Skyscraper Mega Base](https://www.gamer.org/how-to-build-a-palworld-skyscraper-mega-base-step-by-step/)
- mod 源码：`Scripts/BlueprintJsonSchema.lua`、`BlueprintWavePlanner.lua`、`BlueprintThumbnailRenderer.lua`、`BlueprintThumbnailSessionRuntime.lua`
- 本仓库工具：`tooling/make.py`、`tooling/verify.py`、`tooling/aesthetic.py`、`tooling/render_offline.mjs`

