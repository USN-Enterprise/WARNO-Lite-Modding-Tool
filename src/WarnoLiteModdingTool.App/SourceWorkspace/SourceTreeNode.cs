using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace WarnoLiteModdingTool.App.SourceWorkspace;

public sealed class SourceTreeNode(string path) : INotifyPropertyChanged
{
    public string Path { get; } = path;
    public ObservableCollection<SourceTreeNode> Children { get; } = [];
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('/')) + (_draft ? " ◇" : "");
    private bool _draft;
    public bool Draft { get => _draft; set { if (_draft == value) return; _draft = value; PropertyChanged?.Invoke(this, new(nameof(Name))); } }
    private Visibility _visibility;
    public Visibility Visibility { get => _visibility; set { _visibility = value; PropertyChanged?.Invoke(this, new(nameof(Visibility))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}
