using System.Diagnostics.CodeAnalysis;
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
}
