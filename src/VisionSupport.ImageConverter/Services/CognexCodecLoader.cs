using System.IO;
using System.Reflection;

namespace VisionSupport.ImageConverter.Services;

/// <summary>
/// Finds and loads the optional Cognex .idb codec at runtime. The plugin assembly
/// (VisionSupport.ImageConverter.Cognex.dll) is dropped into a <c>plugins</c> folder next to the
/// shell only on machines that built it against an installed VisionPro. When it is missing - the
/// build server, any PC without VisionPro - <see cref="Load"/> returns null and the converter
/// carries on with .idb absent from its menus.
/// </summary>
public static class CognexCodecLoader
{
    private const string PluginFileName = "VisionSupport.ImageConverter.Cognex.dll";

    /// <param name="cognexBinPath">VisionPro's <c>bin</c> folder, used to resolve the
    /// <c>Cognex.VisionPro.*</c> assemblies the plugin depends on. Defaults to the standard
    /// install location.</param>
    /// <returns>A working codec, or null if the plugin is absent or fails to load.</returns>
    public static IImageDbCodec? Load(string? cognexBinPath = null)
    {
        string pluginPath = Path.Combine(AppContext.BaseDirectory, "plugins", PluginFileName);
        if (!File.Exists(pluginPath)) return null;

        string binPath = string.IsNullOrWhiteSpace(cognexBinPath)
            ? @"C:\Program Files\Cognex\VisionPro\bin"
            : cognexBinPath;

        try
        {
            ResolveEventHandler resolver = (_, args) =>
            {
                string simpleName = new AssemblyName(args.Name).Name ?? string.Empty;
                if (!simpleName.StartsWith("Cognex.", StringComparison.OrdinalIgnoreCase)) return null;
                string dll = Path.Combine(binPath, simpleName + ".dll");
                return File.Exists(dll) ? Assembly.LoadFrom(dll) : null;
            };
            AppDomain.CurrentDomain.AssemblyResolve += resolver;

            Assembly plugin = Assembly.LoadFrom(pluginPath);
            Type? implementation = Array.Find(plugin.GetTypes(),
                t => typeof(IImageDbCodec).IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false });

            return implementation is null ? null : (IImageDbCodec?)Activator.CreateInstance(implementation);
        }
        catch
        {
            // VisionPro not installed, wrong version, no licence - the plugin is unusable, and that
            // is expected on most machines. The converter treats it exactly like "not present".
            return null;
        }
    }
}
