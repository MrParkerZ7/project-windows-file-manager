using System.Diagnostics.CodeAnalysis;
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
}
