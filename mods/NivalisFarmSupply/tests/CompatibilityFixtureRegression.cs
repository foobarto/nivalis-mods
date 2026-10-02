using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

public static class CompatibilityFixtureRegression
{
    // Each context gets the same adapter bytes and a dependency with the same
    // assembly identity. Only the dependency's available members differ.
    public static int Run(Action<Assembly> validate)
    {
        string fixtures = Path.Combine(AppContext.BaseDirectory, "compatibility-fixtures");
        string adapter = Path.Combine(fixtures, "adapter", "CompatibilityFixtureAdapter.dll");
        using (var matching = new FixtureContext(Path.Combine(fixtures, "present", "CompatibilityFixtureBinding.dll")))
            validate(matching.LoadFromAssemblyPath(adapter));
        using (var missing = new FixtureContext(Path.Combine(fixtures, "missing", "CompatibilityFixtureBinding.dll")))
        {
            try { validate(missing.LoadFromAssemblyPath(adapter)); }
            catch (MissingMethodException) { return 2; }
            throw new Exception("Validator accepted an adapter whose required dependency member was removed.");
        }
    }

    private sealed class FixtureContext(string dependency) : AssemblyLoadContext(isCollectible: true), IDisposable
    {
        protected override Assembly? Load(AssemblyName assemblyName) =>
            assemblyName.Name == "CompatibilityFixtureBinding" ? LoadFromAssemblyPath(dependency) : null;
        public void Dispose() => Unload();
    }
}
