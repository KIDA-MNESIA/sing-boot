using System.Reflection;
using System.Runtime.CompilerServices;

namespace SingBoot;

internal static class EmbeddedAssemblyResolver
{
    private const string YamlDotNetAssemblyName = "YamlDotNet";
    private const string YamlDotNetResourceName = "SingBoot.Dependencies.YamlDotNet.dll";

    private static readonly object SyncRoot = new();
    private static Assembly? _yamlDotNetAssembly;
    private static int _initialized;

    [ModuleInitializer]
    internal static void InitializeModule()
    {
        Initialize();
    }

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return;

        AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbeddedAssembly;
    }

    private static Assembly? ResolveEmbeddedAssembly(object? sender, ResolveEventArgs args)
    {
        var requestedAssembly = new AssemblyName(args.Name);
        if (!string.Equals(requestedAssembly.Name, YamlDotNetAssemblyName, StringComparison.OrdinalIgnoreCase))
            return null;

        lock (SyncRoot)
        {
            if (_yamlDotNetAssembly is not null)
                return _yamlDotNetAssembly;

            var hostAssembly = typeof(EmbeddedAssemblyResolver).Assembly;
            using var stream = hostAssembly.GetManifestResourceStream(YamlDotNetResourceName);
            if (stream is null)
                return null;

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            _yamlDotNetAssembly = Assembly.Load(buffer.ToArray());
            return _yamlDotNetAssembly;
        }
    }
}
