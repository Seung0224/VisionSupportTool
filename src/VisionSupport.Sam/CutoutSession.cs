using System.IO;
using System.Net.Http;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace VisionSupport.Sam;

public enum CutoutEnd
{
    Picked,
    Cancelled,
    Failed,
}

/// <summary>How a cutout mode ended. The cursor position is where the pick was made, for placing
/// the viewer beside it.</summary>
public sealed record CutoutResult(CutoutEnd End, CutoutImage? Image = null, string? Error = null,
                                  int CursorX = 0, int CursorY = 0);

/// <summary>
/// One run of the cutout mode, from the tile click to a pick or a cancel.
///
/// Four kinds of work run side by side and meet only at the hand-off points below:
/// a capture thread per monitor keeps its <see cref="ScreenFrame"/> fresh; the encoder thread turns
/// the crop under the cursor into an embedding whenever the last one has gone stale (about 0.4s on
/// SAM 3); the decoder thread follows the cursor with the newest embedding, about 10ms a step,
/// never waiting for the encoder; and the UI thread paints frames and masks and takes clicks.
///
/// Memory: every large buffer is made once, after the model has loaded, and reused until the mode
/// ends. Measured before this design, the buffers allocated per decode added about 125MB per use
/// that nothing collected, because the launcher is idle after a mode and idle processes do not
/// collect. When the mode ends everything is disposed and collected straight away.
/// </summary>
public sealed class CutoutSession
{
    /// <summary>One current, one still read by the decoder or the pick, one being written.</summary>
    private const int CropSlots = 3;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private static int _running;

    private readonly ModelStore _store;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<CutoutResult> _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Task> _workers = new();

    private IReadOnlyList<MonitorInfo> _monitors = Array.Empty<MonitorInfo>();
    private ScreenFrame[] _frames = Array.Empty<ScreenFrame>();
    private CutoutOverlay[] _overlays = Array.Empty<CutoutOverlay>();
    private SamModel? _model;
    private HandoffPool<EncodedCrop>? _crops;

    /// <summary>The encoder's input tensor. Encoder thread only.</summary>
    private float[] _tensor = Array.Empty<float>();

    // What a click would cut. The decoder thread writes the spare mask, then swaps it in under the gate.
    private readonly object _pickGate = new();
    private bool[][] _masks = Array.Empty<bool[]>();
    private int _spareMask;
    private bool[] _edgeScratch = Array.Empty<bool>();
    private byte[] _cleanupMarks = Array.Empty<byte>();
    private int[] _cleanupQueue = Array.Empty<int>();
    private EncodedCrop? _shownCrop;

    /// <summary>Mask size level set by the wheel: 0 is the largest. Overlay threads write, decoder reads.</summary>
    private int _level;

    /// <summary>How many levels the decoder actually offered last time - usually 3, fewer once a mask
    /// is untrusted. The wheel clamps against this so it cannot run past what is really there.</summary>
    private int _availableLevels = SamPostprocess.MaskCount;
    private SamMask? _shownMask;

    // What the UI thread draws next. The decoder thread renders the spare overlay, then swaps it in
    // under the gate; the UI thread copies the pending one into its bitmap under the same gate.
    private readonly object _drawGate = new();
    private byte[][] _overlayPixels = Array.Empty<byte[]>();
    private int _spareOverlay;
    private int _drawMonitor = -1;
    private PixelRect? _drawCrop;
    private string? _drawLevelText;
    private int _drawCursorX;
    private int _drawCursorY;
    private bool _drawDirty;
    private bool _drawQueued;
    private bool _waitingForFirstMask;

    private CutoutSession(ModelStore store) => _store = store;

    /// <summary>
    /// Runs the mode on the calling UI thread and gives its memory back before returning. A second
    /// call while one runs ends at once, as cancelled - the overlay already covers the tile.
    /// </summary>
    public static async Task<CutoutResult> RunAsync(ModelStore store)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return new CutoutResult(CutoutEnd.Cancelled);

