using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using TrashPickupPlugin.Services;

namespace TrashPickupPlugin.Windows;

public class InventoryWindow : Window, IDisposable
{
    private readonly InventoryService inventoryService;
    private int zoneFilter = -1;
    private List<CollectedItem> cached = new();

    public InventoryWindow(InventoryService inventoryService) : base("Trash Inventory")
    {
        this.inventoryService = inventoryService;
        Size = new Vector2(500, 400);
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.Text("Collected items:");
        ImGui.Separator();

        ImGui.PushItemWidth(120);
        ImGui.InputInt("Zone filter (ID)", ref zoneFilter);
        ImGui.SameLine();
        if (ImGui.Button("Refresh"))
        {
            cached = inventoryService.GetCollectedItems();
        }
        ImGui.PopItemWidth();

        if (cached.Count == 0)
            cached = inventoryService.GetCollectedItems();

        using var child = ImRaii.Child("InventoryList", new Vector2(0, -ImGui.GetFrameHeightWithSpacing() * 2), true);
        if (child.Success)
        {
            foreach (var it in cached)
            {
                if (zoneFilter >= 0 && it.ZoneId != zoneFilter)
                    continue;

                ImGui.Text($"{it.Id}: {it.Name} (Zone {it.ZoneId}) — {it.CollectedAt.ToLocalTime():g}");
            }
        }
    }
}
