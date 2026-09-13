using System.Diagnostics.CodeAnalysis;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace WindowsFileManager.Views.Screens;

/// <summary>
/// History screen: the global operation log and its controls.
/// </summary>
[ExcludeFromCodeCoverage]
public partial class HistoryScreen : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HistoryScreen"/> class.
    /// </summary>
    public HistoryScreen()
    {
        InitializeComponent();
    }

    // UI Automation parity (T-002): a UserControl creates a UserControlAutomationPeer, so wrapping this
    // markup in one put an unnamed "Custom" node into the accessibility tree that the Grid/Border it
    // replaced never had. Returning no peer keeps that tree exactly as it was before the split.
    // T-011 owns replacing this with named regions - see docs/modules/ui.md.
    protected override AutomationPeer? OnCreateAutomationPeer() => null;
}
