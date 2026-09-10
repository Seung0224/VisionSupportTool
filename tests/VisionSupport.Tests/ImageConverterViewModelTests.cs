using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VisionSupport.ImageConverter;
using VisionSupport.ImageConverter.Services;
using VisionSupport.ImageConverter.ViewModels;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// The queue-building and batch-running logic, with the actual pixel work stubbed out through a
/// fake runner so these stay fast and deterministic.
/// </summary>
public class ImageConverterViewModelTests
{
    [Fact]
    public void AddPaths_queues_only_files_with_a_supported_image_extension()
    {
        using var dir = new TempDir();
        string png = Touch(dir, "a.png");
        string jpg = Touch(dir, "c.JPG");
        Touch(dir, "b.txt");
        Touch(dir, "notes.docx");
        var vm = NewViewModel(out _);

        vm.AddPaths(new[] { png, jpg, Path.Combine(dir.Path, "b.txt"), Path.Combine(dir.Path, "notes.docx") });

        Assert.Equal(new[] { png, jpg }, vm.Queue.Select(i => i.SourcePath).OrderBy(p => p));
    }

    [Fact]
    public void AddPaths_expands_a_folder_and_honours_the_recurse_toggle()
    {
        using var dir = new TempDir();
        Touch(dir, "top.png");
        string sub = Path.Combine(dir.Path, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "deep.bmp"), "");

        var vm = NewViewModel(out _);
        vm.RecurseFolders = false;
        vm.AddPaths(new[] { dir.Path });
        Assert.Equal(new[] { "top.png" }, vm.Queue.Select(i => Path.GetFileName(i.SourcePath)));

        vm.ClearQueue();
        vm.RecurseFolders = true;
        vm.AddPaths(new[] { dir.Path });
        Assert.Equal(
            new[] { "deep.bmp", "top.png" },
            vm.Queue.Select(i => Path.GetFileName(i.SourcePath)).OrderBy(n => n));
    }

    [Fact]
    public void AddPaths_ignores_a_path_already_in_the_queue()
    {
        using var dir = new TempDir();
        string png = Touch(dir, "a.png");
        var vm = NewViewModel(out _);

        vm.AddPaths(new[] { png });
        vm.AddPaths(new[] { png });

        Assert.Single(vm.Queue);
    }

    [Fact]
    public async Task ConvertAllAsync_runs_every_item_and_marks_it_done()
    {
        using var dir = new TempDir();
        string a = Touch(dir, "a.png");
        string b = Touch(dir, "b.png");
        var vm = NewViewModel(out FakeRunner runner);
        vm.Options.OutputFolder = @"D:\out";
        vm.AddPaths(new[] { a, b });

        await vm.ConvertAllAsync(CancellationToken.None);

        Assert.All(vm.Queue, i => Assert.Equal(ItemStatus.Done, i.Status));
        Assert.Equal(new[] { a, b }, runner.Converted.OrderBy(p => p));
    }

    [Fact]
    public async Task ConvertAllAsync_keeps_going_after_one_item_fails()
    {
        using var dir = new TempDir();
        string a = Touch(dir, "a.png");
        string b = Touch(dir, "b.png");
        string c = Touch(dir, "c.png");
        var vm = NewViewModel(out FakeRunner runner);
        runner.FailOn = b;
        vm.Options.OutputFolder = @"D:\out";
        vm.AddPaths(new[] { a, b, c });

        await vm.ConvertAllAsync(CancellationToken.None);

        Assert.Equal(ItemStatus.Done, vm.Queue[0].Status);
        Assert.Equal(ItemStatus.Failed, vm.Queue[1].Status);
        Assert.Equal(ItemStatus.Done, vm.Queue[2].Status);
        Assert.False(string.IsNullOrEmpty(vm.Queue[1].Message));
    }

    [Fact]
    public async Task ConvertAllAsync_marks_the_remaining_items_skipped_when_cancelled()
    {
        using var dir = new TempDir();
        string a = Touch(dir, "a.png");
        string b = Touch(dir, "b.png");
        string c = Touch(dir, "c.png");
        using var cts = new CancellationTokenSource();
        var vm = NewViewModel(out FakeRunner runner);
        runner.OnConvert = _ => cts.Cancel();
        vm.Options.OutputFolder = @"D:\out";
        vm.AddPaths(new[] { a, b, c });

        await vm.ConvertAllAsync(cts.Token);

        Assert.Equal(ItemStatus.Done, vm.Queue[0].Status);
        Assert.Equal(ItemStatus.Skipped, vm.Queue[1].Status);
        Assert.Equal(ItemStatus.Skipped, vm.Queue[2].Status);
    }

    [Fact]
    public void Summary_reports_the_pending_count_before_a_run()
    {
        using var dir = new TempDir();
        var vm = NewViewModel(out _);
        vm.AddPaths(new[] { Touch(dir, "a.png"), Touch(dir, "b.png") });

        Assert.Equal("대기 2개", vm.Summary);
    }

    [Fact]
    public async Task Summary_reports_done_and_failed_after_a_run()
    {
        using var dir = new TempDir();
        string a = Touch(dir, "a.png");
        string b = Touch(dir, "b.png");
        string c = Touch(dir, "c.png");
        var vm = NewViewModel(out FakeRunner runner);
        runner.FailOn = b;
        vm.Options.OutputFolder = @"D:\out";
        vm.AddPaths(new[] { a, b, c });

        await vm.ConvertAllAsync(CancellationToken.None);

        Assert.Equal("완료 2 · 실패 1", vm.Summary);
    }

    private static string Touch(TempDir dir, string name)
    {
        string path = Path.Combine(dir.Path, name);
        File.WriteAllText(path, "");
        return path;
    }

    private static ImageConverterViewModel NewViewModel(out FakeRunner runner)
    {
        runner = new FakeRunner();
        return new ImageConverterViewModel(runner, new ImageConverterSettings(), idbAvailable: false);
    }

    private sealed class FakeRunner : IConversionRunner
    {
        public List<string> Converted { get; } = new();
        public string? FailOn { get; set; }
        public Action<string>? OnConvert { get; set; }

        public string Convert(string sourcePath, string outputFolder, ConversionOptions options)
        {
            OnConvert?.Invoke(sourcePath);
            if (sourcePath == FailOn) throw new InvalidOperationException("boom");
            Converted.Add(sourcePath);
            return Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(sourcePath) + ".png");
        }
    }
}