        try
        {
            CutoutResult result = await RunOnceAsync(store);
            await ReclaimAsync();
            return result;
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    /// <summary>Kept apart from <see cref="RunAsync"/> so nothing on its frame still refers to the
    /// session when the collection runs.</summary>
    private static Task<CutoutResult> RunOnceAsync(ModelStore store) => new CutoutSession(store).RunCoreAsync();

    /// <summary>
    /// Collects once the closed overlays have finished tearing down. The mode's buffers, the capture
    /// frames and the bitmaps' native memory are all unreachable by now, but an idle launcher gives
    /// the GC no reason to run, so without this they stayed until something else happened.
    /// </summary>
    private static async Task ReclaimAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        await Task.Run(() =>
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        });
    }

    private async Task<CutoutResult> RunCoreAsync()
    {
        if (!_store.IsComplete())
        {
            string? failure = await DownloadAsync();
            if (failure is not null) return Failed(failure);
        }

        try
        {
            ShowOverlays();
            if (_overlays.Any(overlay => !overlay.Excluded))
            {
                return Failed("화면 캡처에서 누끼 막을 제외하지 못했습니다.");
            }

            StartCapture();

            (int x, int y) = Monitors.Cursor();
            _waitingForFirstMask = true;
            ShowStatus("준비 중…", x, y);

            try
            {
                _model = await Task.Run(() => SamModel.Load(_store.EncoderPath, _store.DecoderPath));
            }
            catch (Exception ex)
            {
                return Failed("GPU 에서 누끼 모델을 올리지 못했습니다: " + FirstLine(ex));
            }

            if (!_ended.Task.IsCompleted)
            {
                AllocateBuffers();
                _workers.Add(Task.Factory.StartNew(EncoderLoop, TaskCreationOptions.LongRunning));
                _workers.Add(Task.Factory.StartNew(DecoderLoop, TaskCreationOptions.LongRunning));
            }

            return await _ended.Task;
        }
        finally
        {
            await StopAsync();
        }
    }

    private async Task<string?> DownloadAsync()
    {
        var window = new DownloadWindow(_store.TotalBytes);
        window.Show();

        try
        {
            await _store.DownloadAsync(Http, window.Progress, CancellationToken.None);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException
                                      or TaskCanceledException or UnauthorizedAccessException)
        {
            return "누끼 모델을 내려받지 못했습니다: " + FirstLine(ex);
        }
        finally
        {
            window.Close();
        }
    }

    private void ShowOverlays()
    {
        _monitors = Monitors.All();
        _frames = _monitors.Select(m => new ScreenFrame(m.Bounds)).ToArray();
        _overlays = _monitors.Select(m => new CutoutOverlay(m)).ToArray();

        foreach (CutoutOverlay overlay in _overlays)
        {
            overlay.MouseLeftButtonDown += (_, _) => Pick();
            overlay.MouseRightButtonDown += (_, _) => End(new CutoutResult(CutoutEnd.Cancelled));
            overlay.MouseWheel += (_, e) => ChangeLevel(e.Delta);
            overlay.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) End(new CutoutResult(CutoutEnd.Cancelled));
            };
            overlay.Closed += (_, _) => End(new CutoutResult(CutoutEnd.Cancelled));
            overlay.Show();
        }

        // The overlay under the cursor takes the keyboard, so Esc works without clicking first.
        (int x, int y) = Monitors.Cursor();
        int index = IndexOf(x, y);
        _overlays[index >= 0 ? index : 0].Activate();

        CompositionTarget.Rendering += OnRendering;
    }

    /// <summary>Everything the encoder and decoder will ever need, made once. About 85MB for SAM 3,
    /// most of it the three encodings.</summary>
    private void AllocateBuffers()
    {
        int pixels = SamGeometry.ModelSide * SamGeometry.ModelSide;

        _crops = new HandoffPool<EncodedCrop>(
            Enumerable.Range(0, CropSlots).Select(_ => new EncodedCrop(_model!.CreateEmbedding(), pixels * 4)));
        _tensor = new float[3 * pixels];
        _masks = new[] { new bool[pixels], new bool[pixels] };
        _edgeScratch = new bool[pixels];
        _cleanupMarks = new byte[pixels];
        _cleanupQueue = new int[pixels];
        _overlayPixels = new[] { new byte[pixels * 4], new byte[pixels * 4] };
    }

    private void StartCapture()
    {
        foreach (ScreenFrame frame in _frames)
        {
            _workers.Add(Task.Factory.StartNew(() => CaptureLoop(frame), TaskCreationOptions.LongRunning));
        }
    }

    /// <summary>
    /// Keeps one monitor's frame fresh. The monitor under the cursor is captured back to back: it is
    /// the one being looked at and the crops come from it. The others only have to look alive, and a
    /// full-screen blit is a real slice of a CPU core, so they refresh five times a second.
    /// </summary>
    private void CaptureLoop(ScreenFrame frame)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                frame.Capture();

                (int x, int y) = Monitors.Cursor();
                if (!frame.Bounds.Contains(x, y)) Wait(200);
            }
        }
        catch (Exception ex)
        {
            End(Failed("화면을 캡처하지 못했습니다: " + FirstLine(ex)));
        }
        finally
        {
            frame.Dispose();
        }
    }

    /// <summary>
    /// Encodes the crop under the cursor whenever <see cref="EncodePolicy"/> says the current
    /// encoding is stale, into a slot nobody is reading, and publishes it for the decoder.
    /// </summary>
    private void EncoderLoop()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                (int x, int y) = Monitors.Cursor();
                int index = IndexOf(x, y);
                if (index < 0 || _frames[index].Version == 0 || !EncodePolicy.NeedsEncode(CurrentStamp(), x, y, DateTime.UtcNow))
                {
                    Wait(15);
                    continue;
                }

                EncodedCrop? slot = _crops!.RentForWriting();
                if (slot is null)
                {
                    Wait(5);
                    continue;
                }

                DateTime taken = DateTime.UtcNow;
                PixelRect crop = SamGeometry.CropAround(x, y, _frames[index].Bounds);
                _frames[index].CopyRegion(crop, slot.Pixels);
                SamPreprocess.ToTensor(slot.Pixels, crop.Width, crop.Height, _tensor);
                _model!.Encode(_tensor, slot.Embedding);

                slot.Stamp = new EncodeStamp(crop, x, y, taken);
                _crops.Publish(slot);
            }
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            End(Failed("누끼 인코딩 중 오류가 났습니다: " + FirstLine(ex)));
        }
    }

    /// <summary>
    /// Follows the cursor with the newest encoding. It decodes whenever the cursor has moved or a new
    /// encoding has been published, and never waits for one: while the encoder catches up with a
    /// cursor that has left the encoded area, it shows nothing highlighted rather than a stale mask.
    /// </summary>
    private void DecoderLoop()
    {
        try
        {
            int lastX = int.MinValue, lastY = int.MinValue, lastLevel = -1;
            EncodeStamp? lastStamp = null;

            while (!_stop.IsCancellationRequested)
            {
                (int x, int y) = Monitors.Cursor();
                EncodedCrop? crop = _crops!.AcquireCurrent();
                if (crop is null)
                {
                    Wait(10);
                    continue;
                }

                EncodeStamp stamp = crop.Stamp;
                int level = Volatile.Read(ref _level);
                if (x == lastX && y == lastY && stamp == lastStamp && level == lastLevel)
                {
                    _crops.Release(crop);
                    Wait(8);
                    continue;
                }

                lastX = x;
                lastY = y;
                lastStamp = stamp;
                lastLevel = level;

                int monitor = IndexOf(x, y);
                if (monitor < 0 || !stamp.Crop.Contains(x, y))
                {
                    PresentNothing(crop);
                    continue;
                }

                PixelRect rect = stamp.Crop;
                (float modelX, float modelY) = SamGeometry.ToModel(x, y, rect);
                SamDecodeResult decoded = _model!.Decode(crop.Embedding, modelX, modelY);

                SamMask? mask = null;
                string? levelText = null;
                if (SamPostprocess.ChooseMask(decoded.MaskLogits, decoded.IouScores, decoded.ObjectScore, level) is { } choice)
                {
                    // However many levels the decoder actually offered this time, not the fixed
                    // maximum - the wheel is clamped against this so going past the last one holds
                    // there instead of counting up somewhere the label never shows.
                    Volatile.Write(ref _availableLevels, choice.Total);

                    mask = SamPostprocess.Upscale(decoded.MaskLogits, choice.Index, rect.Width, rect.Height, _masks[_spareMask]);
                    if (MaskCleanup.Clean(mask, x - rect.X, y - rect.Y, _cleanupMarks, _cleanupQueue))
                    {
                        levelText = LevelLabel.Format(choice.Level, choice.Total);
                    }
                    else
                    {
                        mask = null;
                    }
                }

                if (mask is not null)
                {
                    MaskOverlay.Render(mask, CutoutOverlay.DimAlpha, _overlayPixels[_spareOverlay], _edgeScratch);
                }
                else
                {
                    MaskOverlay.Dim(rect.Width, rect.Height, CutoutOverlay.DimAlpha, _overlayPixels[_spareOverlay]);
                }

                Present(monitor, crop, rect, mask, levelText, x, y);
            }
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            End(Failed("누끼 처리 중 오류가 났습니다: " + FirstLine(ex)));
        }
    }

    private EncodeStamp? CurrentStamp()
    {
        EncodedCrop? current = _crops!.AcquireCurrent();
        if (current is null) return null;

        EncodeStamp stamp = current.Stamp;
        _crops.Release(current);
        return stamp;
    }

    /// <summary>
    /// Makes a decode what a click would cut and what the UI draws next. The crop's lease passes to
    /// the pick when there is an object, and the crop it replaces is released; with no object this
    /// decode's lease goes straight back.
    /// </summary>
    private void Present(int monitor, EncodedCrop crop, PixelRect rect, SamMask? mask, string? levelText, int cursorX, int cursorY)
    {
        lock (_pickGate)
        {
            if (_shownCrop is not null) _crops!.Release(_shownCrop);

            if (mask is null)
            {
                _crops!.Release(crop);
                _shownCrop = null;
                _shownMask = null;
            }
            else
            {
                _shownCrop = crop;
                _shownMask = mask;
                _spareMask = 1 - _spareMask;
            }
        }

        QueueDraw(monitor, rect, overlayRendered: true, levelText, cursorX, cursorY);
    }

    private void PresentNothing(EncodedCrop crop)
    {
        lock (_pickGate)
        {
            _crops!.Release(crop);
            if (_shownCrop is not null) _crops.Release(_shownCrop);
            _shownCrop = null;
            _shownMask = null;
        }

        QueueDraw(-1, null, overlayRendered: false, null, 0, 0);
    }

    private void QueueDraw(int monitor, PixelRect? crop, bool overlayRendered, string? levelText, int cursorX, int cursorY)
    {
        lock (_drawGate)
        {
            if (overlayRendered) _spareOverlay = 1 - _spareOverlay;
            _drawMonitor = monitor;
            _drawCrop = crop;
            _drawLevelText = levelText;
            _drawCursorX = cursorX;
            _drawCursorY = cursorY;
            _drawDirty = true;

            if (_drawQueued) return;
            _drawQueued = true;
        }

        _dispatcher.BeginInvoke(DispatcherPriority.Render, DrawPending);
    }

    private void DrawPending()
    {
        lock (_drawGate)
        {
            _drawQueued = false;
            if (!_drawDirty || _stop.IsCancellationRequested) return;
            _drawDirty = false;

            byte[] overlay = _overlayPixels[1 - _spareOverlay];
            for (int i = 0; i < _overlays.Length; i++)
            {
                bool here = i == _drawMonitor;
                _overlays[i].ShowCrop(here ? _drawCrop : null, here ? overlay : null);
                _overlays[i].ShowLevel(here ? _drawLevelText : null, _drawCursorX, _drawCursorY);
            }
        }

        if (_waitingForFirstMask)
        {
            _waitingForFirstMask = false;
            ShowStatus(null, 0, 0);
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        for (int i = 0; i < _overlays.Length; i++) _overlays[i].ShowFrame(_frames[i]);
    }

    private void Pick()
    {
        CutoutImage? image;
        lock (_pickGate)
        {
            // Nothing under the cursor, or the model is still loading: a click means nothing yet.
            if (_shownCrop is null || _shownMask is null) return;
            image = Cutout.Make(_shownCrop.Pixels, _shownMask);
        }

        if (image is null) return;

        (int x, int y) = Monitors.Cursor();
        End(new CutoutResult(CutoutEnd.Picked, image, CursorX: x, CursorY: y));
    }

    /// <summary>
    /// The wheel steps between the decoder's masks by size: up towards the largest - usually the
    /// whole object - and down towards its parts. It stays where it was left for the rest of the
    /// mode. Clamped against how many levels the decoder actually offered last time, not the fixed
    /// maximum of three - once a mask has been untrusted away, going further down holds at the last
    /// real one instead of counting up somewhere the label can never show.
    /// </summary>
    private void ChangeLevel(int wheelDelta)
    {
        int max = Volatile.Read(ref _availableLevels) - 1;
        int level = Volatile.Read(ref _level) + (wheelDelta > 0 ? -1 : 1);
        Volatile.Write(ref _level, Math.Clamp(level, 0, max));
    }

    private void End(CutoutResult result) => _ended.TrySetResult(result);

    private void ShowStatus(string? text, int x, int y)
    {
        int index = IndexOf(x, y);
        for (int i = 0; i < _overlays.Length; i++) _overlays[i].ShowStatus(i == index ? text : null, x, y);
    }

    /// <summary>
    /// Stops the workers before anything they use goes away: the encodings and the model are
    /// disposed only once no encode or decode can still be running on them. The cancellation source
    /// is disposed too - the workers' waits gave it a kernel event, one of the handles the
    /// measurements showed left behind per use.
    /// </summary>
    private async Task StopAsync()
    {
        _stop.Cancel();
        CompositionTarget.Rendering -= OnRendering;

        await Task.WhenAll(_workers);

        foreach (CutoutOverlay overlay in _overlays) overlay.Close();

        if (_crops is not null)
        {
            foreach (EncodedCrop crop in _crops.Items) crop.Dispose();
        }
        _model?.Dispose();
        _stop.Dispose();
    }

    private void Wait(int milliseconds) => _stop.Token.WaitHandle.WaitOne(milliseconds);

    private int IndexOf(int x, int y)
    {
        for (int i = 0; i < _monitors.Count; i++)
        {
            if (_monitors[i].Bounds.Contains(x, y)) return i;
        }
        return -1;
    }

    private static CutoutResult Failed(string error) => new(CutoutEnd.Failed, Error: error);

    private static string FirstLine(Exception ex) => ex.Message.Split('\n')[0].Trim();

    /// <summary>One encoding and the pixels it was made from, so a mask drawn from it and the cutout
    /// a click makes always refer to the same picture.</summary>
    private sealed class EncodedCrop : IDisposable
    {
        public EncodedCrop(SamEmbedding embedding, int pixelBytes)
        {
            Embedding = embedding;
            Pixels = new byte[pixelBytes];
        }

        public SamEmbedding Embedding { get; }

        public byte[] Pixels { get; }

        /// <summary>Written by the encoder before publishing; read only while leased.</summary>
        public EncodeStamp Stamp { get; set; }

        public void Dispose() => Embedding.Dispose();
    }
}
