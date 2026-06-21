using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace TrashPickupPlugin.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    // We give this window a hidden ID using ##.
    // The user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public MainWindow(Plugin plugin)
        : base("Trash Pickup", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("TrashPickupMainTabs"))
        {
            if (ImGui.BeginTabItem("Overview"))
            {
                ImGui.Text("Trash Pickup — Overview");
                ImGui.Separator();
                ImGui.Text("Open Inventory to see collected items.");
                ImGui.Text($"Ephemeral feature enabled: {plugin.Configuration.SomePropertyToBeSavedAndWithADefault}");

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Settings"))
            {
                ImGui.Text("Plugin Settings");
                ImGui.Separator();

                var configValue = plugin.Configuration.SomePropertyToBeSavedAndWithADefault;
                if (ImGui.Checkbox("Enable feature X", ref configValue))
                {
                    plugin.Configuration.SomePropertyToBeSavedAndWithADefault = configValue;
                    plugin.Configuration.Save();
                }

                var movable = plugin.Configuration.IsConfigWindowMovable;
                if (ImGui.Checkbox("Allow moving windows", ref movable))
                {
                    plugin.Configuration.IsConfigWindowMovable = movable;
                    plugin.Configuration.Save();
                }

                ImGui.Separator();
                ImGui.Text("Marker Settings");
                var size = plugin.Configuration.MarkerSize;
                if (ImGui.SliderFloat("Marker Size", ref size, 2f, 64f))
                {
                    plugin.Configuration.MarkerSize = size;
                    plugin.Configuration.Save();
                }

                var color = plugin.Configuration.MarkerColor;
                var a = (color >> 24) & 0xFF;
                var r = (color >> 16) & 0xFF;
                var g = (color >> 8) & 0xFF;
                var b = (color) & 0xFF;
                var fa = a / 255f; var fr = r / 255f; var fg = g / 255f; var fb = b / 255f;
                var col = new System.Numerics.Vector4(fr, fg, fb, fa);
                if (ImGui.ColorEdit4("Marker Color", ref col))
                {
                    var na = (uint)(col.W * 255) & 0xFF;
                    var nr = (uint)(col.X * 255) & 0xFF;
                    var ng = (uint)(col.Y * 255) & 0xFF;
                    var nb = (uint)(col.Z * 255) & 0xFF;
                    plugin.Configuration.MarkerColor = (na << 24) | (nr << 16) | (ng << 8) | nb;
                    plugin.Configuration.Save();
                }

                var los = plugin.Configuration.RequireLineOfSight;
                if (ImGui.Checkbox("Require line of sight to show/collect", ref los))
                {
                    plugin.Configuration.RequireLineOfSight = los;
                    plugin.Configuration.Save();
                }

                ImGui.Separator();
                ImGui.Text("Pickup / Visibility");
                var pickup = plugin.Configuration.PickupDistance;
                if (ImGui.SliderFloat("Pickup distance", ref pickup, 0.5f, 10f))
                {
                    plugin.Configuration.PickupDistance = pickup;
                    plugin.Configuration.Save();
                }

                var showDist = plugin.Configuration.MarkerShowDistance;
                if (ImGui.SliderFloat("Show marker within distance", ref showDist, 1f, 200f))
                {
                    plugin.Configuration.MarkerShowDistance = showDist;
                    plugin.Configuration.Save();
                }

                var maxActive = plugin.Configuration.MaxActivePerZone;
                if (ImGui.InputInt("Max active per zone", ref maxActive))
                {
                    if (maxActive < 1) maxActive = 1;
                    plugin.Configuration.MaxActivePerZone = maxActive;
                    plugin.Configuration.Save();
                }

                var spawnAmount = plugin.Configuration.SpawnAmountPerZone;
                if (ImGui.InputInt("Spawn amount per zone", ref spawnAmount))
                {
                    if (spawnAmount < 0) spawnAmount = 0;
                    plugin.Configuration.SpawnAmountPerZone = spawnAmount;
                    plugin.Configuration.Save();
                }

                var spawnRadius = plugin.Configuration.ZoneSpawnRadius;
                if (ImGui.SliderFloat("Spawn radius around player", ref spawnRadius, 50f, 500f))
                {
                    plugin.Configuration.ZoneSpawnRadius = spawnRadius;
                    plugin.Configuration.Save();
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Inventory"))
            {
                ImGui.Text("Inventory and management");
                ImGui.Separator();

                if (ImGui.Button("Open Inventory Window"))
                    plugin.ToggleInventoryUi();

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Debug"))
            {
                ImGui.Text("Debug Menu");
                ImGui.Separator();
                ImGui.Text($"Active trash count: {plugin.GetActiveCount()}");
                ImGui.Text($"Current zone definition: {plugin.GetCurrentZoneDefinitionPath()}");
                ImGui.Text($"Current mode: {plugin.Configuration.SomePropertyToBeSavedAndWithADefault}");
                ImGui.Text($"Max active per zone: {plugin.Configuration.MaxActivePerZone}");
                ImGui.Text($"Pickup distance: {plugin.Configuration.PickupDistance:0.00}");
                ImGui.Separator();

                using (var child = ImRaii.Child("DebugList", new Vector2(0, -ImGui.GetFrameHeightWithSpacing() * 3), true))
                {
                    if (child.Success)
                    {
                        foreach (var it in plugin.GetActiveTrash())
                        {
                            var info = plugin.GetTrashDebugInfo(it);
                            ImGui.Text($"{it.Name} - Collected:{it.Collected} - Dist:{info.distance:0.00} - Pos:({it.Position.X:0.0},{it.Position.Y:0.0},{it.Position.Z:0.0}) - Visible:{info.visible}");
                        }
                    }
                }

                ImGui.EndTabItem();
            }

            if (plugin.IsAdminUser() && ImGui.BeginTabItem("Admin"))
            {
                ImGui.Text("Admin Editor");
                ImGui.Separator();
                ImGui.Text("Only use this tab for zone mapping. This tab will be removed in the final version.");
                ImGui.Text($"Active trash count: {plugin.GetActiveCount()}");
                ImGui.Text($"Current zone definition: {plugin.GetCurrentZoneDefinitionPath()}");
                ImGui.Separator();

                if (ImGui.Button("Spawn Trash Piece at Player"))
                    plugin.SpawnTrashPieceAtPlayer();

                if (ImGui.Button("Delete Closest Trash Piece"))
                    plugin.DeleteClosestTrashPiece();

                ImGui.Separator();
                if (ImGui.Button("Export Zone JSON"))
                    plugin.ExportCurrentZoneDefinitions();

                ImGui.Separator();
                using (var child = ImRaii.Child("AdminList", new Vector2(0, -ImGui.GetFrameHeightWithSpacing() * 3), true))
                {
                    if (child.Success)
                    {
                        foreach (var it in plugin.GetActiveTrash())
                        {
                            var info = plugin.GetTrashDebugInfo(it);
                            ImGui.Text($"{it.Name} - Collected:{it.Collected} - Dist:{info.distance:0.00} - Pos:({it.Position.X:0.0},{it.Position.Y:0.0},{it.Position.Z:0.0}) - Visible:{info.visible}");
                        }
                    }
                }

                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }
}
