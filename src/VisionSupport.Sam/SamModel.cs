using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace VisionSupport.Sam;

/// <summary>
/// The encoder's three feature maps for one crop, in arrays made once and bound to ONNX Runtime
/// once. The encoder writes straight into them and the decoder reads straight from them, so an
/// encode or a decode neither copies nor allocates them. Shapes are the ones the spike read out of
/// the SAM 3 tracker export.
/// </summary>
public sealed class SamEmbedding : IDisposable
{
    private static readonly long[] Level0Shape = { 1, 32, 288, 288 };
    private static readonly long[] Level1Shape = { 1, 64, 144, 144 };
    private static readonly long[] Level2Shape = { 1, 256, 72, 72 };

    private readonly float[] _level0 = new float[32 * 288 * 288];
    private readonly float[] _level1 = new float[64 * 144 * 144];
    private readonly float[] _level2 = new float[256 * 72 * 72];

    internal SamEmbedding()
    {
        Values = new[]
        {
            OrtValue.CreateTensorValueFromMemory(_level0, Level0Shape),
            OrtValue.CreateTensorValueFromMemory(_level1, Level1Shape),
            OrtValue.CreateTensorValueFromMemory(_level2, Level2Shape),
        };
    }

    /// <summary>image_embeddings.0, .1 and .2, each pinning its array for as long as this lives.</summary>
    internal OrtValue[] Values { get; }

    public void Dispose()
    {
        foreach (OrtValue value in Values) value.Dispose();
    }
}

/// <summary>The decoder's answer for one point: a score and a 288×288 logit map per mask (three of
/// each), and whether it thinks anything is there at all. The arrays belong to the model and are
/// overwritten by its next decode.</summary>
public sealed record SamDecodeResult(float[] IouScores, float[] MaskLogits, float ObjectScore);

/// <summary>
/// SAM 3 (tracker export, fp16) on DirectML. Loaded when the cutout mode starts and disposed when
/// it ends, so the GPU memory - about 2.6GB while encoding - is held only while the mode is up.
///
/// Every tensor goes through a buffer made here once and bound once: the decoder's inputs and
/// outputs belong to the model, the encoder's outputs to a <see cref="SamEmbedding"/>. Before this,
/// each decode allocated its outputs afresh, dozens of times a second.
///
/// <see cref="Encode"/> and <see cref="Decode"/> may run at the same time on two threads - they use
/// separate sessions - but each must be called from one thread only.
/// </summary>
public sealed class SamModel : IDisposable
{
    private static readonly long[] PixelShape = { 1, 3, SamGeometry.ModelSide, SamGeometry.ModelSide };
    private static readonly string[] EncoderInputNames = { "pixel_values" };
    private static readonly string[] EncoderOutputNames = { "image_embeddings.0", "image_embeddings.1", "image_embeddings.2" };
    private static readonly string[] DecoderInputNames =
        { "input_points", "input_labels", "input_boxes", "image_embeddings.0", "image_embeddings.1", "image_embeddings.2" };
    private static readonly string[] DecoderOutputNames = { "iou_scores", "pred_masks", "object_score_logits" };

    private readonly SessionOptions _encoderOptions;
    private readonly InferenceSession _encoder;
    private readonly RunOptions _encoderRun = new();
    private readonly SessionOptions _decoderOptions;
    private readonly InferenceSession _decoder;
    private readonly RunOptions _decoderRun = new();

    private readonly float[] _point = new float[2];
    private readonly long[] _label = { 1 };
    private readonly float[] _iou = new float[3];
    private readonly float[] _maskLogits = new float[3 * SamPostprocess.LowRes * SamPostprocess.LowRes];
    private readonly float[] _objectScore = new float[1];
    private readonly OrtValue _pointValue;
    private readonly OrtValue _labelValue;
    private readonly OrtValue _boxValue;
    private readonly OrtValue[] _decoderOutputs;

