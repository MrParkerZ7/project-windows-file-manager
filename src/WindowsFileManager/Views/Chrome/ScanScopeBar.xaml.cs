using System.Diagnostics.CodeAnalysis;
using System.Windows.Controls;

namespace WindowsFileManager.Views.Chrome;

/// <summary>
/// Scan scope row: the Target Folders and Exclude Folders editors, side by side.
/// </summary>
[ExcludeFromCodeCoverage]
public partial class ScanScopeBar : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScanScopeBar"/> class.
    /// </summary>
    public ScanScopeBar()
    {
        InitializeComponent();
    }
}
