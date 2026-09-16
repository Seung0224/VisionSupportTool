using VirtualPlcServer.Protocols.Mc;
using VirtualPlcServer.ViewModels;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// D영역 워드는 16비트 그대로 저장되지만 화면과 입력은 부호 있는 정수로 다뤄야 한다 -
/// -1을 65535로 보여주면 래더에서 보는 값과 달라지고, 음수는 아예 입력할 수도 없었다.
/// </summary>
public class McNegativeValueTests
{
    [Fact]
    public void Words_are_shown_as_signed_16_bit()
    {
        var map = new McMap(30000, 4);
        map.WriteWord(30001, unchecked((ushort)(short)-1163));

        var viewModel = new McMonitorViewModel(map, "test");

        Assert.Equal((short)-1163, viewModel.Rows[1].Value);
    }

    [Fact]
    public void Writing_a_negative_value_keeps_the_same_bits()
    {
        var map = new McMap(30000, 4);
        var viewModel = new McMonitorViewModel(map, "test");

        // 값이 바뀌면 UI 디스패처로 마샬링하는 핸들러가 붙어 있어서, 테스트에서는 떼고 쓴다.
        viewModel.Detach();
        viewModel.WriteValue(30002, -5);

        Assert.Equal(unchecked((ushort)(short)-5), map.ReadWord(30002));
    }
}
