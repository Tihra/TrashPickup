using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace TrashPickupPlugin.Services;

public class CollectedItem
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public int ZoneId { get; set; }
    public DateTime CollectedAt { get; set; }
}

public class InventoryService : IDisposable
{
    private readonly string dbPath;
    private readonly SqliteConnection connection;

    public InventoryService(string dbPath)
    {
        this.dbPath = dbPath;
        var connString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        connection = new SqliteConnection(connString);
        connection.Open();
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS collected_items (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            description TEXT,
            posx REAL,
            posy REAL,
            posz REAL,
            zoneid INTEGER,
            collected_at TEXT NOT NULL
        );";
        cmd.ExecuteNonQuery();
    }

    public void AddCollectedItem(CollectedItem item)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO collected_items (name, description, posx, posy, posz, zoneid, collected_at)
                            VALUES ($name, $description, $posx, $posy, $posz, $zoneid, $collected_at);";
        cmd.Parameters.AddWithValue("$name", item.Name);
        cmd.Parameters.AddWithValue("$description", item.Description);
        cmd.Parameters.AddWithValue("$posx", item.PosX);
        cmd.Parameters.AddWithValue("$posy", item.PosY);
        cmd.Parameters.AddWithValue("$posz", item.PosZ);
        cmd.Parameters.AddWithValue("$zoneid", item.ZoneId);
        cmd.Parameters.AddWithValue("$collected_at", item.CollectedAt.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public List<CollectedItem> GetCollectedItems()
    {
        var results = new List<CollectedItem>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, description, posx, posy, posz, zoneid, collected_at FROM collected_items";
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            results.Add(new CollectedItem
            {
                Id = rdr.GetInt64(0),
                Name = rdr.IsDBNull(1) ? string.Empty : rdr.GetString(1),
                Description = rdr.IsDBNull(2) ? string.Empty : rdr.GetString(2),
                PosX = rdr.IsDBNull(3) ? 0f : (float)rdr.GetDouble(3),
                PosY = rdr.IsDBNull(4) ? 0f : (float)rdr.GetDouble(4),
                PosZ = rdr.IsDBNull(5) ? 0f : (float)rdr.GetDouble(5),
                ZoneId = rdr.IsDBNull(6) ? 0 : rdr.GetInt32(6),
                CollectedAt = DateTime.Parse(rdr.GetString(7))
            });
        }

        return results;
    }

    public void Dispose()
    {
        connection?.Dispose();
    }
}
