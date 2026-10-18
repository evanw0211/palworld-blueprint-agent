-- Read-only probe: which BuildObjectDataTable row names exist, and what MapObjectId /
-- blueprint class they point at. Mirrors BlueprintSelectionData.resolve_blueprint_build_class().
local candidates = {
    "Wooden_foundation", "Wooden_Foundation", "Wooden_wall", "Wooden_Wall",
    "Wooden_DoorWall", "Wooden_roof", "Wooden_stair", "Wooden_Pillar",
    "Stone_Foundation", "Stone_foundation", "Stone_wall", "Stone_DoorWall",
    "Wood_WindowWall", "Wood_SlantedRoof", "Wood_TriangleWall",
    "JapaneseStyle_foundation", "JapaneseStyle_wall_01", "JapaneseStyle_DoorWall_02",
}
local result = {}
local ok_world, world = pcall(UEHelpers.GetWorld)
if not ok_world or world == nil then return { error = "world unavailable" } end
local ok_util, util = pcall(StaticFindObject, "/Script/Pal.Default__PalMasterDataTablesUtility")
if not ok_util or util == nil then return { error = "PalMasterDataTablesUtility unavailable" } end

local ok_build, build_table = pcall(function() return util:GetBuildObjectDataTable(world) end)
if not ok_build or build_table == nil then
    return { error = "build table unavailable: " .. tostring(build_table) }
end
local ok_map, map_table = pcall(function() return util:GetMapObjectDataTable(world) end)
if not ok_map then map_table = nil end

local function fname_to_string(value)
    if value == nil then return nil end
    local ok, s = pcall(function() return value:ToString() end)
    if ok and type(s) == "string" then return s end
    return tostring(value)
end

for _, id in ipairs(candidates) do
    local entry = {}
    local ok, row = pcall(function() return build_table:FindRow(id) end)
    if not ok then
        entry = { findFailed = tostring(row) }
    elseif row == nil then
        entry = { found = false }
    else
        entry.found = true
        local ok_mapid, map_id = pcall(function() return row.MapObjectId end)
        if ok_mapid and map_id ~= nil then
            entry.mapObjectId = fname_to_string(map_id)
        end
        local ok_type, type_a = pcall(function() return row.TypeA end)
        if ok_type and type_a ~= nil then
            entry.typeA = fname_to_string(type_a)
        end
        if map_table ~= nil and entry.mapObjectId ~= nil then
            local ok_row2, map_row = pcall(function() return map_table:FindRow(entry.mapObjectId) end)
            if ok_row2 and map_row ~= nil then
                local ok_soft, soft = pcall(function() return map_row.BlueprintClassSoft end)
                if ok_soft and soft ~= nil then
                    local ok_path, path = pcall(function()
                        return soft:GetObjectID():GetAssetPathName()
                    end)
                    if ok_path then entry.blueprint = fname_to_string(path) end
                end
            end
        end
    end
    result[id] = entry
end
return result
