using System.Numerics;

namespace TrashPickupPlugin.Services;

public class TrashDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Vector3 Position { get; set; }
}
