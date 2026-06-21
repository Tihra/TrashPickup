using System.Numerics;

namespace TrashPickupPlugin;

public class TrashItem
{
    public string Name { get; set; }
    
    public string Description { get; set; }

    public Vector3 Position { get; set; }

    public bool Collected { get; set; }

    public TrashItem(string name, string desc, Vector3 position)
    {
        Name = name;
        Description = desc;
        Position = position;
        Collected = false;
    }
}
