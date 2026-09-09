using System;
using System.Windows;
using System.Windows.Controls;

namespace WarnoLiteModdingTool.App.Workspaces;

public partial class ObjectWorkspaceView : UserControl
{
    public ObjectWorkspaceView() => InitializeComponent();
    private MainWindow Host => (MainWindow)Window.GetWindow(this);
    private void ObjectIndex_Sorting(object sender, DataGridSortingEventArgs e) => Host.ObjectIndex_Sorting(sender, e);
}
