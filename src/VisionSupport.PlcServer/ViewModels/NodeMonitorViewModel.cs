using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using VirtualPlcServer.Core;
using VirtualPlcServer.Protocols.Common;

namespace VirtualPlcServer.ViewModels
{
    public sealed partial class NodeRowViewModel : ObservableObject
    {
        public NodeRowViewModel(NodeDefinition definition)
        {
            Definition = definition;
            Refresh();
        }

        public NodeDefinition Definition { get; }

        public string Name => Definition.Name;

        public string DataTypeDisplay => Definition.IsArray
            ? Definition.DataType + "[" + Definition.ArrayLength + "]"
            : Definition.DataType.ToString();

        [ObservableProperty]
        private string valueDisplay;

        [ObservableProperty]
        private DateTime lastUpdated;

        public void Refresh()
        {
            ValueDisplay = FormatValue(Definition.Value);
            LastUpdated = Definition.LastUpdated;
        }

        private static string FormatValue(object value)
        {
            if (value is IEnumerable enumerable && !(value is string))
            {
                return "[" + string.Join(", ", enumerable.Cast<object>()) + "]";
            }

            return value?.ToString() ?? string.Empty;
        }
    }

    public partial class NodeMonitorViewModel : ObservableObject
    {
        private readonly NodeMap _nodeMap;

        public NodeMonitorViewModel(NodeMap nodeMap, string headerInfo)
        {
            _nodeMap = nodeMap;
            HeaderInfo = headerInfo;
            Rows = new ObservableCollection<NodeRowViewModel>();
            LoadAll();
            _nodeMap.ValueChanged += OnValueChanged;
            _nodeMap.NodeAdded += OnNodeAdded;
            _nodeMap.NodeRemoved += OnNodeRemoved;
        }

        public ObservableCollection<NodeRowViewModel> Rows { get; }

        public string HeaderInfo { get; }

        public bool TryAddNode(NodeDefinition definition)
        {
            return _nodeMap.TryAddNode(definition);
        }

        public void RemoveNode(string name)
        {
            _nodeMap.RemoveNode(name);
        }

        public void SetValue(string name, object value)
        {
            _nodeMap.SetValue(name, value);
        }

        public void ClearAll()
        {
            _nodeMap.Clear();
        }

        public void Detach()
        {
            _nodeMap.ValueChanged -= OnValueChanged;
            _nodeMap.NodeAdded -= OnNodeAdded;
            _nodeMap.NodeRemoved -= OnNodeRemoved;
        }

        private void LoadAll()
        {
            Rows.Clear();
            foreach (NodeDefinition definition in _nodeMap.GetAllNodes())
            {
                Rows.Add(new NodeRowViewModel(definition));
            }
        }

        private void OnValueChanged(object sender, MapValueChangedEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                NodeRowViewModel row = Rows.FirstOrDefault(r => r.Name == e.Key);
                row?.Refresh();
            });
        }

        private void OnNodeAdded(object sender, NodeDefinition definition)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (!Rows.Any(r => r.Name == definition.Name))
                {
                    Rows.Add(new NodeRowViewModel(definition));
                }
            });
        }

        private void OnNodeRemoved(object sender, string name)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                NodeRowViewModel row = Rows.FirstOrDefault(r => r.Name == name);
                if (row != null)
                {
                    Rows.Remove(row);
                }
            });
        }
    }
}
