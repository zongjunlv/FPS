using System;
using System.Collections.Generic;
using System.Reflection;

public static class RuntimeTypeResolver
{
    public static Type GetType(string typeName)
    {
        return GetType(typeName, false);
    }

    public static Type GetType(string typeName, bool throwOnError)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return throwOnError
                ? throw new TypeLoadException("A runtime type name is required.")
                : null;
        }

        Type exact = Type.GetType(typeName, false);

        if (exact != null)
        {
            return exact;
        }

        string fullName = typeName.Split(',')[0].Trim();
        var matches = new List<Type>();

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            string assemblyName = assembly.GetName().Name;

            if (!assemblyName.StartsWith(
                    "FPS.",
                    StringComparison.Ordinal))
            {
                continue;
            }

            Type candidate = assembly.GetType(fullName, false);

            if (candidate != null)
            {
                matches.Add(candidate);
            }
        }

        if (matches.Count > 1)
        {
            throw new AmbiguousMatchException(
                $"Runtime type '{fullName}' exists in multiple FPS assemblies.");
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        return throwOnError
            ? throw new TypeLoadException(
                $"Runtime type '{fullName}' was not found.")
            : null;
    }
}
