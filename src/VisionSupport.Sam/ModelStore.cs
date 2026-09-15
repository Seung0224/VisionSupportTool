using System.IO;
using System.Net.Http;

namespace VisionSupport.Sam;

/// <summary>One file of the model and the exact size it must have.</summary>
public sealed record ModelFile(string Name, long Size);

/// <summary>
/// Where the SAM 3 model lives on this PC, and fetching it the first time it is needed.
///
/// Completeness is by exact byte size. A file cut short by a dropped connection would otherwise
/// count as present and fail much later, inside ONNX Runtime, with an error that says nothing about
/// a download.
/// </summary>
public sealed class ModelStore
{
    public const string EncoderFile = "vision_encoder_fp16.onnx";

    public const string DecoderFile = "prompt_encoder_mask_decoder_fp16.onnx";

    public static readonly Uri Source =
        new("https://huggingface.co/onnx-community/sam3-tracker-ONNX/resolve/main/onnx/");

    /// <summary>The graphs are small; the weights sit beside them as external data. Sizes checked
    /// against the repository on 2026-09-15.</summary>
    public static readonly IReadOnlyList<ModelFile> Sam3TrackerFp16 = new[]
    {
        new ModelFile(EncoderFile, 1_263_005),
        new ModelFile(EncoderFile + "_data", 934_729_728),
        new ModelFile(DecoderFile, 229_804),
        new ModelFile(DecoderFile + "_data", 11_011_072),
    };

    private readonly Uri _source;
    private readonly IReadOnlyList<ModelFile> _files;

    public ModelStore() : this(DefaultFolder, Source, Sam3TrackerFp16)
    {
    }

    public ModelStore(string folder, Uri source, IReadOnlyList<ModelFile> files)
    {
        Folder = folder;
        _source = source;
        _files = files;
    }

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VisionSupport", "Sam", "sam3-tracker-fp16");

    public string Folder { get; }

    public string EncoderPath => Path.Combine(Folder, EncoderFile);

    public string DecoderPath => Path.Combine(Folder, DecoderFile);

    public long TotalBytes => _files.Sum(f => f.Size);

    public bool IsComplete() => _files.All(IsPresent);

    /// <summary>
    /// Fetches every file that is missing or the wrong size, reporting overall progress from 0 to 1.
    ///
    /// Each file is written to <c>.part</c> and only renamed once its size is right, so an
    /// interrupted run never leaves something <see cref="IsComplete"/> would accept. On failure the
    /// part file is removed and the exception goes to the caller, which is where the user is told.
    /// </summary>
    public async Task DownloadAsync(HttpClient http, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Folder);

        long total = TotalBytes;
        long done = 0;
        var buffer = new byte[81_920];

        foreach (ModelFile file in _files)
        {
            if (IsPresent(file))
            {
                done += file.Size;
                progress?.Report((double)done / total);
                continue;
            }

            string path = Path.Combine(Folder, file.Name);
            string part = path + ".part";

            try
            {
                using HttpResponseMessage response = await http.GetAsync(
                    new Uri(_source, file.Name), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                await using (Stream input = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (FileStream output = File.Create(part))
                {
                    int read;
                    while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        done += read;
                        progress?.Report((double)done / total);
                    }
                }

                long written = new FileInfo(part).Length;
                if (written != file.Size)
                {
                    throw new InvalidDataException($"{file.Name} 크기가 맞지 않습니다 ({written:N0} / {file.Size:N0} 바이트)");
                }

                File.Move(part, path, overwrite: true);
            }
            catch
            {
                try
                {
                    File.Delete(part);
                }
                catch (IOException)
                {
                    // Still held by the stream that failed; the next run overwrites it anyway.
                }
                throw;
            }
        }
    }

    private bool IsPresent(ModelFile file)
    {
        var info = new FileInfo(Path.Combine(Folder, file.Name));
        return info.Exists && info.Length == file.Size;
    }
}
