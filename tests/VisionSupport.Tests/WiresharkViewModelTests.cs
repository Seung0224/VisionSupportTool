using System.IO;
using VisionSupport.Wireshark.Health;
using VisionSupport.Wireshark.Settings;
using VisionSupport.Wireshark.ViewModels;
using Xunit;

namespace VisionSupport.Tests;

[Collection(StaAppCollection.Name)]
public class WiresharkViewModelTests
{
    private readonly StaAppFixture _app;

    public WiresharkViewModelTests(StaAppFixture app) => _app = app;

    private static WiresharkSettingsStore StoreWithPinnedPlc(TempDir dir)
    {
        var store = new WiresharkSettingsStore(Path.Combine(dir.Path, "settings.json"));
        var settings = new WiresharkSettings();
        settings.Pinned.Add(new PinnedTarget { Id = "192.168.0.10:5000", Kind = TargetKind.Mc, Name = "검사기 PLC" });
        store.Save(settings);
        return store;
    }

    [Fact]
    public void Pinned_targets_show_as_grey_cards_before_anything_runs() => _app.Run(() =>
    {
        using var dir = new TempDir();
        using var vm = new WiresharkViewModel(StoreWithPinnedPlc(dir), new ManualClock());

        TargetCardViewModel card = Assert.Single(vm.Cards);
        Assert.Equal("검사기 PLC", card.Name);
        Assert.Equal(HealthLevel.Idle, card.Level);
        Assert.Equal("트래픽 없음", card.Summary);
        Assert.False(vm.IsWorking);
        Assert.False(vm.HasAlert);
    });

    [Fact]
    public void Unpinning_is_saved_and_drops_a_card_never_seen() => _app.Run(() =>
    {
        using var dir = new TempDir();
        WiresharkSettingsStore store = StoreWithPinnedPlc(dir);
        using (var vm = new WiresharkViewModel(store, new ManualClock()))
        {
            vm.TogglePinCommand.Execute(vm.Cards[0]);
            Assert.Empty(vm.Cards);
        }

        Assert.Empty(store.Load().Pinned);
    });

    [Fact]
    public void Focusing_a_card_filters_the_list_to_it() => _app.Run(() =>
    {
        using var dir = new TempDir();
        using var vm = new WiresharkViewModel(StoreWithPinnedPlc(dir), new ManualClock());

        vm.FocusCardCommand.Execute(vm.Cards[0]);

        Assert.Equal("id:192.168.0.10:5000", vm.FilterText);
        Assert.Same(vm.Cards[0], vm.FocusedCard);
    });

    [Fact]
    public void Disposing_twice_is_harmless() => _app.Run(() =>
    {
        using var dir = new TempDir();
        var vm = new WiresharkViewModel(StoreWithPinnedPlc(dir), new ManualClock());
        vm.Dispose();
        vm.Dispose();
    });
}
