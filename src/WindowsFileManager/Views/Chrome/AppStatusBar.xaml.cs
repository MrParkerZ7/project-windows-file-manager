using System.Diagnostics.CodeAnalysis;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace WindowsFileManager.Views.Chrome;

/// <summary>
/// Global status bar: busy indicator, status text, progress, count, ETA and the
/// RAM / CPU / thread readout.
/// </summary>
[ExcludeFromCodeCoverage]
public partial class AppStatusBar : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AppStatusBar"/> class.
    /// </summary>
    public AppStatusBar()
    {
        InitializeComponent();
    }

    // UI Automation parity (T-002): a UserControl creates a UserControlAutomationPeer, so wrapping this
    // markup in one put an unnamed "Custom" node into the accessibility tree that the Grid/Border it
    // replaced never had. Returning no peer keeps that tree exactly as it was before the split.
    // T-011 owns replacing this with named regions - see docs/modules/ui.md.
    protected override AutomationPeer? OnCreateAutomationPeer() => null;
}
