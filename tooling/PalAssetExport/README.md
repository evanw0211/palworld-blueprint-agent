---
description: "从 Pal-Windows.pak 转数据表、导图标/网格/蓝图类，并用 info/list/inspect 诊断挂载：前置、环境变量、退出码与已知警告。"
kind: "component-reference"
---

# PalAssetExport — 从游戏 pak 取数据与资产

## 摘要

挂载 `Pal-Windows.pak` 后，这个 CLI 能把 UDataTable 转成 JSON、把图标表里的 SoftIcon 解成 PNG、把建筑蓝图导出为 glb，也可以用 `info`/`list`/`inspect` 只读确认挂载与映射是否正常。它需要 .NET 10 SDK、本机装好的 Palworld，以及 `tooling/mappings/Palworld.usmap`；游戏路径与 usmap 路径都能用环境变量覆盖。只有 `info`、`list` 和不带 `outJson` 的 `inspect` 不写盘，其余命令都会写盘。刷新流程的步骤、验证与过期判断 owner 是 [data/refresh.md](../../data/refresh.md)，本页只写工具契约。

## 目录

- [怎么用](#怎么用)
- [运行](#运行)
- [失败与恢复](#失败与恢复)
- [边界与安全](#边界与安全)
- [了解实现](#了解实现)
- [限制](#限制)
- [Further Exploration](#further-exploration)
- [Dev Note](#dev-note)

-----

## 怎么用

先跑两条只读命令确认挂载与 usmap 可用，再决定跑哪条写盘命令。下面每条都标了本仓库的实测状态。

### 何时选它

需要 pak 里的原始数据或资产时才用它：把 7 张表转成 `data/raw/` 的 JSON、把图标解成 PNG、把建筑蓝图导出为 glb。只想校验或导出蓝图时用 [tooling/README.md](../README.md)，完全不需要游戏安装；只想消费现成快照时直接用已提交的 [data/](../../data/README.md)，不必重建。它的代价是需要本机装好的游戏与本页的前置文件，且写盘命令会覆盖 `data/` 的版本锁定数据。

### 前置与最小配置

- .NET 10 SDK（本机实测 `dotnet --version` = `10.0.402`）。
- Palworld 安装目录含 `Pal\Content\Paks\Pal-Windows.pak`。
- `tooling/mappings/Palworld.usmap`，随本仓库提交；文件缺失时 cooked 包无法反序列化。

~~~powershell
dotnet build tooling/PalAssetExport            # 已实测：0 错误 / 16 警告
dotnet run --project tooling/PalAssetExport -- info   # 已实测
~~~

| 输入 | 默认 | 作用 |
|---|---|---|
| `PALWORLD_PAKS` | `D:\SteamLibrary\steamapps\common\Palworld\Pal\Content\Paks` | 指向游戏 pak 目录；目录不存在时挂载抛未捕获异常 |
| `PALWORLD_USMAP` | 空 | 显式指定 usmap；设了就用它，不再向下搜索 |
| （无变量时的 usmap 搜索） | 从当前目录与程序目录向上逐级找 `tooling/mappings/Palworld.usmap` | 在仓库任意子目录或仓库外运行都能命中 |

不带任何参数运行只打印 usage 并退出 2，不会挂载 pak——缺参、未知命令、非法数字选项都在挂载前判定，所以这类拼写错误不会触发 185141 个文件的挂载。`exporttex` 的 outDir 仓库根判定发生在挂载之后、任何写盘之前（子命令分派本身在挂载后），那一步已付出挂载成本，但仍不会写任何文件。每个子命令的参数形态在小节 [运行](#运行)，完整分派逻辑的 owner 是 [Program.cs](Program.cs) 的 `switch (args[0])`。

### 可观察行为

挂载成功时先打印两行，然后才是命令自己的输出：

~~~text
[map] usmap 3,312,408 bytes attached
[pak] 185141 files mounted from Pal-Windows.pak
  files           = 185141
  MountedVfs      = 1
  RequiredKeys    = 0
  vfs: PakFileReader enc=False files=185141
~~~

`info` 成功退出 0；`list` 末行是 `-- <n> match(es), showing <m>`，无匹配也退 0。前置缺失时不是非零退出加错误码，而是下面两种可辨认的信号之一：usmap 找不到会打印 `[map] WARNING: usmap missing -- cooked packages will fail to deserialize` 并继续（退出 0）；pak 目录找不到会让 CUE4Parse 抛 `DirectoryNotFoundException`，进程以 0xE0434352 结束。退出码契约见 [失败与恢复](#失败与恢复) 的 0/1/2 表：用法错误退 2，`exporttex` 的输入/导出失败退 1。

-----

## 运行

第一次成功后，日常只做三件事：跑只读诊断、跑写盘命令、以及在怀疑构建产物过期时重建。

| 命令 | 参数 | 作用 | 本仓库实测 |
|---|---|---|---|
| `info` | 无 | 打印挂载文件数、VFS 数、密钥数 | 已实测 |
| `list` | `<子串> [--exclude <子串>] [--limit N]` | 列出 pak 内匹配路径（`--limit` 默认 60） | 已实测 |
| `inspect` | `<包路径> [outJson]` | 打印包内对象类型与名字；给了 `outJson` 才写盘 | 已实测（不带 `outJson`） |
| `dumptable` | `<资源路径> <outJson>` | UDataTable → JSON（行名 + 属性） | 未实测 |
| `exporticons` | `<图标表JSON> <outDir> [limit]` | `SoftIcon` → PNG，并写 `<outDir>\_icons.json` 映射 | 未实测 |
| `exportmesh` | `<对象路径> <outDir>` | UStaticMesh → glb（非 StaticMesh 则中止） | 未实测 |
| `exporttex` | `<对象路径> <outDir>` | UStaticMesh 的基色/法线贴图 → PNG，并写 `<outDir>\_texmap.json`（键 = glTF 材质名）；有失败时退 1 | 已实测（`SM_Wall_Wood`） |
| `exporttex --all` | `<outDir> [meshesDir]` | 批量：`<meshesDir>/_meshrefs.json` 的全部网格 → 基色/法线/ORM PNG，并写 `_texmap_all.json` 与 `_texmap_all_orm.json`（对象形式）；有失败时另写 `_failures.json` 并退 1 | 已实测（17 网格 / 20 材质） |
| `exportbps` | `<布局JSON> <outDir>` | 蓝图类 → StaticMesh 引用清单 `<outDir>\_meshrefs.json`（已递归索引 `BuildObject/` 子目录） | 已实测（481 条一遍解析 452） |
| `exportall` | `<meshrefsJSON> <outDir>` | 每个引用导 glb，并写 `<outDir>\_parts.json` | 已实测（452 bp → 602 GLB，0 错误） |

~~~powershell
# 已实测：pak 里有哪些 DT_BuildObjectDataTable 资源
dotnet run --project tooling/PalAssetExport -- list DT_BuildObjectDataTable --limit 5
# 已实测：某蓝图类里有什么对象（不带第二个参数即只读）
dotnet run --project tooling/PalAssetExport -- inspect Pal/Content/Pal/Blueprint/MapObject/BuildObject/BP_BuildObject_Stone_Foundation
~~~

`list` 实测输出 4 条 `DT_BuildObjectDataTable*` 路径、末行 `-- 4 match(es), showing 4`；`inspect` 实测输出 `20 object(s) in ...` 与对象列表。

三条写盘命令（`dumptable`/`exporticons`/`exportmesh`）在本仓库未实测：它们会覆盖 `data/` 的版本锁定数据，执行与验证的 owner 是 [data/refresh.md](../../data/refresh.md)（该页写明责任人 lead）。7 个表的资源路径也由那一页维护，本页不复制。**已实测的写盘命令有三条**：`exporttex`（2026-10-19 由 lead 授权，单件 + `--all` 批量，只写 `out/textured/`）与 `exportbps`/`exportall`（同日由 lead 授权做资产补全试跑，只写 `meshes_new/`）——三者都不碰 `data/`。**2026-10-20 修复后**：`exporttex` 的 outDir 必须先解析到仓库根内（否则 exit 2 且不写盘），导出失败退 1，批量另写 `_failures.json`；单件与批量都按贴图对象路径去重，同名不同包用 SHA256 前 8 hex 后缀。2026-10-19 实测产物的内容语义不变（texmap 格式见下）：

~~~powershell
# 已实测：木墙的基色 + 法线贴图 → PNG，并写 _texmap.json
tooling\PalAssetExport\bin\Debug\net10.0\PalAssetExport.exe exporttex Pal/Content/Pal/Model/Prop/Architecture/Architecture_Wood/SM_Wall_Wood out/textured
# 已实测：全量（17 网格 / 20 材质 / 3 图各）→ PNG + 两个合并 texmap；约 78 秒，只挂载一次 pak
tooling\PalAssetExport\bin\Debug\net10.0\PalAssetExport.exe exporttex --all out/textured
~~~

批量模式（`--all`）与单件模式在**同一个子命令**里：`args[1] == "--all"` 走批量，否则走单件。两者的 outDir 都先 `Path.GetFullPath` 再判定是否落在仓库根内（仓库根按 `render/modsrender.html` / `tooling/PalAssetExport` 向上查找），在根内才输出真实的仓库根相对 URL，否则打印 error、退 2、一个文件都不写。批量只 `Mount()` 一次 pak 后循环，不会每网格重挂。网格清单来自 `<meshesDir>/_meshrefs.json`（跳过无 `meshes` 的键，本仓库当前是 `JapaneseStyle_Fence`）；**额外补一轮**：`meshes/*.glb` 里出现、但 `_meshrefs.json` 没覆盖的材质（当前是 `Wood_DoorWall` / `Wood_WindowWall` 两个 GLB）按 `MI_*` 包名直接解析补齐——否则 texmap 键集会少于 GLB 并集。批量把每处失败（网格不是 StaticMesh、材质不是 `UMaterialInstanceConstant`、基色/法线/ORM 通道缺失或编码失败、GLB-only 材质在 pak 里找不到、GLB 头非法）连同 context/object/reason 记进 `<outDir>/_failures.json` 并退 1；没有失败时删掉上一次留下的 `_failures.json`，不会留下「新 PNG + 旧 texmap」却报成功。

两个合并 texmap 都是**对象形式**，键 = GLB 里的 `materials[].name`，值是**真实**仓库根相对 URL（outDir 解析后若不在仓库根内，命令已退 2，所以这里不会出现裸文件名）：

~~~json
// _texmap_all.json（安全版）
{ "MI_PalProp_Wall_Wood": { "map": "/out/textured/T_Wall_Wood_B.png",
                            "normalMap": "/out/textured/T_Wall_Wood_N.png" } }
// _texmap_all_orm.json（实验版）：上面两个键 + roughnessMap + metalnessMap，都指向原始 ORM
{ "MI_PalProp_Wall_Wood": { "map": "…", "normalMap": "…",
                            "roughnessMap": "/out/textured/T_Wall_Wood_M.png",
                            "metalnessMap": "/out/textured/T_Wall_Wood_M.png" } }
~~~

ORM 那版是**故意不做通道拆分**的：three.js 从 G 读 roughness、从 B 读 metalness，而 UE 侧的打包顺序未确认，所以它可能看起来发黑/过金属。保留原样是为了让 A/B 可行，不要「修」它。

输出目录由调用方指定，但 `exporttex`（单件与 `--all`）要求它解析后落在仓库根内，否则退 2 且不写任何文件；本仓库**刻意不提交**这些产物：`out/` 在 `.gitignore` 里（`.gitignore:6` 的 `out/*`），所以游戏贴图不进版本库——这是再分发授权未决时的默认边界。

重复运行 `dotnet run` 会重印 NuGet 警告；构建过一次后加 `--no-build`，或直接跑 `tooling\PalAssetExport\bin\Debug\net10.0\PalAssetExport.exe`，输出就只剩程序自己的行。

-----

## 失败与恢复

先看退出码，再看输出。0/1/2 只覆盖参数校验与 `exporttex`；pak 目录缺失仍是未捕获异常（0xE0434352），不在这张契约里。

| 退出码 | 含义 | 何时出现 |
|---|---|---|
| 0 | 成功 | `info`/`list`/`inspect` 正常；`exporttex` 单件全部通道写出且无失败；`exporttex --all` 无失败（texmap 与 GLB 材质并集无关项齐全）。`exportbps`/`exportall`/`dumptable`/`exporticons`/`exportmesh` 仍未接入统一失败码，见 [限制](#限制) |
| 1 | 运行中失败（输入缺失/不可解析、导出或编码失败） | `exporttex --all`：`_meshrefs.json` 缺失/坏 JSON、任一网格不是 StaticMesh、材质不是 `UMaterialInstanceConstant`、基色/法线/ORM 通道缺失或编码失败、GLB-only 材质解析失败、GLB 头非法（同时写 `<outDir>/_failures.json`）；`exporttex` 单件：对象加载失败、不是 StaticMesh、基色通道或材质解析失败（法线图失败只打印、不计入退出码），`_texmap.json` 可能不完整 |
| 2 | 用法错误（参数类错误在挂载前判定；`exporttex` 的 outDir 仓库根判定在挂载后、任何写盘前） | 不带参数、缺参、未知子命令、`--limit`/`[limit]` 不是整数、`exporttex` 的 outDir 解析后不在仓库根内 |

下面每行都是本仓库实际观察到的现象；未复现的失败模式不列。

| 现象 | 原因 | 诊断 | 处理 |
|---|---|---|---|
| `Unhandled exception. System.IO.DirectoryNotFoundException: The game directory could not be found.`，退出码 0xE0434352（Windows 记作 `-532462766`） | `PALWORLD_PAKS` 或默认路径下没有 pak 目录 | `Test-Path "$env:PALWORLD_PAKS"` / 对照默认值 | 改成真实 pak 目录，或清掉该变量用默认值 |
| `[map] WARNING: usmap missing -- cooked packages will fail to deserialize`，退出 0 | `PALWORLD_USMAP` 指向不存在的文件，或仓库里 `tooling/mappings/Palworld.usmap` 被删 | 看这行是否出现；`Get-Item tooling\mappings\Palworld.usmap` | 修正变量或恢复 usmap；`list`/`info` 仍可用，`dumptable`/`inspect` 会失败 |
| `ERROR: '<命令>' needs at least N argument(s) ...`、`ERROR: unknown command '<名字>'` 或 `ERROR: --limit expects an integer`，随后打印 usage，退出 2 | 缺参、子命令拼错、数字选项非法；校验在挂载前完成，不会挂载 pak | 看 stderr 的 `ERROR:` 行与 usage | 按 [运行](#运行) 的命令表改正；不带参数运行即打印 usage |
| `ERROR: meshrefs file not found: ...` 或 `ERROR: cannot parse ..._meshrefs.json: ...`，退出 1，输出目录**不创建** | `<meshesDir>/_meshrefs.json` 缺失或不是合法 JSON（`meshesDir` 默认 `meshes`，可用第 4 个参数指定） | 确认该文件存在、`Get-Content ... \| ConvertFrom-Json` 能否解析 | 先跑 `exportbps`，或把 `meshesDir` 指对 |
| `ERROR: outDir '...' resolves to '...', which is outside the repository root '...'`，退出 2，未写任何文件 | outDir 是仓库外的绝对路径，或用 `..` 逃出了仓库根；texmap 值是仓库根相对 URL，无法表达 | 看 ERROR 里打印的两个绝对路径 | 把 outDir 指到仓库根内（如 `out/textured`），或从仓库根用相对路径运行 |
| `<outDir>\_failures.json` 出现且进程退出 1 | 批量有失败（网格不是 StaticMesh、材质不是 `UMaterialInstanceConstant`、通道缺失/编码失败、GLB-only 无法解析、GLB 头非法） | 读 `_failures.json` 的 `kind`/`context`/`object`/`reason`，对照 stdout 末尾的失败清单 | 按 kind 分类处理；修完重跑，无失败时该文件会被自动删除 |
| 每行输出前有 `warning NU19xx` | `dotnet run` 每次重建并重印 NuGet 审计警告 | 看是否为 `PalAssetExport.csproj` 前缀 | 加 `--no-build`，或直接跑已构建的 exe |

-----

## 边界与安全

它只读 pak 与 usmap，不启动游戏进程、不写游戏安装目录、不需要任何凭据。写盘目标由参数决定：`dumptable` 覆盖同名 JSON，`exporticons`/`exportmesh`/`exportbps`/`exportall` 在指定目录下新建或覆盖文件，`exporttex`/`exporttex --all` 还额外要求 outDir 解析后落在仓库根内（否则退 2、不写盘）。因此把输出目录指到 `data/` 就会直接改写版本锁定数据——这正是本仓库不跑写盘命令的原因。文件名取自 pak 内的资产名（贴图/材质短名），不拼接用户输入，所以参数本身不会把文件写到 outDir 之外；贴图 PNG 是逐个解码逐个写盘，不落临时大文件。构建依赖从本机 NuGet 缓存解析（实测离线成功），程序本身不发起网络请求。

-----

## 了解实现

<details>
<summary>实现内部 — 点击展开</summary>

一个约 1400 行的顶层语句程序，没有类与项目结构：先按 `minArgs` 表在**挂载前**统一校验缺参、未知命令与非法数字选项（失败打印 usage 并退 2），再处理 `findapi`/`probe`/`mapapi` 等反射诊断命令，然后构造并挂载 `DefaultFileProvider`，再按 `args[0]` 分派。挂载之后才附加 usmap——CUE4Parse 的 mappings provider 挂在 file provider 的 `MappingsContainer` 上，而不是 `VersionContainer`；`Initialize()` 只注册 VFS，`Mount()` 才真正读 pak 索引（源码首部注释记下了这个坑）。各子命令自己读参数、自己写盘；`exporttex` 的输入/导出失败由它自己置 exit 1（批量另写 `_failures.json`），其余写盘命令尚未接入统一失败码。

| 区域 | 源 | 职责 |
|---|---|---|
| 命令分派与全部子命令 | [`Program.cs`](Program.cs) | 解析参数、挂载 pak、附加 usmap、执行 `list`/`info`/`inspect`/`dumptable`/`exporticons`/`exporttex`/`exportmesh`/`exportbps`/`exportall` 与反射诊断 |
| 依赖清单 | [`PalAssetExport.csproj`](PalAssetExport.csproj) | `net10.0`、`Nullable`+`ImplicitUsings`、CUE4Parse 与 CUE4Parse-Conversion 1.2.2.202610 |
| 类型映射 | [`../mappings/Palworld.usmap`](../mappings/Palworld.usmap) | 3.2 MB 二进制 usmap，cooked 包的反序列化依据 |

</details>

-----

## 限制

- 除 `exporttex` 外的五条写盘命令在本仓库未实测——刷新前必须先读 [data/refresh.md](../../data/refresh.md)，不要凭本页直接执行。
- [data/refresh.md](../../data/refresh.md) 的第 1 段仍写「第 1 段在原工作区 `tools/PalAssetExport`」；本仓库已带同一工具（本页），是否改写由该页 owner 决定。
- 没有 `--help`；参数形态只能读源码或本页的命令表。
- 退出码只覆盖参数校验与 `exporttex`：0 成功 / 1 导出或输入失败 / 2 用法错误（见 [失败与恢复](#失败与恢复)）；`exportbps`/`exportall`/`dumptable`/`exporticons`/`exportmesh` 失败时仍可能退 0 或抛未捕获异常，不能当门禁。usmap 缺失仍退 0，pak 目录缺失仍以 0xE0434352 终止。
- 反射诊断命令（`findapi`/`probe`/`mapapi`/`convapi`/`whereis`/`dump`）是当时的排障脚本，不保证随依赖升级仍可用；本页未实测。
- `exportmesh`/`exportbps`/`exportall` 只导出几何与引用清单，不导贴图、不组装蓝图，也不做任何放置可行性判断。`exporttex` 是例外：单件模式导基色+法线并写字符串形式 `_texmap.json`；批量模式（`--all`）导基色/法线/ORM 三图并写对象形式 `_texmap_all.json` 与 `_texmap_all_orm.json`，有失败时写 `_failures.json` 并退 1（outDir 必须在仓库根内，否则退 2）。两者都**不是** GLB 内嵌（见下条）。
- **`exportbps` 曾静默丢掉 45% 的蓝图（2026-10-19 已修）**：`BuildObject/` 下有子目录（`Furniture/` 201、`MultiProduct/` 12、`LabResearchSpace/` 9、`Base/` 5、`ForDevelop/` 1，共 **228/562**）。它们的包名形如 `Furniture/BP_BuildObject_Chair01_Wood`，而 `ResolveBp` 只拿裸名 `BP_BuildObject_<id>` 做完全匹配，**永远命中不了**——首次全量跑 481 条只解析出 233 条，其余 219 条被报成 `no matching blueprint`，**没有任何错误信号**。修法：建「裸包名 → 相对路径（优先顶层）」索引再查。实测同一份 481 条布局从"两遍凑 452"变成**一遍 452 / 失败 29**。
- 那 **29 个失败项不是导出问题**：它们没有 `StaticMesh`，是 `USkeletalMeshComponent` 道具（旗帜、`FlourMill`、`Crusher`、`OilPump`、发电机）。`inspect` 可见 `PalMapObjectSkeletalMesh_GEN_VARIABLE` / `PalSkeletalMesh_GEN_VARIABLE`。要补需新增 `USkeletalMesh` 导出路径。
- `exportbps` 的源码注释与本页早前都提到会写 `_bboxes.json`，**实测从未写出**，只写 `_meshrefs.json`。
- **为什么导出的 GLB 里 `images: 0`（只读实测的根因）**：pak 侧资产齐全——`Architecture/` 下有 **1218 个 `T_*.uasset`** 与同数 `.ubulk`，17 件构件对应的 20 个 `MI_*` 共引用 **58 张不同贴图**（桌面 BC：`PF_DXT1`×38 / `PF_BC5`×18 / `PF_DXT5`×2，2048²×36 与 1024²×22），**58/58 可解码**。缺的是导出器没接这条链：CUE4Parse-Conversion 1.2.2 的 glTF writer **只写材质名**，`baseColorTexture` / `pbrMetallicRoughness` 在 DLL 里 0 命中——即使 `exportall` 传 `ExportMaterials=true`，产出的 GLB 仍无 `images`/`textures` 键且 `pbrMetallicRoughness: {}`。**所以把 `exportmesh` 的 `ExportMaterials=false` 改成 `true` 并不能修好它**；要带贴图得在导出后加一遍 SharpGLTF 后处理（约 200–300 行，`SharpGLTF.Core` 已是依赖）。另外两点：`inspect` **看不到材质槽**（静态网格只列 `BodySetup`/`NavCollision`/`UStaticMesh` 三个对象），且玻璃/日式材质的参数名与石/木不同（前者 `BaseColorMap`/`MaskMap`，后者 `Base Texture`/`Normal Map`）。
- 带贴图导出的两个真正约束是**产物体积**（全量嵌入约 350 MB PNG 级，单栋 20–50 MB）与**游戏美术再分发**，属产品决策；另有未验证项：UV 集不在 `MI_*` 参数里（在父材质 `M_PalLit`），只能从 `TEXCOORD_0` 起试并对 `render/compare/` 校准。
- **`exporttex` 实测结果（2026-10-19，单件试水）**：`SM_Wall_Wood` 1 个材质槽 → `MI_PalProp_Wall_Wood`，`Base Texture`=`T_Wall_Wood_B`（2,488,005 B PNG）、`Normal Map`=`T_Wall_Wood_N`（6,260,277 B PNG），ok=1 fail=0。`_texmap.json` 只有一条：`{"MI_PalProp_Wall_Wood": "/out/textured/T_Wall_Wood_B.png"}`——键与 `meshes/BP_BuildObject_Wooden_Wall.glb` 的 `materials[].name` **精确相等**（导出器按材质对象名给 glTF 材质命名，不是按槽名，槽名实测是 `T_Wall_Wood`）。两条 PNG 实测为 RGBA 2048²，视觉上是真木纹图集，法线图是标准切空间紫底 + 木纹浮雕，**不是**空白/全黑/BC 块状伪影。
- **图集的「黑色」是不透明纯黑，不是透明**（2026-10-19 用 Pillow 逐通道实测修正）：`T_Wall_Wood_B.png` / `_N.png` 的 alpha 全为 255（0% 透明），未使用的 UV 岛是 RGB(0,0,0)。所以 UV 一旦偏，渲染出来是**黑斑**而不是透出背景。本件 `TEXCOORD_0` 落在 [0,1] 内且渲染无黑斑，说明 UV 是对的，但这条不能用「透明」去解释。
- **批量实测（2026-10-19，全量授权）**：`exporttex --all out/textured` → 17 网格 ok=17 fail=0；20 材质全部解析（含 2 个 `_meshrefs.json` 未覆盖的 GLB-only 材质）；每材质基色/法线/ORM 三图 **absent=0**、即 60 个通道槽全部存在；去重后 **54 张不同 PNG**（`MI_PalProp_Roof_Glass_Parts` 与 `MI_PropGlass_Roof_Glass` 共用 3 张，`MI_PalProp_Wall_Parts` 与 `MI_PropGlass_Wall_Glass` 共用 3 张），合计 **148,602,678 B（141.7 MB）**，耗时约 78 秒。两个 texmap 都是 20 键，与 17 个 GLB 的 `materials[].name` 并集**集合相等**（`True` / `True`）。
- **体积比先前估算小得多**：本条上上条曾估「全量约 350 MB」，实测 141.7 MB（约 2.4 倍差）。原因：ORM 走 1024² 且扁平、压缩率极高（每张 0.36–1.0 MB），而 2048² 只集中在基色与法线；另外 3 张 `ChangeColor Mask` 涂装遮罩不在三图范围内，故去重后是 54 张而非 58 张。
- **ORM 通道实测（只报数，不修）**：木/石族的 `_M` 图 **R 通道全为 0**，G 有值（木 mean 156/255，石 245/255），B 全范围变化；玻璃族 R/G/B 都有值。若 three.js 按 G 读 roughness、B 读 metalness，则金属度会取到 B 而非 UE 可能使用的 R——所以 `_texmap_all_orm.json` 有很大概率偏「过金属」。这正是 A/B 要看的，代码里不拆分通道。
- 两个坑（实测）：单件模式的 `_texmap.json` 仍是**字符串**形式且只含基色（保持 task-6 行为不变）；只有批量模式的 `_texmap_all*.json` 是**对象**形式。渲染页要用对象形式需 `render/modsrender.js` 侧支持（该文件 owner 是 lead，本页不改）。URL 是**真实**仓库根相对路径（`/out/textured/...`）：outDir 先 `Path.GetFullPath`，再判定它是否落在仓库根内，在根内才按实际相对位置生成，否则命令退 2 且不写盘——所以不会再出现绝对 outDir 退化成裸文件名、或相对 outDir 随 CWD 漂移的错 URL。渲染页按页面 URL 解析 texmap 值，而页面服务根是仓库根，这是该形式的原因。贴图文件名冲突时用 `SHA256(贴图对象路径)` 前 8 hex 做后缀（不是 `string.GetHashCode()`，后者每进程随机化），重跑同名、可复现。

-----

## Further Exploration

- [data/refresh.md](../../data/refresh.md) — 刷新流程 owner：7 张表的资源路径、四条命令的编排、验证与过期判断。
- [data/format.md](../../data/format.md) — 转出来的数据表字段语义与 catalog 格式。
- [tooling/README.md](../README.md) — 离线导出与校验，不需要游戏安装。
- [archive/README.md](../../archive/README.md) — 冻结历史：usmap 来源、引擎内渲染探针与两次事故记录。

-----

## Dev Note

<details>
<summary>维护者工作记录 — 点击展开</summary>

本页所有"已实测"结论来自 2026-10-19 本仓库的一次实测：`dotnet build` 0 错误 / 16 警告；`info` 从仓库根挂载 185141 个文件并 attach 3,312,408 字节 usmap；`list`/`inspect` 各跑一次；另外单独验证了 `PALWORLD_USMAP` 指向缺失文件时走 WARNING 分支、以及把工作目录换到 `C:\` 后 usmap 仍能通过程序目录向上搜索命中。

16 条警告的实际构成：6 条 NuGet 审计警告（`Microsoft.Bcl.Memory` 9.0.0 NU1903 高危 GHSA-73j8-2gch-69rq；`SixLabors.ImageSharp` 3.1.12 的 NU1903 高危 GHSA-j3p4-wp97-rph4 / GHSA-j9gm-c75j-xc9q / GHSA-jjfr-hcj7-qf5w 与 NU1902 中危 GHSA-gwg2-r3hj-4w44 / GHSA-wmxv-xphr-5c9g，均为传递依赖），加上 4 条代码警告（`Program.cs` 三处 CS0618 过时 API、一处 CS8604 在 `TextureEncoder.Encode`）。两处路径补丁没有引入新警告：警告条数与语义与补丁前基线一致，四条代码警告都落在未改动的代码上。新增 `exporttex` 后该基线不变（同为 16 条、同为那 4 条代码警告）。

未复现项：`dumptable`/`exporticons`/`exportmesh` 未运行（写盘，可能覆盖 `data/` 版本锁定数据，owner 为 [data/refresh.md](../../data/refresh.md) 的责任人 lead）；`convapi`/`whereis`/`dump` 等反射诊断命令未运行。`exportbps`/`exportall` 已于 2026-10-19 由 lead 授权实跑（只写 `meshes_new/`），`exporttex` 同日实跑（只写 `out/textured/`）。责任人：merge-author。

`exporttex` 追加实测（2026-10-19，新增子命令的试水）：加完 `case "exporttex"` 后 `dotnet build` 仍为 **0 错误 / 16 警告**——强制重建（`--no-incremental`）下 `Program.cs` 只有 4 条代码警告（168/247/609 三处 CS0618、320 一处 CS8604），全是插入前就存在的旧代码，新增分支贡献 0 条；第一版曾多出 2 条（CS8604 与 CS8602，`TextureDecoder.Decode` 返回可空与 `FStaticMaterial.MaterialInterface` 可空），已在 helper 里加空判后消除。运行只写 `out/textured/`（`T_Wall_Wood_B.png` 2,488,005 B、`T_Wall_Wood_N.png` 6,260,277 B、`_texmap.json` 65 B），未触碰 `data/`。视觉判读：`read_image` 两张 2048² RGBA——基色是真木纹图集（木纹板 + 倒角构件 + 圆形木节），法线是标准切空间紫底 + 木纹浮雕；无空白、无全黑、无 BC 块伪影。texmap 键与 GLB 材质名用脚本逐集合比对，结果 `EXACT SET MATCH = True`。未做：GLB 内嵌贴图、UV 集/平铺校正。责任人：asset-probe。

`exporttex --all` 追加实测（2026-10-19，全量授权，owner lead 授权 / asset-probe 执行）：批量分支加在同一 `case` 内，单件分支逐字未动（回归：同样命令输出 6 行、`_texmap.json` 内容与试水时一致，`ok=1 fail=0`）。构建仍 **0 错误 / 16 警告**，强制重建下 `Program.cs` 代码警告仍只有旧的那 4 条（行号因插入后移为 168/247/320/925），批量分支贡献 0 条。首次编译踩了两个纯语法坑并如实记录：批量与单件同处一个 `case` 作用域，`texDir`/`urlPrefix`/`baseParamNames`/`normalParamNames`/`EncodePng`/`sm`/`slots` 触发 CS0136（重名），批量侧统一改名解决；`ProcessMaterial` 最初用 `ref` 参数又在局部函数里捕获，改成返回元组。运行：17 网格 ok=17 fail=0、20 材质、60 个通道槽 absent=0、54 张去重 PNG、148,602,678 B（141.7 MB）、约 78 秒；两个 texmap 各 20 键且与 GLB 并集集合相等。`_meshrefs.json` 实际是 **16 键 / 15 个有网格 / 17 个网格对象**（任务书写的 18 键与实况不符），且 `meshes/` 有 **17 个 GLB**、其中 `Wood_DoorWall` / `Wood_WindowWall` 不在 `_meshrefs.json` 里——补了「GLB-only 按 `MI_*` 名反查」一轮才凑齐 20 材质，否则 texmap 会少 2 键。另用 Pillow 逐通道实测纠正了试水页的一条错判：图集黑色区域 alpha 全 255，是**不透明黑**不是透明。未做：GLB 内嵌贴图、UV 平铺校准、ORM 通道拆分（有意保留原样供 A/B）。责任人：asset-probe。

`exporttex` 契约修复（2026-10-20，cs-export-reviewer）：按 `out/workspace-review-2026-10-20.md`（未入库，属 `out/*` 忽略范围） 的 P1-6（texmap URL 前缀）/P1-7（单件短名去重）/P2-11（随机哈希）/P2-12（缺参、退出码、半截产物）修——texmap 前缀改为 `Path.GetFullPath` + 仓库根判定（根外打印 ERROR 并退 2、不写盘）；单件按贴图对象路径去重，同名不同包与批量同规则用 `SHA256(对象路径)` 前 8 hex 后缀；挂载前统一校验缺参/未知命令/非法数字选项（usage + 退 2）；`exporttex` 输入或导出失败退 1，批量写 `_failures.json`（`kind`/`context`/`object`/`reason`），无失败时删除旧文件；批量先校验 `_meshrefs.json` 再建输出目录。强制重建仍 **0 错误 / 16 警告**，4 条代码警告仍全在旧代码（行号后移为 286/365/438/1208），新增代码贡献 0 条。本机无游戏 pak，运行期用 `PALWORLD_PAKS` 指向空目录（CUE4Parse 挂载 0 文件、`info` 退 0）验证退出码与失败路径：缺参/未知命令/坏 `--limit` → usage + 2；`_meshrefs.json` 缺失、坏 JSON → 1 且输出目录不创建；outDir 绝对路径（仓库外）或 `..` 逃出 → 2 且不写盘；绝对路径在仓库内、相对路径从 `tooling/` 子目录运行 → 解析到实际位置并退 0；自造 2 条不存在网格的 refs → 写 `_failures.json`、退 1，随后空清单 `{}` 重跑 → 退 0 且 `_failures.json` 被删；单件未知对象 → 1、不建目录。未验证：真实 pak 下的贴图编码与 texmap URL 取值（`/out/...` 前缀）只能靠代码路径保证。责任人：cs-export-reviewer。

</details>
