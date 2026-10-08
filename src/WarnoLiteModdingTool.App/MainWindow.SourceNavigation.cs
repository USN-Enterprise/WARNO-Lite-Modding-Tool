using System.Windows;
using WarnoLiteModdingTool.App.SourceWorkspace;

namespace WarnoLiteModdingTool.App;

public partial class MainWindow
{
    private bool _sourceNavigating;
    internal async Task OpenBusinessSourceAsync(BusinessSourceTarget target, FrameworkElement origin)
    {
        if (!_viewModel.AdvancedMode) return;
        if (_sourceNavigating || _viewModel.SourceOperationBusy) throw new InvalidOperationException(Localisation.UiText.T("工作台正在处理操作，请稍后重试。"));
        _sourceNavigating = true;
        try
        {
            var store = _viewModel.SourceDraftStore;
            var module = _viewModel.SelectedModule;
            var originWindow = Window.GetWindow(origin);
            var restore = SourceOrigin.Capture(_viewModel, origin);
            await _viewModel.SaveBeforeLeavingAsync();
            if (!ReferenceEquals(store, _viewModel.SourceDraftStore)) return;
            if (_sourceWindow is null)
            {
                _sourceWindow = new SourceWorkspaceWindow(_viewModel) { Owner = this };
                _sourceWindow.Closed += (_, _) => _sourceWindow = null; _sourceWindow.Show();
            }
            var workspace = _sourceWindow;
            await workspace.Initialization;
            if (!ReferenceEquals(store, _viewModel.SourceDraftStore)) return;
            await workspace.NavigateBusinessAsync(target, () =>
            {
                if (!ReferenceEquals(store, _viewModel.SourceDraftStore)) return;
                _viewModel.SelectedModule = module; restore();
                var window = originWindow?.IsVisible == true ? originWindow : this;
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Activate();
            });
            if (workspace.WindowState == WindowState.Minimized) workspace.WindowState = WindowState.Normal;
            workspace.Activate();
        }
        finally { _sourceNavigating = false; }
    }
}
