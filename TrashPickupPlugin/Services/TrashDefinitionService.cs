using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TrashPickupPlugin.Services;

public class TrashDefinitionService
{
    private readonly string definitionsDirectory;
    private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };

    public TrashDefinitionService(string pluginDataFolder)
    {
        definitionsDirectory = Path.Combine(pluginDataFolder, "TrashDefinitions");
        Directory.CreateDirectory(definitionsDirectory);
    }

    public string GetZoneFileName(uint territoryType, uint mapId)
        => $"zone_{territoryType}_{mapId}.json";

    public string GetZoneFilePath(uint territoryType, uint mapId)
        => Path.Combine(definitionsDirectory, GetZoneFileName(territoryType, mapId));

    public bool HasDefinitions(uint territoryType, uint mapId)
    {
        var path = GetZoneFilePath(territoryType, mapId);
        return File.Exists(path);
    }

    public List<TrashDefinition> LoadDefinitions(uint territoryType, uint mapId)
    {
        var path = GetZoneFilePath(territoryType, mapId);
        if (!File.Exists(path))
            return new List<TrashDefinition>();

        try
        {
            var json = File.ReadAllText(path);
            var definitions = JsonSerializer.Deserialize<List<TrashDefinition>>(json, jsonOptions);
            return definitions ?? new List<TrashDefinition>();
        }
        catch
        {
            return new List<TrashDefinition>();
        }
    }

    public void SaveDefinitions(uint territoryType, uint mapId, IEnumerable<TrashDefinition> definitions)
    {
        var path = GetZoneFilePath(territoryType, mapId);
        Directory.CreateDirectory(definitionsDirectory);

        var json = JsonSerializer.Serialize(definitions, jsonOptions);
        File.WriteAllText(path, json);
    }
}
