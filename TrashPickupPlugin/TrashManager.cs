using System.Collections.Generic;
using System.Numerics;

namespace TrashPickupPlugin;

public class TrashManager
{
    public List<TrashItem> Items { get; } = new();

    public void AddTrash(string name, string desc, Vector3 position)
    {
        Items.Add(new TrashItem(name,desc,position));
    }

    public void CollectNearbyTrash(Vector3 playerPosition, float radius = 2f)
    {
        foreach (var item in Items)
        {
            if (item.Collected)
                continue;

            if (Vector3.Distance(playerPosition, item.Position) <= radius)
            {
                item.Collected = true;
            }
        }
    }
}
