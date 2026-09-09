using System;
using System.Windows;
using System.Windows.Controls;

namespace WarnoLiteModdingTool.App.Workspaces;

public partial class ProblemWorkspaceView : UserControl
{
    public ProblemWorkspaceView() => InitializeComponent();
    private MainWindow Host => (MainWindow)Window.GetWindow(this);
    private void CopyProblem_Click(object sender, RoutedEventArgs e) => Host.CopyProblem_Click(sender, e);
    private void OpenProblemLogs_Click(object sender, RoutedEventArgs e) => Host.OpenProblemLogs_Click(sender, e);
    private void ProblemAction_Click(object sender,RoutedEventArgs e) => Host.ProblemAction_Click(sender, e);
}
