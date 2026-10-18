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

不带任何参数运行只打印 usage 并退出 0，不会挂载 pak。每个子命令的参数形态在小节 [运行](#运行)，完整分派逻辑的 owner 是 [Program.cs](Program.cs) 的 `switch (args[0])`。

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

`info` 成功退出 0；`list` 末行是 `-- <n> match(es), showing <m>`，无匹配也退 0。前置缺失时不是非零退出加错误码，而是下面两种可辨认的信号之一：usmap 找不到会打印 `[map] WARNING: usmap missing -- cooked packages will fail to deserialize` 并继续（退出 0）；pak 目录找不到会让 CUE4Parse 抛 `DirectoryNotFoundException`，进程以 0xE0434352 结束。

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
| `exportbps` | `<布局JSON> <outDir>` | 蓝图类 → StaticMesh 引用清单 `<outDir>\_meshrefs.json` | 未实测 |
| `exportall` | `<meshrefsJSON> <outDir>` | 每个引用导 glb，并写 `<outDir>\_parts.json` | 未实测 |

~~~powershell
# 已实测：pak 里有哪些 DT_BuildObjectDataTable 资源
dotnet run --project tooling/PalAssetExport -- list DT_BuildObjectDataTable --limit 5
# 已实测：某蓝图类里有什么对象（不带第二个参数即只读）
dotnet run --project tooling/PalAssetExport -- inspect Pal/Content/Pal/Blueprint/MapObject/BuildObject/BP_BuildObject_Stone_Foundation
~~~

`list` 实测输出 4 条 `DT_BuildObjectDataTable*` 路径、末行 `-- 4 match(es), showing 4`；`inspect` 实测输出 `20 object(s) in ...` 与对象列表。

五条写盘命令在本仓库未实测：它们会覆盖 `data/` 的版本锁定数据，执行与验证的 owner 是 [data/refresh.md](../../data/refresh.md)（该页写明责任人 lead）。7 个表的资源路径也由那一页维护，本页不复制。

重复运行 `dotnet run` 会重印 NuGet 警告；构建过一次后加 `--no-build`，或直接跑 `tooling\PalAssetExport\bin\Debug\net10.0\PalAssetExport.exe`，输出就只剩程序自己的行。

-----

## 失败与恢复

下面每行都是本仓库实际观察到的现象；未复现的失败模式不列。

| 现象 | 原因 | 诊断 | 处理 |
|---|---|---|---|
| `Unhandled exception. System.IO.DirectoryNotFoundException: The game directory could not be found.`，退出码 0xE0434352（Windows 记作 `-532462766`） | `PALWORLD_PAKS` 或默认路径下没有 pak 目录 | `Test-Path "$env:PALWORLD_PAKS"` / 对照默认值 | 改成真实 pak 目录，或清掉该变量用默认值 |
| `[map] WARNING: usmap missing -- cooked packages will fail to deserialize`，退出 0 | `PALWORLD_USMAP` 指向不存在的文件，或仓库里 `tooling/mappings/Palworld.usmap` 被删 | 看这行是否出现；`Get-Item tooling\mappings\Palworld.usmap` | 修正变量或恢复 usmap；`list`/`info` 仍可用，`dumptable`/`inspect` 会失败 |
| `unknown command: <名字>`，退出 0 | 子命令拼错 | 对照 [运行](#运行) 的命令表 | 改正；不传参数运行可打印 usage |
| 每行输出前有 `warning NU19xx` | `dotnet run` 每次重建并重印 NuGet 审计警告 | 看是否为 `PalAssetExport.csproj` 前缀 | 加 `--no-build`，或直接跑已构建的 exe |

-----

## 边界与安全

它只读 pak 与 usmap，不启动游戏进程、不写游戏安装目录、不需要任何凭据。写盘目标完全由第二个参数决定：`dumptable` 覆盖同名 JSON，`exporticons`/`exportmesh`/`exportbps`/`exportall` 在指定目录下新建或覆盖文件。因此把输出目录指到 `data/` 就会直接改写版本锁定数据——这正是本仓库不跑写盘命令的原因。构建依赖从本机 NuGet 缓存解析（实测离线成功），程序本身不发起网络请求。

-----

## 了解实现

<details>
<summary>实现内部 — 点击展开</summary>

一个 600 行的顶层语句程序，没有类与项目结构：先处理 `findapi`/`probe`/`mapapi` 等反射诊断命令，再构造并挂载 `DefaultFileProvider`，然后按 `args[0]` 分派。挂载之后才附加 usmap——CUE4Parse 的 mappings provider 挂在 file provider 的 `MappingsContainer` 上，而不是 `VersionContainer`；`Initialize()` 只注册 VFS，`Mount()` 才真正读 pak 索引（源码首部注释记下了这个坑）。各子命令自己读参数、自己写盘，没有统一的输出目录或退出码封装，所以参数错误不一定反映到退出码上。

| 区域 | 源 | 职责 |
|---|---|---|
| 命令分派与全部子命令 | [`Program.cs`](Program.cs) | 解析参数、挂载 pak、附加 usmap、执行 `list`/`info`/`inspect`/`dumptable`/`exporticons`/`exportmesh`/`exportbps`/`exportall` 与反射诊断 |
| 依赖清单 | [`PalAssetExport.csproj`](PalAssetExport.csproj) | `net10.0`、`Nullable`+`ImplicitUsings`、CUE4Parse 与 CUE4Parse-Conversion 1.2.2.202610 |
| 类型映射 | [`../mappings/Palworld.usmap`](../mappings/Palworld.usmap) | 3.2 MB 二进制 usmap，cooked 包的反序列化依据 |

</details>

-----

## 限制

- 五条写盘命令在本仓库未实测——刷新前必须先读 [data/refresh.md](../../data/refresh.md)，不要凭本页直接执行。
- [data/refresh.md](../../data/refresh.md) 的第 1 段仍写「第 1 段在原工作区 `tools/PalAssetExport`」；本仓库已带同一工具（本页），是否改写由该页 owner 决定。
- 没有 `--help`；参数形态只能读源码或本页的命令表。
- 退出码不可当门禁：`unknown command` 与 usmap 缺失都退 0，只有 pak 目录缺失会以异常终止。
- 反射诊断命令（`findapi`/`probe`/`mapapi`/`convapi`/`whereis`/`dump`）是当时的排障脚本，不保证随依赖升级仍可用；本页未实测。
- `exportmesh`/`exportbps`/`exportall` 只导出几何与引用清单，不导贴图、不组装蓝图，也不做任何放置可行性判断。

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

16 条警告的实际构成：6 条 NuGet 审计警告（`Microsoft.Bcl.Memory` 9.0.0 NU1903 高危 GHSA-73j8-2gch-69rq；`SixLabors.ImageSharp` 3.1.12 的 NU1903 高危 GHSA-j3p4-wp97-rph4 / GHSA-j9gm-c75j-xc9q / GHSA-jjfr-hcj7-qf5w 与 NU1902 中危 GHSA-gwg2-r3hj-4w44 / GHSA-wmxv-xphr-5c9g，均为传递依赖），加上 4 条代码警告（`Program.cs` 三处 CS0618 过时 API、一处 CS8604 在 `TextureEncoder.Encode`）。两处路径补丁没有引入新警告：警告条数与语义与补丁前基线一致，四条代码警告都落在未改动的代码上。

未复现项：`dumptable`/`exporticons`/`exportmesh`/`exportbps`/`exportall` 全部未运行（写盘，可能覆盖 `data/` 版本锁定数据，owner 为 [data/refresh.md](../../data/refresh.md) 的责任人 lead）；`convapi`/`whereis`/`dump` 等反射诊断命令未运行。责任人：merge-author。

</details>
