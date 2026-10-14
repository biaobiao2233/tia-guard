using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using TwinCAT.Ads;
using TwinCat3.Engineering;

namespace TwinCat3.Runtime;

// Reuse the official client already installed with this XAE. Its native DLL
// transport reaches local Usermode aliases that the modern SDK's router channel
// did not resolve on the qualified host. Never redistribute the vendor DLLs.
public sealed class InstalledAdsClient : IDisposable
{
    private static readonly object Gate = new();
    private static Assembly? installed;
    private readonly OwnedUsermodeRuntime owner;
    private readonly dynamic client;
    private readonly Type stateType;
    private object? symbolLoader;
    internal InstalledAdsClient(OwnedUsermodeRuntime owner, int port)
    {
        this.owner = owner; owner.RequireLive();
        var assembly = Load(owner.InstallationRoot);
        stateType = assembly.GetType("TwinCAT.Ads.StateInfo", true)!;
        client = Activator.CreateInstance(assembly.GetType("TwinCAT.Ads.TcAdsClient", true)!)!;
        try { client.Synchronize = false; client.Timeout = 1000; client.Connect(owner.NetId, port); }
        catch { client.Dispose(); throw; }
    }
    internal static bool Installed(string root) => Find(root) is not null &&
        File.Exists(Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(root))!, "Common64", "TcAdsDll.dll"));
    private static string? Find(string root)
    {
        var directory = Path.Combine(root, "Components", "Plc", "LacBinaries", "GAC_MSIL", "TwinCAT.Ads");
        if (!Directory.Exists(directory)) return null;
        return Directory.EnumerateFiles(directory, "TwinCAT.Ads.dll", SearchOption.AllDirectories)
            .Where(p => AssemblyName.GetAssemblyName(p).Version?.Major == 4).OrderByDescending(p => p, StringComparer.Ordinal).FirstOrDefault();
    }
    private static Assembly Load(string root)
    {
        lock (Gate)
        {
            if (installed is not null) return installed;
            var path = Find(root) ?? throw new NotSupportedException("The installed official ADS 4 client is absent.");
            EngineeringPaths.CheckTree(Path.GetDirectoryName(path)!);
            var common = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(root))!, "Common64");
            NativeLibrary.Load(Path.Combine(common, "TcAdsDll.dll"));
            // Isolate the official legacy types from the modern SDK types in this Worker.
            var context = new AssemblyLoadContext("tia-guard-installed-ads", isCollectible: false);
            installed = context.LoadFromAssemblyPath(path); return installed;
        }
    }
    public StateInfo ReadState() => Invoke(() =>
    {
        dynamic actual = client.ReadState();
        return new StateInfo((AdsState)Convert.ToInt16(actual.AdsState), Convert.ToUInt16(actual.DeviceState));
    });
    // Official Beckhoff ADS LicenseAccess reads only the online count here;
    // no license contents, system identifiers, installation or activation.
    internal uint ReadOnlineLicenseCount() => Invoke(() => (uint)client.ReadAny((uint)0x01010006, (uint)0, typeof(uint)));
    public void WriteControl(StateInfo requested) => Invoke(() =>
    {
        var legacyState = Activator.CreateInstance(stateType,
            Enum.ToObject(stateType.Assembly.GetType("TwinCAT.Ads.AdsState", true)!, (short)requested.AdsState),
            Convert.ToInt16(requested.DeviceState))!;
        stateType.Assembly.GetType("TwinCAT.Ads.TcAdsClient", true)!.GetMethod("WriteControl", new[] { stateType })!.Invoke(client, new[] { legacyState });
        return true;
    });
    public InstalledSymbol ReadSymbol(string name) => Invoke(() =>
    {
        // ADS 4 ReadSymbolInfo returns the legacy ITcAdsSymbol (Type/Size),
        // not the modern symbol metadata contract. Use the official loader and
        // its public interfaces, including access flags. Internal implementation
        // classes cannot safely be accessed with the C# dynamic binder.
        var info = FindSymbol(name);
        var primitive = (bool)Property(info, "TwinCAT.TypeSystem.ISymbol", "IsPrimitiveType");
        var dataType = Property(info, "TwinCAT.TypeSystem.IInstance", "DataType");
        return new InstalledSymbol((string)Property(info, "TwinCAT.TypeSystem.IInstance", "TypeName"),
            (int)Property(info, "TwinCAT.TypeSystem.IBitSize", "ByteSize"), primitive,
            (bool)Property(info, "TwinCAT.TypeSystem.IInstance", "IsPointer"),
            (bool)Property(info, "TwinCAT.TypeSystem.IInstance", "IsReference"),
            (bool)Property(info, "TwinCAT.Ads.TypeSystem.ISymbolFlagProvider", "IsTcComInterfacePointer") ||
                !(bool)Property(dataType, "TwinCAT.TypeSystem.IDataType", "IsPrimitive"),
            (bool)Property(info, "TwinCAT.TypeSystem.ISymbol", "IsReadOnly"));
    });
    private object FindSymbol(string name)
    {
        var assembly = stateType.Assembly;
        if (symbolLoader is null)
        {
            var settings = assembly.GetType("TwinCAT.Ads.SymbolLoaderSettings", true)!
                .GetProperty("Default", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            var factory = assembly.GetType("TwinCAT.Ads.TypeSystem.SymbolLoaderFactory", true)!;
            var create = factory.GetMethod("Create", new[] { assembly.GetType("TwinCAT.IConnection", true)!,
                assembly.GetType("TwinCAT.ISymbolLoaderSettings", true)! })!;
            symbolLoader = create.Invoke(null, new object[] { client, settings })
                ?? throw new InvalidDataException("Official symbol loader returned no metadata.");
        }
        var symbols = Property(symbolLoader, "TwinCAT.TypeSystem.ISymbolProvider", "Symbols");
        var indexer = assembly.GetType("TwinCAT.TypeSystem.ReadOnlySymbolCollection", true)!
            .GetProperty("Item", new[] { typeof(string) })!;
        return indexer.GetValue(symbols, new object[] { name })
            ?? throw new InvalidDataException("Actual PLC symbol is absent.");
    }
    private object Property(object value, string contract, string name)
    {
        var type = stateType.Assembly.GetType(contract, true)!;
        if (!type.IsInstanceOfType(value)) throw new InvalidDataException("Actual metadata does not implement " + contract + ".");
        return type.GetProperty(name)!.GetValue(value)
            ?? throw new InvalidDataException("Actual symbol metadata is incomplete: " + name + ".");
    }
    public object ReadValue(string name, Type type) => Invoke(() =>
    {
        int handle = client.CreateVariableHandle(name);
        try
        {
            if (type == typeof(string)) { var info = FindSymbol(name); return (object)client.ReadAny(handle, type,
                new[] { (int)Property(info, "TwinCAT.TypeSystem.IBitSize", "ByteSize") }); }
            return (object)client.ReadAny(handle, type);
        }
        finally { client.DeleteVariableHandle(handle); }
    });
    public void WriteValue(string name, object value) => Invoke(() =>
    {
        int handle = client.CreateVariableHandle(name);
        try { client.WriteAny(handle, value); }
        finally { client.DeleteVariableHandle(handle); }
        return true;
    });
    private T Invoke<T>(Func<T> callback)
    {
        owner.RequireLive();
        try { return callback(); }
        catch (Exception caught)
        {
            var error = caught is TargetInvocationException { InnerException: { } inner } ? inner : caught;
            if (error.GetType().FullName != "TwinCAT.Ads.AdsErrorException") throw;
            throw new InstalledAdsException(error.Message, error);
        }
    }
    public void Dispose() => client.Dispose();
}
public sealed record InstalledSymbol(string TypeName, int ByteSize, bool IsPrimitiveType, bool IsPointer, bool IsReference, bool IsProperty, bool IsReadOnly);
public sealed class InstalledAdsException(string message, Exception inner) : IOException(message, inner);
