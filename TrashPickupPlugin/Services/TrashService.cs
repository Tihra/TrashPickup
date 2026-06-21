using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;

namespace TrashPickupPlugin.Services;

public class TrashService
{
    private readonly List<TrashItem> items = new();

    public IReadOnlyList<TrashItem> Items => items;

    public TrashService()
    {
    }

    public void Clear()
    {
        items.Clear();
    }

    public void SpawnAround(Vector3 center, int count = 20, float radius = 200f)
    {
        var rand = new Random();
        for (var i = 0; i < count; i++)
        {
            var angle = (float)(rand.NextDouble() * Math.PI * 2);
            var dist = (float)(rand.NextDouble() * radius);
            var x = center.X + dist * (float)Math.Cos(angle);
            var y = center.Y + dist * (float)Math.Sin(angle);
            var z = center.Z;
            AddTrash($"Trash_{i}", "Scattered debris", new Vector3(x, y, z));
        }
    }

    public void SpawnAtPositions(IEnumerable<Vector3> positions)
    {
        var index = 0;
        foreach (var position in positions)
        {
            AddTrash($"Trash_{index++}", "Scattered debris", position);
        }
    }

    public void SpawnFromDefinitions(IEnumerable<TrashDefinition> definitions)
    {
        var index = 0;
        foreach (var definition in definitions)
        {
            var name = string.IsNullOrWhiteSpace(definition.Name) ? $"Trash_{index++}" : definition.Name;
            var description = string.IsNullOrWhiteSpace(definition.Description) ? "Scattered debris" : definition.Description;
            AddTrash(name, description, definition.Position);
        }
    }

    public void AddTrash(string name, string description, Vector3 position)
    {
        items.Add(new TrashItem(name, description, position));
    }

    public void RemoveTrash(TrashItem item)
    {
        items.Remove(item);
    }

    public List<TrashItem> CollectNearbyTrash(Vector3 playerPosition, float radius = 2f)
    {
        var collected = new List<TrashItem>();
        var radiusSq = radius * radius;
        foreach (var item in items.Where(i => !i.Collected))
        {
            var delta = item.Position - playerPosition;
            if (delta.LengthSquared() <= radiusSq)
            {
                item.Collected = true;
                collected.Add(item);
            }
        }

        return collected;
    }
}
