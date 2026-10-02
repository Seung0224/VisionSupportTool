using VisionSupport.Wireshark.Capture;
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
    public void The_overall_badge_waits_while_pinned_targets_are_silent_since_start() => _app.Run(() =>
    {
        using var dir = new TempDir();
        using var vm = new WiresharkViewModel(StoreWithPinnedPlc(dir), new ManualClock());

        Assert.Equal(HealthLevel.Idle, vm.OverallLevel);
        Assert.Equal("대기 중", vm.OverallText);
    });

    [Fact]
    public void With_nothing_to_watch_the_badge_says_so() => _app.Run(() =>
    {
        using var dir = new TempDir();
        using var vm = new WiresharkViewModel(new WiresharkSettingsStore(Path.Combine(dir.Path, "s.json")), new ManualClock());

        Assert.Equal(HealthLevel.Idle, vm.OverallLevel);
        Assert.Equal("감시 대상 없음", vm.OverallText);
    });

    /// <summary>On a PC without the CXP grabber there is nothing CXP to show or start.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cxp_controls_follow_whether_the_board_is_present(bool present) => _app.Run(() =>
    {
        using var dir = new TempDir();
        using var vm = new WiresharkViewModel(new WiresharkSettingsStore(Path.Combine(dir.Path, "s.json")),
            new ManualClock(), cxpPresence: () => present);

        Assert.False(vm.HasCxpBoard);
        vm.DetectCxp();

        Assert.Equal(present, vm.HasCxpBoard);
    });

    [Fact]
    public void Disposing_twice_is_harmless() => _app.Run(() =>
    {
        using var dir = new TempDir();
        var vm = new WiresharkViewModel(StoreWithPinnedPlc(dir), new ManualClock());
        vm.Dispose();
        vm.Dispose();
    });

    /// <summary>The NIC card is "everything on this port": focusing it must not filter the list empty.</summary>
    [Fact]
    public void Focusing_the_nic_card_shows_every_packet() => _app.Run(() =>
    {
        using var dir = new TempDir();
        using var vm = new WiresharkViewModel(new WiresharkSettingsStore(Path.Combine(dir.Path, "s.json")), new ManualClock());
        vm.FilterText = "MC";

        vm.FocusCardCommand.Execute(new TargetCardViewModel("00-11-22-33-44-55", TargetKind.Nic));

        Assert.Equal(string.Empty, vm.FilterText);
    });

    [Theory]
    [InlineData(false, 0, "감시를 시작하면 PLC 와 카메라가 여기 나타납니다.")]
    [InlineData(true, 0, "패킷을 기다리는 중입니다…")]
    [InlineData(true, 8015, "패킷 8,015개를 받았지만 주고받은 상대가 없습니다 (방송·그룹 주소만 오갔습니다).")]
    public void The_card_area_says_why_it_is_empty(bool capturing, long packets, string expected)
        => Assert.Equal(expected, WiresharkViewModel.DescribeNoDevices(capturing, packets));

    /// <summary>The port is not a device: its state sits next to the interface picker, not in the card row.</summary>
    [Fact]
    public void The_network_port_shows_beside_the_interface_not_as_a_card() => _app.Run(() =>
    {
        using var dir = new TempDir();
        using var vm = new WiresharkViewModel(new WiresharkSettingsStore(Path.Combine(dir.Path, "s.json")), new ManualClock());

        vm.Health.ReportLink("00-11-22-33-44-55", "PLC", up: false);
        vm.Refresh();

        Assert.Empty(vm.Cards);
        Assert.Equal("끊김", vm.LinkText);
        Assert.Equal(HealthLevel.Bad, vm.LinkLevel);
        Assert.Equal(HealthLevel.Bad, vm.OverallLevel);
    });

    /// <summary>Exactly one card shows as selected, and "전체 보기" clears it.</summary>
    [Fact]
    public void Selecting_a_card_marks_only_that_card() => _app.Run(() =>
    {
        using var dir = new TempDir();
        WiresharkSettingsStore store = StoreWithPinnedPlc(dir);
        WiresharkSettings s = store.Load();
        s.Pinned.Add(new PinnedTarget { Id = "192.168.0.11:5000", Kind = TargetKind.Mc, Name = "로더 PLC" });
        store.Save(s);
        using var vm = new WiresharkViewModel(store, new ManualClock());

        vm.FocusedCard = vm.Cards[0];
        vm.FocusedCard = vm.Cards[1];

        Assert.False(vm.Cards[0].IsSelected);
        Assert.True(vm.Cards[1].IsSelected);
        Assert.Equal("id:" + vm.Cards[1].Id, vm.FilterText);

        vm.ClearFilterCommand.Execute(null);

        Assert.All(vm.Cards, c => Assert.False(c.IsSelected));
        Assert.Equal(string.Empty, vm.FilterText);
    });

    [Fact]
    public void The_list_header_names_the_chosen_connection() => _app.Run(() =>
    {
        using var dir = new TempDir();
        using var vm = new WiresharkViewModel(new WiresharkSettingsStore(Path.Combine(dir.Path, "s.json")), new ManualClock());

        Assert.Equal("모든 연결의 상대", vm.PeerHeader);
        vm.SelectedNic = new NicInfo(9, "34-5A-60-86-4C-F3", "PLC", true);
        Assert.Equal("PLC 연결의 상대", vm.PeerHeader);
    });
}
