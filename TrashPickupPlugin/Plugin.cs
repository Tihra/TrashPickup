using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using TrashPickupPlugin.Extensions;
using TrashPickupPlugin.Services;
using TrashPickupPlugin.Windows;

namespace TrashPickupPlugin;

public sealed class Plugin : IDalamudPlugin
{
    //
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/trash";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("TrashPickupWindows");
    private MainWindow MainWindow { get; init; }
    public InventoryWindow InventoryWindow { get; init; }
    private int lastZoneId = -1;

    private readonly TrashService trashService;
    private readonly ZoneService zoneService;
    private readonly TrashDefinitionService trashDefinitionService;
    private readonly InventoryService inventoryService;
    private readonly NavMeshService navMeshService;

    private readonly IGameGui gameGui;
    private bool raycastAvailable = false;
    private bool localPlayerLookupLogged = false;

    private static string PluginDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Dalamud",
        "TrashPickupPlugin");

    public Plugin(IGameGui gameGui)
    {
        this.gameGui = gameGui;

        DetectRaycastSupport();

        Directory.CreateDirectory(PluginDataFolder);

        // initialize services
        trashService = new TrashService();
        zoneService = new ZoneService(PluginDataFolder);
        trashDefinitionService = new TrashDefinitionService(PluginDataFolder);
        navMeshService = new NavMeshService();

        // No initial sample item; spawns will be created on zone change or loaded from zone JSON

        // Inventory DB file stored in app-local plugin data folder
        var dbPath = Path.Combine(PluginDataFolder, "TrashPickupInventory.db");
        inventoryService = new InventoryService(dbPath);

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        MainWindow = new MainWindow(this);
        InventoryWindow = new InventoryWindow(inventoryService);

        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(InventoryWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the main window."
        });

        // Tell the UI system that we want our windows to be drawn through the window system
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.Draw += DrawUI;

        // Adds a button for opening the main UI
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information($"{PluginInterface.Manifest.Name} initialized.");
    }

    private void DetectRaycastSupport()
    {
        try
        {
            var found = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypesSafe())
                .Any(t => t.Name.IndexOf("Ray", StringComparison.OrdinalIgnoreCase) >= 0 && t.Name.IndexOf("Cast", StringComparison.OrdinalIgnoreCase) >= 0);
            raycastAvailable = found;
            if (!raycastAvailable)
                Log.Information("Raycast API not found in loaded assemblies; falling back to screen-visibility checks.");
            else
                Log.Information("Runtime raycast-like type detected; will attempt to use if available.");
        }
        catch (Exception ex)
        {
            raycastAvailable = false;
            Log.Warning($"Raycast detection failed: {ex.Message}");
        }
    }

    public (float distance, bool visible) GetTrashDebugInfo(TrashItem item)
    {
        var visible = gameGui.WorldToScreen(item.Position, out var screenPos);
        var dist = TryGetLocalPlayerPosition(out var p)
            ? (item.Position - p).Length()
            : 0f;

        // If raycast available, we would prefer to use it; currently fallback to WorldToScreen
        var los = visible;
        return (dist, los);
    }

    private bool TryGetLocalPlayerPosition(out System.Numerics.Vector3 pos)
    {
        pos = default;

        try
        {
            if (TryGetLocalPlayerGameObject(out var playerObject) && playerObject is not null && TryGetMapCoordinates(playerObject, out pos))
                return true;

            var directPlayer = ClientState != null
                ? GetMemberValue(ClientState, "LocalPlayer") ?? GetMemberValue(ClientState, "Player")
                : null;
            if (directPlayer is IGameObject directGameObject && TryGetMapCoordinates(directGameObject, out pos))
                return true;

            if (ClientState != null && TryGetPositionFromSource(ClientState, out pos))
                return true;

            if (PlayerState != null && TryGetPositionFromSource(PlayerState, out pos))
                return true;

            if (ClientState != null && TryScanSourceForPlayer(ClientState, out pos))
                return true;

            if (PlayerState != null && TryScanSourceForPlayer(PlayerState, out pos))
                return true;
        }
        catch { }

        LogLocalPlayerLookupFailure();
        return false;
    }

    private void LogLocalPlayerLookupFailure()
    {
        if (localPlayerLookupLogged)
            return;

        localPlayerLookupLogged = true;
        var clientType = ClientState?.GetType().Name ?? "null";
        var playerType = PlayerState?.GetType().Name ?? "null";
        Log.Warning($"Local player lookup failed. ClientState type={clientType}, PlayerState type={playerType}.");

        if (ClientState != null)
            Log.Warning(GetObjectMemberSummary(ClientState, "ClientState"));
        if (PlayerState != null)
            Log.Warning(GetObjectMemberSummary(PlayerState, "PlayerState"));
    }

    private static string GetObjectMemberSummary(object source, string label)
    {
        var type = source.GetType();
        var members = type.GetMembers(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        var names = members
            .Where(m => m.MemberType == System.Reflection.MemberTypes.Property || m.MemberType == System.Reflection.MemberTypes.Field)
            .Select(m => m.Name)
            .Distinct()
            .OrderBy(n => n)
            .Take(30);

        return $"{label} ({type.Name}) candidates: {string.Join(", ", names)}";
    }

    private static bool TryGetPositionFromSource(object source, out System.Numerics.Vector3 pos)
    {
        return TryGetPositionFromSourceRecursive(source, 0, out pos);
    }

    private static bool TryGetPositionFromSourceRecursive(object? source, int depth, out System.Numerics.Vector3 pos)
    {
        pos = default;
        if (source == null || depth > 4)
            return false;

        if (TryGetPositionFromObject(source, out pos))
            return true;

        var player = GetMemberValue(source, "LocalPlayer")
            ?? GetMemberValue(source, "Player")
            ?? GetMemberValue(source, "Me")
            ?? GetMemberValue(source, "CurrentPlayer");

        if (player != null && TryGetPositionFromObject(player, out pos))
            return true;

        if (TryGetInnerSource(source, out var innerSource))
            return TryGetPositionFromSourceRecursive(innerSource, depth + 1, out pos);

        return false;
    }

    private static bool TryGetInnerSource(object source, out object? innerSource)
    {
        innerSource = GetMemberValue(source, "Instance")
            ?? GetMemberValue(source, "clientStateService")
            ?? GetMemberValue(source, "playerStateService")
            ?? GetMemberValue(source, "playerState");

        return innerSource != null && !ReferenceEquals(innerSource, source);
    }

    private static bool TryScanSourceForPlayer(object source, out System.Numerics.Vector3 pos)
    {
        pos = default;
        var type = source.GetType();
        var members = type.GetMembers(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        foreach (var member in members)
        {
            if (!member.Name.Contains("player", StringComparison.OrdinalIgnoreCase)
                && !member.Name.Contains("me", StringComparison.OrdinalIgnoreCase)
                && !member.Name.Contains("instance", StringComparison.OrdinalIgnoreCase)
                && !member.Name.Contains("service", StringComparison.OrdinalIgnoreCase))
                continue;

            object? candidate = null;
            if (member is System.Reflection.PropertyInfo property)
            {
                candidate = property.GetValue(source);
            }
            else if (member is System.Reflection.FieldInfo field)
            {
                candidate = field.GetValue(source);
            }

            if (candidate != null && TryGetPositionFromObject(candidate, out pos))
                return true;

            if (candidate != null && TryGetInnerSource(candidate, out var innerSource) && TryGetPositionFromSourceRecursive(innerSource, 0, out pos))
                return true;
        }

        return false;
    }

    private static bool TryGetPositionFromObject(object source, out System.Numerics.Vector3 pos)
    {
        pos = default;
        if (source == null)
            return false;

        if (source is IGameObject gameObject)
        {
            if (TryGetMapCoordinates(gameObject, out pos))
                return true;
        }

        if (TryGetVector3FromObject(source, out pos))
            return true;

        var vectorPropertyNames = new[] { "Position", "WorldPosition", "Pos", "WorldPos", "CharacterPosition" };
        foreach (var name in vectorPropertyNames)
        {
            var candidate = GetMemberValue(source, name);
            if (candidate != null && TryGetVector3FromObject(candidate, out pos))
                return true;
        }

        return false;
    }

    private static bool TryGetLocalPlayerGameObject(out IGameObject? gameObject)
    {
        gameObject = null;
        if (ObjectTable != null)
        {
            try
            {
                var localPlayer = ObjectTable.LocalPlayer;
                if (localPlayer is IGameObject playerObject)
                {
                    gameObject = playerObject;
                    return true;
                }
            }
            catch { }
        }

        if (ClientState != null)
        {
            var localPlayer = GetMemberValue(ClientState, "LocalPlayer")
                ?? GetMemberValue(ClientState, "Player");
            if (localPlayer is IGameObject playerObject)
            {
                gameObject = playerObject;
                return true;
            }
        }

        if (PlayerState != null)
        {
            var localPlayer = GetMemberValue(PlayerState, "LocalPlayer")
                ?? GetMemberValue(PlayerState, "Player")
                ?? GetMemberValue(PlayerState, "Me");
            if (localPlayer is IGameObject playerObject)
            {
                gameObject = playerObject;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetMapCoordinates(IGameObject gameObject, out System.Numerics.Vector3 pos)
    {
        pos = default;
        try
        {
            pos = MapUtil.GetMapCoordinates(gameObject, true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryResolveSpawnPosition(System.Numerics.Vector3 inputPosition, out System.Numerics.Vector3 resolvedPosition)
    {
        resolvedPosition = inputPosition;
        if (!navMeshService.IsAvailable)
            return true;

        if (navMeshService.IsPointOnMesh(inputPosition))
            return true;

        if (navMeshService.FindNearestPoint(inputPosition, out var nearest) && (nearest - inputPosition).Length() <= 3.0f)
        {
            resolvedPosition = nearest;
            Log.Information($"NavMeshService adjusted spawn point from {inputPosition} to {nearest}.");
            return true;
        }

        return false;
    }

    private static object? GetMemberValue(object source, string name)
    {
        var type = source.GetType();
        var prop = type.GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.IgnoreCase);
        if (prop != null)
            return prop.GetValue(source);

        var field = type.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.IgnoreCase);
        if (field != null)
            return field.GetValue(source);

        return null;
    }

    private static bool TryGetVector3FromObject(object? obj, out System.Numerics.Vector3 vector)
    {
        vector = default;
        if (obj == null)
            return false;

        if (obj is System.Numerics.Vector3 v)
        {
            vector = v;
            return true;
        }

        var xProp = obj.GetType().GetProperty("X");
        var yProp = obj.GetType().GetProperty("Y");
        var zProp = obj.GetType().GetProperty("Z");
        if (xProp == null || yProp == null || zProp == null)
            return false;

        var xVal = xProp.GetValue(obj);
        var yVal = yProp.GetValue(obj);
        var zVal = zProp.GetValue(obj);
        if (xVal == null || yVal == null || zVal == null)
            return false;

        vector = new System.Numerics.Vector3(
            Convert.ToSingle(xVal),
            Convert.ToSingle(yVal),
            Convert.ToSingle(zVal));
        return true;
    }

    public System.Collections.Generic.IEnumerable<TrashItem> GetActiveTrash()
    {
        return trashService.Items;
    }

    public bool IsAdminUser()
    {
        var playerName = GetLocalPlayerName();
        return AdminWhitelist.IsWhitelisted(playerName);
    }

    public string GetLocalPlayerName()
    {
        try
        {
            if (PlayerState != null)
            {
                var nameProp = PlayerState.GetType().GetProperty("CharacterName");
                if (nameProp != null)
                {
                    var value = nameProp.GetValue(PlayerState) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }
            }

            var fallbackSource = (object?)PlayerState ?? ClientState;
            var fallback = fallbackSource != null
                ? GetMemberValue(fallbackSource, "CharacterName")
                    ?? GetMemberValue(fallbackSource, "Name")
                    ?? GetMemberValue(fallbackSource, "DisplayName")
                : null;

            if (fallback is string fallbackName && !string.IsNullOrWhiteSpace(fallbackName))
                return fallbackName.Trim();
        }
        catch { }

        return string.Empty;
    }

    public string GetCurrentZoneDefinitionPath()
    {
        if (!TryGetCurrentZoneIds(out var territoryType, out var mapId, out _))
            return "N/A";

        return trashDefinitionService.GetZoneFilePath(territoryType, mapId);
    }

    public void SpawnTrashPieceAtPlayer()
    {
        if (!TryGetLocalPlayerPosition(out var playerPos))
        {
            Log.Information("SpawnTrashPieceAtPlayer: no local player position available.");
            return;
        }

        var index = trashService.Items.Count + 1;
        var name = $"Trash_{index}";
        trashService.AddTrash(name, "Admin placed trash piece", playerPos);
        Log.Information($"SpawnTrashPieceAtPlayer: spawned {name} at {playerPos}.");
    }

    public void DeleteClosestTrashPiece()
    {
        if (!TryGetLocalPlayerPosition(out var playerPos))
        {
            Log.Information("DeleteClosestTrashPiece: no local player position available.");
            return;
        }

        var closest = trashService.Items
            .OrderBy(i => (i.Position - playerPos).LengthSquared())
            .FirstOrDefault();

        if (closest == null)
        {
            Log.Information("DeleteClosestTrashPiece: no active trash to delete.");
            return;
        }

        trashService.RemoveTrash(closest);
        Log.Information($"DeleteClosestTrashPiece: removed {closest.Name} at {closest.Position}.");
    }

    public void ExportCurrentZoneDefinitions()
    {
        if (!TryGetCurrentZoneIds(out var territoryType, out var mapId, out var zoneId))
        {
            Log.Information("ExportCurrentZoneDefinitions: current zone unavailable.");
            return;
        }

        var definitions = trashService.Items
            .Select(i => new TrashDefinition { Name = i.Name, Description = i.Description, Position = i.Position })
            .ToList();

        trashDefinitionService.SaveDefinitions(territoryType, mapId, definitions);
        var path = trashDefinitionService.GetZoneFilePath(territoryType, mapId);
        Log.Information($"Exported {definitions.Count} trash definitions for zone {zoneId} to {path}.");
    }

    private System.Collections.Generic.List<TrashDefinition> ValidateDefinitionsAgainstNavMesh(System.Collections.Generic.List<TrashDefinition> definitions)
    {
        if (!navMeshService.IsAvailable)
            return definitions;

        var validated = new System.Collections.Generic.List<TrashDefinition>();
        foreach (var definition in definitions)
        {
            if (TryResolveSpawnPosition(definition.Position, out var resolved))
            {
                validated.Add(new TrashDefinition
                {
                    Name = definition.Name,
                    Description = definition.Description,
                    Position = resolved
                });
            }
            else
            {
                Log.Information($"Navmesh rejected definition position {definition.Position} for {definition.Name}.\n");
            }
        }

        return validated;
    }

    private bool TryGetCurrentZoneIds(out uint territoryType, out uint mapId, out int zoneId)
    {
        territoryType = 0;
        mapId = 0;
        zoneId = 0;
        try { territoryType = ClientState.TerritoryType; zoneId = (int)territoryType; } catch { }
        try { mapId = ClientState.MapId; } catch { }
        return territoryType != 0 || mapId != 0;
    }

    private void LoadCurrentZoneDefinitions()
    {
        if (!TryGetCurrentZoneIds(out var territoryType, out var mapId, out var zoneId))
        {
            Log.Information("LoadCurrentZoneDefinitions: unable to resolve current zone.");
            trashService.Clear();
            return;
        }

        var definitions = trashDefinitionService.LoadDefinitions(territoryType, mapId);
        trashService.Clear();

        if (definitions.Count == 0)
        {
            Log.Information($"No trash definition file found for territory {territoryType}, map {mapId}. No trash spawned.");
            return;
        }

        var validDefinitions = ValidateDefinitionsAgainstNavMesh(definitions);
        if (validDefinitions.Count == 0)
        {
            Log.Information($"Loaded definitions for territory {territoryType}, map {mapId}, but no valid navmesh positions were found. No trash spawned.");
            return;
        }

        trashService.SpawnFromDefinitions(validDefinitions);
        Log.Information($"Loaded {validDefinitions.Count} trash items from definitions for territory {territoryType}, map {mapId}.");
    }

    public void Dispose()
    {
        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        
        WindowSystem.RemoveAllWindows();

        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
        inventoryService?.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        var a = (args ?? string.Empty).Trim();
        if (string.Equals(a, "spawn", StringComparison.OrdinalIgnoreCase))
        {
            SpawnNow();
            return;
        }

        if (string.Equals(a, "clearzone", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var zid = (int)ClientState.TerritoryType;
                zoneService.MarkZoneCleared(zid);
                Log.Information($"Marked zone {zid} cleared via command.");
            }
            catch { }
            return;
        }

        // default: toggle main UI
        MainWindow.Toggle();
    }

    public void SpawnNow()
    {
        try
        {
            var zoneId = 0;
            try { zoneId = (int)ClientState.TerritoryType; } catch { zoneId = 0; }
            if (zoneService.IsZoneCleared(zoneId))
            {
                Log.Information($"SpawnNow: zone {zoneId} is already cleared; skipping.");
                return;
            }

            if (!TryGetCurrentZoneIds(out var territoryType, out var mapId, out _))
            {
                Log.Information("SpawnNow: current zone unavailable; skipping spawn.");
                return;
            }

            if (!trashDefinitionService.HasDefinitions(territoryType, mapId))
            {
                Log.Information($"SpawnNow: no zone JSON for territory {territoryType}, map {mapId}; skipping spawn.");
                return;
            }

            LoadCurrentZoneDefinitions();
        }
        catch (Exception ex)
        {
            Log.Warning($"SpawnNow failed: {ex.Message}");
        }
    }

    public void ClearSpawns()
    {
        try
        {
            trashService.Clear();
            Log.Information("ClearSpawns: cleared ephemeral spawns.");
        }
        catch (Exception ex)
        {
            Log.Warning($"ClearSpawns failed: {ex.Message}");
        }
    }

    public int GetActiveCount() => trashService.Items?.Count ?? 0;

    private void DrawUI()
    {
        // Spawn zone trash when entering a new zone
        try
        {
            var zoneId = 0;
            try { zoneId = (int)ClientState.TerritoryType; } catch { zoneId = 0; }
            if (zoneId != lastZoneId)
            {
                lastZoneId = zoneId;
                try
                {
                    if (zoneService.IsZoneCleared(zoneId))
                    {
                        Log.Information($"Zone {zoneId} is marked cleared; skipping spawn.");
                        trashService.Clear();
                    }
                    else
                    {
                        LoadCurrentZoneDefinitions();
                    }
                }
                catch (Exception ex) { Log.Warning($"SpawnOnZoneChange failed: {ex.Message}"); }
            }
        }
        catch { }
        // Attempt to collect nearby trash around the player each frame
            try
            {
                if (TryGetLocalPlayerPosition(out var p))
                {
                    var newly = trashService.CollectNearbyTrash(p, Configuration.PickupDistance);
                    if (newly != null && newly.Count > 0)
                    {
                        var zoneId = 0;
                        try { zoneId = (int)ClientState.TerritoryType; } catch { zoneId = 0; }
                        foreach (var it in newly)
                        {
                            // If LOS is required but item is not visible, unmark it and skip persistence
                            var visible = gameGui.WorldToScreen(it.Position, out var _);
                            if (Configuration.RequireLineOfSight && !visible)
                            {
                                it.Collected = false;
                                continue;
                            }
                            var dto = new CollectedItem
                            {
                                Name = it.Name,
                                Description = it.Description,
                                PosX = it.Position.X,
                                PosY = it.Position.Y,
                                PosZ = it.Position.Z,
                                ZoneId = zoneId,
                                CollectedAt = DateTime.UtcNow
                            };

                            inventoryService.AddCollectedItem(dto);
                        }
                        // If all active items in this zone are now collected, mark zone cleared
                        var allCollected = true;
                        foreach (var ai in trashService.Items)
                        {
                            if (!ai.Collected)
                            {
                                allCollected = false;
                                break;
                            }
                        }

                        if (allCollected)
                        {
                            try { zoneService.MarkZoneCleared(zoneId); } catch { }
                        }
                    }
                }
            }
            catch { }

        DrawTrash();
        WindowSystem.Draw();
    }

    public void ToggleMainUi() => MainWindow.Toggle();
    public void ToggleInventoryUi() => InventoryWindow.Toggle();

    private void DrawTrash()
    {
        // get player position for distance checks
        if (!TryGetLocalPlayerPosition(out var playerPos))
            return;

        foreach (var item in trashService.Items)
        {
            if (item.Collected)
                continue;

            var visible = gameGui.WorldToScreen(item.Position, out var screenPos);
            // if LOS is required, only draw when worldToScreen reports visible
            if (Configuration.RequireLineOfSight && !visible)
                continue;


            // only show marker when player is within configured show distance
            var dist = (item.Position - playerPos).Length();
            if (dist > Configuration.MarkerShowDistance)
                continue;

            if (visible)
            {
                var size = Configuration.MarkerSize;
                var color = Configuration.MarkerColor;
                ImGui.GetForegroundDrawList()
                    .AddCircleFilled(screenPos, size, color);
            }
        }
    }
}