    private SamModel(SessionOptions encoderOptions, InferenceSession encoder,
                     SessionOptions decoderOptions, InferenceSession decoder)
    {
        _encoderOptions = encoderOptions;
        _encoder = encoder;
        _decoderOptions = decoderOptions;
        _decoder = decoder;

        _pointValue = OrtValue.CreateTensorValueFromMemory(_point, new long[] { 1, 1, 1, 2 });
        _labelValue = OrtValue.CreateTensorValueFromMemory(_label, new long[] { 1, 1, 1 });

        // No boxes: the export still requires the input, as an empty [1,0,4].
        _boxValue = OrtValue.CreateAllocatedTensorValue(OrtAllocator.DefaultInstance, TensorElementType.Float, new long[] { 1, 0, 4 });

        _decoderOutputs = new[]
        {
            OrtValue.CreateTensorValueFromMemory(_iou, new long[] { 1, 1, 3 }),
            OrtValue.CreateTensorValueFromMemory(_maskLogits, new long[] { 1, 1, 3, SamPostprocess.LowRes, SamPostprocess.LowRes }),
            OrtValue.CreateTensorValueFromMemory(_objectScore, new long[] { 1, 1, 1 }),
        };
    }

    /// <summary>
    /// Creates both sessions on the discrete GPU. Throws when there is no usable GPU or DirectML
    /// refuses; there is deliberately no CPU fallback, because a CPU encoding takes seconds and a
    /// mode that slow is worse than a clear message.
    /// </summary>
    public static SamModel Load(string encoderPath, string decoderPath)
    {
        DirectMlLoader.EnsureLoaded();

        GpuAdapter gpu = GpuAdapters.Pick(GpuAdapters.Enumerate())
            ?? throw new InvalidOperationException("DirectML 로 쓸 수 있는 GPU 가 없습니다.");

        SessionOptions encoderOptions = Options(gpu.Index);
        SessionOptions decoderOptions = Options(gpu.Index);
        InferenceSession? encoder = null;

        try
        {
            encoder = new InferenceSession(encoderPath, encoderOptions);
            var decoder = new InferenceSession(decoderPath, decoderOptions);
            return new SamModel(encoderOptions, encoder, decoderOptions, decoder);
        }
        catch
        {
            encoder?.Dispose();
            encoderOptions.Dispose();
            decoderOptions.Dispose();
            throw;
        }
    }

    /// <summary>Buffers for one encoding. The caller owns and disposes it.</summary>
    public SamEmbedding CreateEmbedding() => new();

    /// <param name="pixelValues">[1,3,1008,1008] as written by <see cref="SamPreprocess.ToTensor"/>.</param>
    /// <param name="into">Receives the three feature maps.</param>
    public void Encode(float[] pixelValues, SamEmbedding into)
    {
        using OrtValue input = OrtValue.CreateTensorValueFromMemory(pixelValues, PixelShape);
        _encoder.Run(_encoderRun, EncoderInputNames, new[] { input }, EncoderOutputNames, into.Values);
    }

    /// <summary>One positive point, in the encoder's 1008 space. The arrays in the result are this
    /// model's own and are overwritten by the next call.</summary>
    public SamDecodeResult Decode(SamEmbedding embedding, float modelX, float modelY)
    {
        _point[0] = modelX;
        _point[1] = modelY;

        OrtValue[] levels = embedding.Values;
        _decoder.Run(_decoderRun, DecoderInputNames,
                     new[] { _pointValue, _labelValue, _boxValue, levels[0], levels[1], levels[2] },
                     DecoderOutputNames, _decoderOutputs);

        return new SamDecodeResult(_iou, _maskLogits, _objectScore[0]);
    }

    public void Dispose()
    {
        _pointValue.Dispose();
        _labelValue.Dispose();
        _boxValue.Dispose();
        foreach (OrtValue output in _decoderOutputs) output.Dispose();

        _encoder.Dispose();
        _decoder.Dispose();
        _encoderRun.Dispose();
        _decoderRun.Dispose();
        _encoderOptions.Dispose();
        _decoderOptions.Dispose();
    }

    /// <summary>DirectML requires memory patterns off and sequential execution.</summary>
    private static SessionOptions Options(int deviceId)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            EnableMemoryPattern = false,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        };
        options.AppendExecutionProvider_DML(deviceId);
        return options;
    }
}
