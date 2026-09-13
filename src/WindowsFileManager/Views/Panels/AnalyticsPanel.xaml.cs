using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using WindowsFileManager.ViewModels;

namespace WindowsFileManager.Views.Panels;

/// <summary>
/// Analytics dashboard panel.
/// </summary>
[ExcludeFromCodeCoverage]
public partial class AnalyticsPanel : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AnalyticsPanel"/> class.
    /// </summary>
    public AnalyticsPanel()
    {
        InitializeComponent();
    }

    private void CloseAnalytics_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.IsAnalyticsVisible = false;
        }
    }

    // UI Automation parity (T-002): a UserControl creates a UserControlAutomationPeer, so wrapping this
    // markup in one put an unnamed "Custom" node into the accessibility tree that the Grid/Border it
    // replaced never had. Returning no peer keeps that tree exactly as it was before the split.
    // T-011 owns replacing this with named regions - see docs/modules/ui.md.
    protected override AutomationPeer? OnCreateAutomationPeer() => null;
}
