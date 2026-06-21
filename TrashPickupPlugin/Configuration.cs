using Dalamud.Configuration;
using System;

namespace TrashPickupPlugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool SomePropertyToBeSavedAndWithADefault { get; set; } = true;

    // Marker settings
    public float MarkerSize { get; set; } = 10.0f;
    public uint MarkerColor { get; set; } = 0xFF00FF00; // ARGB
    public bool RequireLineOfSight { get; set; } = true;
    
    // Pickup and visibility
    public float PickupDistance { get; set; } = 2.0f;
    // Distance within which markers will be shown (player must be this close)
    public float MarkerShowDistance { get; set; } = 50.0f;
    public int MaxActivePerZone { get; set; } = 20;
    // How many pieces to spawn when entering a new zone
    public int SpawnAmountPerZone { get; set; } = 20;
    public float ZoneSpawnRadius { get; set; } = 250f;

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
