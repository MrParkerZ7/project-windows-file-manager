using System.Diagnostics.CodeAnalysis;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace WindowsFileManager.Views.Screens;

/// <summary>
/// Duplicate-detection screen: filters, custom rules, the group list and the action bar.
/// </summary>
[ExcludeFromCodeCoverage]
public partial class DuplicatesScreen : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicatesScreen"/> class.
    /// </summary>
    public DuplicatesScreen()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Raised when the duplicate-group selection changes and any playing media should stop.
    /// </summary>
    /// <remarks>
    /// MainWindow used to call <c>VideoPlayer.Stop()</c> and <c>AudioPlayer.Stop()</c>
    /// directly from this screen's handler. Those players now live in PreviewPanel, a
    /// sibling control, so the reach-across is re-expressed as an event that the
    /// composition root wires up. The source is deliberately a SelectionChanged and not
    /// a property change — T-008 inherits that as a behavioural constraint.
    /// </remarks>
    public event EventHandler? GroupSelectionChanged;

    private void DuplicateGroups_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => GroupSelectionChanged?.Invoke(this, EventArgs.Empty);

    // UI Automation parity (T-002): a UserControl creates a UserControlAutomationPeer, so wrapping this
    // markup in one put an unnamed "Custom" node into the accessibility tree that the Grid/Border it
    // replaced never had. Returning no peer keeps that tree exactly as it was before the split.
    // T-011 owns replacing this with named regions - see docs/modules/ui.md.
    protected override AutomationPeer? OnCreateAutomationPeer() => null;
}
