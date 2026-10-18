---
description: "游戏或 mod 更新后重生成 data/ 快照：用本仓库自带的 pak 导出工具重跑 7 张表、图标与拼图，并判断快照是否过期。"
kind: "how-to"
---

# 刷新 data/ 快照

## 摘要

游戏或 mod 更新后，按本页把 `data/` 快照整体重生成，让 catalog、图标与拼图重新对齐当前版本。工具已在本仓库内：`tooling/PalAssetExport`（.NET 10，C# 源码）与 `tooling/mappings/Palworld.usmap`，不再依赖原工作区。你需要本机安装 Palworld 与该 mod、.NET 10 SDK；`make_icon_sheets.py` 还需要 Pillow。边界：除构建外每条命令都会覆盖版本锁定数据，两条 pak 导出命令未在本仓库实跑，执行前先确认版本。

## 目录

- [开始前](#开始前)
- [刷新步骤](#刷新步骤)
- [验证](#验证)
- [失败](#失败)
- [Dev Note](#dev-note)

-----

## 开始前

- 工具：本仓库自带 `tooling/PalAssetExport`（C# 源码 + `PalAssetExport.csproj`）与数据映射 `tooling/mappings/Palworld.usmap`（3.2 MB）；CLI 契约见 [../tooling/PalAssetExport/README.md](../tooling/PalAssetExport/README.md)。
- 环境：`dotnet` 10.0.402（实测）；Python 与 Pillow（`data/make_icon_sheets.py` 需要 `from PIL import Image`，本机当前 Python 未装 Pillow，实测报 `ModuleNotFoundError: No module named 'PIL'`）。
- 版本戳输入：`D:\SteamLibrary\steamapps\appmanifest_1623730.acf` 与工坊 `...\workshop\content\1623730\3768908799\Info.json`（两者实测存在），`data/build_catalog.py` 读它们判断版本。
- 路径覆盖：游戏安装目录缺省 `D:\SteamLibrary\steamapps\common\Palworld\Pal\Content\Paks`，可用环境变量 `PALWORLD_PAKS` 覆盖；usmap 先读 `PALWORLD_USMAP`，否则从当前目录与程序目录向上逐级找 `tooling/mappings/Palworld.usmap`（两个覆盖均已实测）。缺 usmap 时工具打印 `[map] WARNING: usmap missing -- cooked packages will fail to deserialize`，cooked 包解析会失败。
- 已实测：`dotnet build tooling/PalAssetExport` 在本仓库 0 错误（有 NuGet 审计与代码警告，明细与构成见 [PalAssetExport/README.md](../tooling/PalAssetExport/README.md)）；`dotnet run --project tooling/PalAssetExport -- info` 冒烟通过（usmap 已挂载、pak 文件列表可读）。

## 刷新步骤

`<ws>` = 本仓库根；以下命令都在仓库根执行。除第一条 build 外都会写 `data/` 下的版本锁定数据（`raw/`、`icons/`、catalog 与 `sheets/`），确认要刷新再跑。

```powershell
dotnet build tooling/PalAssetExport
dotnet run --project tooling/PalAssetExport -- dumptable <资源路径> data/raw/<输出名>   # 7 次
python data/build_catalog.py
dotnet run --project tooling/PalAssetExport -- exporticons data/raw/DT_BuildObjectIconDataTable_Common.json data/icons
python data/make_icon_sheets.py
```

`R`=`/Game/Pal/DataTable/MapObject/`，`T`=`/Game/L10N/{en,zh-Hans}/Pal/DataTable/Text/`；输出名与 `data/raw/` 现有 7 个文件一致：

|资源路径|输出名|
|---|---|
|`R/Building/DT_BuildObjectDataTable_Common`|`DT_BuildObjectDataTable_Common.json`|
|`R/Building/DT_BuildObjectIconDataTable_Common`|`DT_BuildObjectIconDataTable_Common.json`|
|`R/DT_MapObjectMasterDataTable_Common`|`DT_MapObjectMasterDataTable_Common.json`|
|`T/DT_MapObjectNameText_Common` (en)|`DT_MapObjectNameText_en.json`|
|`T/DT_MapObjectNameText_Common` (zh-Hans)|`DT_MapObjectNameText_zh.json`|
|`T/DT_BuildObjectCategoryText` (en)|`DT_BuildObjectCategoryText_en.json`|
|`T/DT_BuildObjectCategoryText` (zh-Hans)|`DT_BuildObjectCategoryText_zh.json`|

`dumptable` / `exporticons` 的参数形态已对 [Program.cs](../tooling/PalAssetExport/Program.cs) 的 args 解析核对（`dumptable <assetPath> <outJson>`、`exporticons <tableJson> <outDir> [limit]`），但**未在本仓库实跑**：它们会改写版本锁定数据，禁止为验证而运行。验证责任人：仓库维护者。

## 验证

跑 `python -c "import sys;sys.path.insert(0,'data');import build_catalog as b;print(b.version_stamp()['steam_buildid'])"`，输出与 `catalog_summary.md` 的 `game buildid` 行（第 5 行）相同即成功（本仓库当前一致，已实测）；不同即过期。

## 失败

| 现象 | 处理 |
|---|---|
| `raw dumps missing … dumptable first` | 缺表，补 dumptable 段 |
| `[map] WARNING: usmap missing -- cooked packages will fail to deserialize` | 设 `PALWORLD_USMAP`，或确认 `tooling/mappings/Palworld.usmap` 在仓库内 |
| `ModuleNotFoundError: No module named 'PIL'` | 装 Pillow，再跑 `make_icon_sheets.py`（本机当前未装） |
| `Unhandled exception. System.IO.DirectoryNotFoundException`（退出码 `0xE0434352`） | 核对 `PALWORLD_PAKS` 与默认路径，改成真实 pak 目录后重试 |
| `dotnet` 命令找不到 | 装 .NET 10 SDK |

## Dev Note

第 1 段的 `dotnet build` 与「开始前」的 `-- info` 冒烟由本页作者实测；dumptable / exporticons 未运行（需 pak 且会覆盖版本锁定数据），命令来自 `Program.cs`。其余验证责任人：仓库维护者（需游戏与 mod 安装）。预算：正文由 2.75 KB 上调至约 5.0 KB，增量来自环境变量覆盖与缺 usmap 的 WARNING、build/info 实测证据、三种失败的恢复路径，都是刷新流程的决策信息。
