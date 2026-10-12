using System.Reflection;
using System.Runtime.Loader;

namespace TwinCat3.EnvironmentHost;

internal static class LocalInteropResolver
{
    private static readonly Dictionary<string, string> paths = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object gate = new();
    private static bool attached;
    public static void Register(IReadOnlyDictionary<string, string> assemblies)
    {
        lock (gate)
        {
            foreach (var pair in assemblies)
            {
                if (paths.TryGetValue(pair.Key, out var old) && !old.Equals(pair.Value, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Cannot switch interop assembly locations inside a live worker.");
                paths[pair.Key] = pair.Value;
            }
            if (!attached) { AssemblyLoadContext.Default.Resolving += Resolve; attached = true; }
        }
    }
    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName requested)
    {
        lock (gate)
            return requested.Name is not null && paths.TryGetValue(requested.Name, out var path)
                ? context.LoadFromAssemblyPath(path) : null;
    }
}
