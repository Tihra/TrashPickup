using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using TrashPickupPlugin.Extensions;

namespace TrashPickupPlugin.Services;

public class NavMeshService
{
    private readonly object? navMeshInstance;
    private readonly MethodInfo? isPointOnMeshMethod;
    private readonly MethodInfo? findNearestPointMethod;

    public bool IsAvailable => navMeshInstance != null && (isPointOnMeshMethod != null || findNearestPointMethod != null);
    public string SourceName { get; }

    public NavMeshService()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in assembly.GetTypesSafe())
            {
                if (type.Name.IndexOf("Navmesh", StringComparison.OrdinalIgnoreCase) < 0 &&
                    type.Name.IndexOf("NavMesh", StringComparison.OrdinalIgnoreCase) < 0 &&
                    type.Name.IndexOf("Navigation", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var instance = ResolveInstance(type);
                if (instance == null)
                    continue;

                var isPoint = FindIsPointOnMeshMethod(type);
                var nearestPoint = FindNearestPointMethod(type);
                if (isPoint == null && nearestPoint == null)
                    continue;

                navMeshInstance = instance;
                isPointOnMeshMethod = isPoint;
                findNearestPointMethod = nearestPoint;
                SourceName = type.FullName ?? type.Name;
                return;
            }
        }

        SourceName = "None";
    }

    private static object? ResolveInstance(Type type)
    {
        var staticProps = new[] { "Instance", "Default", "Current", "Singleton" };
        foreach (var name in staticProps)
        {
            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (prop != null)
            {
                try
                {
                    var value = prop.GetValue(null);
                    if (value != null)
                        return value;
                }
                catch { }
            }

            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (field != null)
            {
                try
                {
                    var value = field.GetValue(null);
                    if (value != null)
                        return value;
                }
                catch { }
            }
        }

        try
        {
            if (!type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null)
                return Activator.CreateInstance(type);
        }
        catch { }

        return null;
    }

    private static MethodInfo? FindIsPointOnMeshMethod(Type type)
    {
        return type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .FirstOrDefault(m => string.Equals(m.Name, "IsPointOnMesh", StringComparison.OrdinalIgnoreCase)
                && m.ReturnType == typeof(bool)
                && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType == typeof(Vector3));
    }

    private static MethodInfo? FindNearestPointMethod(Type type)
    {
        return type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .FirstOrDefault(m => string.Equals(m.Name, "FindNearestPoint", StringComparison.OrdinalIgnoreCase)
                && m.ReturnType == typeof(bool)
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType == typeof(Vector3)
                && m.GetParameters()[1].ParameterType == typeof(Vector3).MakeByRefType());
    }

    public bool IsPointOnMesh(Vector3 point)
    {
        if (!IsAvailable || isPointOnMeshMethod == null)
            return false;

        try
        {
            var args = new object?[] { point };
            var result = isPointOnMeshMethod.Invoke(navMeshInstance, args);
            return result is bool valid && valid;
        }
        catch
        {
            return false;
        }
    }

    public bool FindNearestPoint(Vector3 point, out Vector3 nearest)
    {
        nearest = point;
        if (!IsAvailable || findNearestPointMethod == null)
            return false;

        try
        {
            var args = new object?[] { point, nearest };
            var success = findNearestPointMethod.Invoke(navMeshInstance, args);
            if (success is bool valid && valid)
            {
                if (args[1] is Vector3 found)
                {
                    nearest = found;
                    return true;
                }
            }
        }
        catch { }

        return false;
    }
}
