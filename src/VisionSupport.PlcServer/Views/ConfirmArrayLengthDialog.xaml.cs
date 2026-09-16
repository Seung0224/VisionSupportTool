using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VirtualPlcServer.Protocols.OpcUa;

namespace VirtualPlcServer.Views
{
    /// <summary>
    /// 설정 파일로 노드를 만들기 직전에 배열 길이를 확인받는 창. 파일 포맷에 길이가 없어서
    /// 제안값을 그대로 쓰면 클라이언트가 기대하는 길이와 달라질 수 있으므로, 서버를 만들기 전에 반드시 거친다.
    /// </summary>
    public partial class ConfirmArrayLengthDialog : Window
    {
        private readonly List<CfgNodeInfo> _nodes;

        public ConfirmArrayLengthDialog(List<CfgNodeInfo> nodes)
        {
            InitializeComponent();

            _nodes = nodes;
            List<CfgNodeInfo> arrays = nodes.Where(n => n.IsArray).ToList();
            Grid.ItemsSource = arrays;
            SummaryText.Text = "노드 " + nodes.Count + "개 중 배열 " + arrays.Count + "개";
        }

        /// <summary>확인된 길이가 반영된 노드 목록(입력 목록과 같은 객체들이다).</summary>
        public List<CfgNodeInfo> Nodes => _nodes;

        private void OnConfirmClicked(object sender, RoutedEventArgs e)
        {
            // 셀에 커서가 남아있으면 편집 중인 값이 객체에 아직 안 들어가 있다.
            Grid.CommitEdit(DataGridEditingUnit.Row, true);

            CfgNodeInfo invalid = _nodes.FirstOrDefault(n => n.IsArray && n.ArrayLength < 1);
            if (invalid != null)
            {
                ErrorText.Text = invalid.Name + ": 배열 길이는 1 이상이어야 합니다.";
                return;
            }

            DialogResult = true;
        }
    }
}
