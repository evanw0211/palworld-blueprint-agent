---
description: "游戏或 mod 更新后重生成 data/ 快照：7 张表映射、步骤、验证与过期判断。"
kind: "how-to"
---

# 刷新 data/ 快照

## 摘要

游戏或 mod 更新后，按本页把 `data/` 快照整体重生成，让 catalog、图标与拼图重新对齐当前版本。你需要本机有 Palworld、该 mod、`dotnet` 与 Pillow，并在原工作区保留 `tools/PalAssetExport`。边界：两条 pak 命令未在本仓库复现，命令形态取自原工作区源码。

## 目录

- [开始前](#开始前)
- [刷新步骤](#刷新步骤)
- [验证](#验证)
- [失败](#失败)
- [Dev Note](#dev-note)

-----

## 开始前

- 原工作区 `C:\Users\25849\Documents\deepseek-harness\帕鲁` 内有 `tools/PalAssetExport`；`dotnet` 10.0.402；`D:\SteamLibrary` 下有游戏/mod，`steamapps\appmanifest_1623730.acf` 与工坊 `3768908799\Info.json` 是版本戳输入。
- 脚本以 `HERE` 为根（读源码确认）；完整重跑未执行（会覆盖生成文件），责任人 lead。

## 刷新步骤

`<ws>` = 本仓库根；第 1、3 段在原工作区，第 2、4 段在本仓库。

```powershell
dotnet run --project tools/PalAssetExport -- dumptable <资源路径> <ws>\data\raw\<输出名> # 1) 7 次
python <ws>\data\build_catalog.py
dotnet run --project tools/PalAssetExport -- exporticons <ws>\data\raw\<图标表输出名> <ws>\data\icons
python <ws>\data\make_icon_sheets.py
```

`R`=`/Game/Pal/DataTable/MapObject/`，`T`=`/Game/L10N/{en,zh-Hans}/Pal/DataTable/Text/`；输出名同 `data/raw/` 现有 7 个文件：

|资源路径|输出名|
|---|---|
|`R/Building/DT_BuildObjectDataTable_Common`|`DT_BuildObjectDataTable_Common.json`|
|`R/Building/DT_BuildObjectIconDataTable_Common`|`DT_BuildObjectIconDataTable_Common.json`|
|`R/DT_MapObjectMasterDataTable_Common`|`DT_MapObjectMasterDataTable_Common.json`|
|`T/DT_MapObjectNameText_Common` (en)|`DT_MapObjectNameText_en.json`|
|`T/DT_MapObjectNameText_Common` (zh-Hans)|`DT_MapObjectNameText_zh.json`|
|`T/DT_BuildObjectCategoryText` (en)|`DT_BuildObjectCategoryText_en.json`|
|`T/DT_BuildObjectCategoryText` (zh-Hans)|`DT_BuildObjectCategoryText_zh.json`|

## 验证

跑 `python -c "import sys;sys.path.insert(0,'data');import build_catalog as b;print(b.version_stamp()['steam_buildid'])"`，输出与 `catalog_summary.md` 的 `game buildid` 行（第 5 行）相同即成功（本仓库当前一致，已实测）；不同即过期。

## 失败

| 现象 | 处理 |
|---|---|
| `raw dumps missing … dumptable first` | 缺表，补第 1 段 |
| `ModuleNotFoundError: No module named 'PIL'` | 装 Pillow，重跑第 4 段 |

## Dev Note

第 1、3 段未运行（需 pak），命令来自 `Program.cs`。责任人：lead。