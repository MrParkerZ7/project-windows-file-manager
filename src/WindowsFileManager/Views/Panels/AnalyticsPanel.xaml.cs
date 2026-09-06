using System.Diagnostics.CodeAnalysis;
using System.Windows;
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
}
