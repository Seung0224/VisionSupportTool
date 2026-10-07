using System.Windows;
using System.Windows.Controls;
using VirtualPlcServer.ViewModels;

namespace VirtualPlcServer.Views
{
    /// <summary>보드 맨 끝에 붙는 "ADD MODULE" 자리. 모듈과 같은 격자/목록 안에서 같은 크기로 그려지게
    /// 모듈 목록 뒤에 항목 하나로 끼워 넣는다(PlcServerView의 CompositeCollection).</summary>
    public sealed class AddModuleTile
    {
    }

    /// <summary>보드 항목이 모듈이면 모듈 템플릿을, ADD MODULE 자리면 추가 템플릿을 고른다.
    /// 카드형/목록형이 각자 한 벌씩 갖고, 보기 방식이 바뀌면 스타일이 선택기를 통째로 바꾼다.</summary>
    public sealed class BoardItemTemplateSelector : DataTemplateSelector
    {
        public DataTemplate ModuleTemplate { get; set; }

        public DataTemplate AddTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            return item is ModuleViewModel ? ModuleTemplate : AddTemplate;
        }
    }
}
