namespace TrashPickupPlugin;

public static class AdminWhitelist
{
    private static readonly System.Collections.Generic.HashSet<string> AllowedNames
        = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "Tihra Sweet",
            ""
        };

    public static bool IsWhitelisted(string? playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return false;

        return AllowedNames.Contains(playerName.Trim());
    }

    public static System.Collections.Generic.IEnumerable<string> GetAllowedNames()
        => AllowedNames;
}
