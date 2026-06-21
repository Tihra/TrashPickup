using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace TrashPickupPlugin.Services;

public class ZoneState
{
    public List<int> ClearedZones { get; set; } = new();
}
public class ZoneService
{
    private readonly string statePath;
    private readonly ZoneState state = new();

    public ZoneService(string pluginDataFolder)
    {
        statePath = Path.Combine(pluginDataFolder, "zone_state.json");
        Directory.CreateDirectory(pluginDataFolder);
        LoadState();
    }

    private void LoadState()
    {
        try
        {
            if (File.Exists(statePath))
            {
                var txt = File.ReadAllText(statePath);
                var s = JsonSerializer.Deserialize<ZoneState>(txt);
                if (s != null)
                {
                    state.ClearedZones = s.ClearedZones ?? new List<int>();
                }
            }
        }
        catch { }
    }

    private void SaveState()
    {
        try
        {
            var txt = JsonSerializer.Serialize(state);
            File.WriteAllText(statePath, txt);
        }
        catch { }
    }

    public bool IsZoneCleared(int zoneId)
    {
        return state.ClearedZones.Contains(zoneId);
    }

    public void MarkZoneCleared(int zoneId)
    {
        if (!state.ClearedZones.Contains(zoneId))
        {
            state.ClearedZones.Add(zoneId);
            SaveState();
        }
    }

    public void UnmarkZoneCleared(int zoneId)
    {
        if (state.ClearedZones.Remove(zoneId))
            SaveState();
    }
}
