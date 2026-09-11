using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VisionSupport.Launcher;
using VisionSupport.Shell;

namespace VisionSupport.Archive;

/// <summary>
/// What happens when something is dropped on the launcher icon.
///
/// One job at a time, deliberately. The icon is a single target and a second drop while the first
/// is still running would have two archives deciding the same output name; refusing is honest and
/// the busy state is visible on the icon.
///
/// Passwords are asked for on the UI thread before the work starts, so the prompt never appears
/// over a progress display, and are never written anywhere.
/// </summary>
public sealed partial class DropCoordinator : ObservableObject
{
    private readonly LauncherSettings _settings;
    private readonly ActivityLog _activity;
    private readonly ZipService _zip = new();
    private readonly CopyService _copy = new();

    private string _label = "압축";

    /// <summary>0-100 while a job runs. The icon shows it in place of its mark.</summary>
    [ObservableProperty]
    private int _percent;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private DropState _state = DropState.Idle;

    /// <summary>Returns the icon to its resting face a couple of seconds after a job ends.</summary>
    private DispatcherTimer? _settle;

    public DropCoordinator(LauncherSettings settings, ActivityLog activity)
    {
        _settings = settings;
        _activity = activity;
    }

    /// <summary>True when a drop would be accepted - used to set the drag cursor honestly.</summary>
    public bool CanAccept => !IsBusy;

    /// <summary>
    /// What the icon shows in place of its mark, taken from the original: the percentage while
    /// working, a tick or a bang when it finishes, and the launcher's own V the rest of the time.
    /// </summary>
    public string Mark => State switch
    {
        DropState.Working => $"{Percent}%",
        DropState.Done => "✓",
        DropState.Failed => "!",
        _ => "V",
    };

    /// <summary>
    /// How much of the mark's usual size to use. "100%" is four characters where "V" is one, so
    /// the percentage is set smaller or it runs off the edge of the circle.
    /// </summary>
    public double MarkScale => State == DropState.Working ? 0.52 : 1.0;

    /// <summary>
    /// Runs the drop. Returns when the job is finished, having reported the outcome to the
    /// activity log rather than to a message box - the launcher has nowhere to show one that
    /// would not be in the way.
    /// </summary>
    public async Task HandleAsync(string[] paths)
    {
        if (IsBusy) return;

        switch (DropRouter.Decide(paths, _settings.CopyMode))
        {
            case DropAction.Copy:
                await RunCopy(paths).ConfigureAwait(true);
                return;

            case DropAction.Extract:
                await RunExtract(paths).ConfigureAwait(true);
                return;

            case DropAction.Compress:
                await RunCompress(paths).ConfigureAwait(true);
                return;
        }
    }

    private async Task RunCopy(string[] paths)
    {
        if (string.IsNullOrWhiteSpace(_settings.CopyTargetFolder))
        {
            _activity.Add("압축", "복사 대상 폴더가 없습니다 — 우클릭 메뉴에서 지정하세요");
            return;
        }

        await Run("복사", progress =>
            _copy.CopyAsync(paths, _settings.CopyTargetFolder, progress, CancellationToken.None))
            .ConfigureAwait(true);
    }

    private async Task RunExtract(string[] paths)
    {
        // Asked before the work starts and off the busy state, so the prompt is not competing
        // with a progress readout on the same icon.
        string? password = null;
        if (await Task.Run(() => ZipService.AnyRequiresPassword(paths)).ConfigureAwait(true))
        {
            password = PasswordDialog.AskToOpen();
            if (password is null)
            {
                _activity.Add("압축 해제", "취소됨");
                return;
            }
        }

        await Run("압축 해제", progress =>
            _zip.ExtractAsync(paths, password, progress, CancellationToken.None))
            .ConfigureAwait(true);
    }

    private async Task RunCompress(string[] paths)
    {
        string? password = null;
        if (_settings.UsePassword)
        {
            password = PasswordDialog.AskToLock();
            if (password is null)
            {
                _activity.Add("압축", "취소됨");
                return;
            }
        }

        await Run("압축", progress =>
            _zip.CompressAsync(paths, password, _settings.FastCompress, progress, CancellationToken.None))
            .ConfigureAwait(true);
    }

    private async Task Run(string label, Func<IProgress<int>, Task<ArchiveResult>> job)
    {
        _settle?.Stop();
        _label = label;
        IsBusy = true;
        Percent = 0;
        State = DropState.Working;

        try
        {
            // Progress<T> posts back to the thread that made it, which is the UI thread here, so
            // the icon updates without any dispatcher juggling of its own.
            var progress = new Progress<int>(p => Percent = p);
            ArchiveResult result = await job(progress).ConfigureAwait(true);

            _activity.Add(label, result.Success
                ? $"완료 — {result.OutputPath}"
                : result.Error ?? "실패");

            Finish(result.Success);
        }
        catch (Exception ex)
        {
            // A drop must never take the launcher down with it; the log is where it surfaces.
            _activity.Add(label, "실패 — " + ex.Message);
            Finish(false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Leaves the outcome on the icon for a moment, then puts the mark back.
    ///
    /// Without the pause the icon would snap from 99% straight to its resting face, and a job
    /// that took two seconds would be indistinguishable from one that never started.
    /// </summary>
    private void Finish(bool succeeded)
    {
        State = succeeded ? DropState.Done : DropState.Failed;

        _settle?.Stop();
        _settle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _settle.Tick += (_, _) =>
        {
            _settle!.Stop();
            State = DropState.Idle;
            Percent = 0;
        };
        _settle.Start();
    }

    partial void OnStateChanged(DropState value)
    {
        OnPropertyChanged(nameof(Mark));
        OnPropertyChanged(nameof(MarkScale));
    }

    partial void OnPercentChanged(int value)
    {
        OnPropertyChanged(nameof(Mark));
    }

    /// <summary>Pulls the file list out of a drop, or null when it carries no files.</summary>
    public static string[]? FilesFrom(IDataObject data)
        => data.GetDataPresent(DataFormats.FileDrop) ? data.GetData(DataFormats.FileDrop) as string[] : null;
}

/// <summary>What the launcher icon is in the middle of, as far as a drop is concerned.</summary>
public enum DropState
{
    Idle,

    Working,

    Done,

    Failed,
}
