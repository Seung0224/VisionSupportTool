using System.IO;
using System.Runtime.InteropServices;

namespace VisionSupport.Sam;

/// <summary>
/// Makes sure the DirectML that ONNX Runtime uses is the one shipped with it.
///
/// <c>onnxruntime.dll</c> imports <c>DirectML.dll</c> by name, and Windows resolves that from the
/// folder of the process's executable and then System32 - not from the folder onnxruntime.dll was
/// loaded from. Run under <c>dotnet.exe</c> or the test host, the executable's folder has no
/// DirectML, so System32's inbox copy is taken. On this Windows 10 build that copy is DirectML 1.0,
/// which cannot create the feature-level-5 device ONNX Runtime asks for, and session creation fails
/// with DXGI_ERROR_UNSUPPORTED on every GPU - which looks exactly like a GPU problem.
///
/// Loading the shipped copy by full path first settles it: a DLL already loaded under that name is
/// what later imports bind to.
/// </summary>
internal static class DirectMlLoader
{
    private static readonly object Gate = new();
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_loaded) return;

            string path = FindRedistributable(SearchFolders())
                ?? throw new DllNotFoundException("앱 폴더에서 DirectML.dll 을 찾지 못했습니다.");

            NativeLibrary.Load(path);
            _loaded = true;
        }
    }

    /// <summary>The first folder holding a DirectML.dll, as a full path to it.</summary>
    internal static string? FindRedistributable(IEnumerable<string> folders)
        => folders.Select(folder => Path.Combine(folder, "DirectML.dll")).FirstOrDefault(File.Exists);

    /// <summary>
    /// The application folder, then the folders the runtime itself resolves native libraries from.
    /// The second set is what finds it in a single-file build, where native libraries are extracted
    /// to a temporary folder rather than sitting beside the exe.
    /// </summary>
    private static IEnumerable<string> SearchFolders()
    {
        yield return AppContext.BaseDirectory;

        if (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is string list)
        {
            foreach (string folder in list.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                yield return folder;
            }
        }
    }
}
