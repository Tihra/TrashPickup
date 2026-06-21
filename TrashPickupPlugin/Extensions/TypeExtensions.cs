using System;
using System.Collections.Generic;
using System.Reflection;

namespace TrashPickupPlugin.Extensions;

public static class TypeExtensions
{
    public static IEnumerable<Type> GetTypesSafe(this Assembly asm)
    {
        try
        {
            return asm.GetTypes();
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }
}
