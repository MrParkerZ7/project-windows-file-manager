using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using WindowsFileManager.Core.Models;
using WindowsFileManager.Views.Support;

namespace WindowsFileManager.Views.Panels;

/// <summary>
/// Folder action panel: subfolder paging, file-type filters and the flatten controls.
/// </summary>
[ExcludeFromCodeCoverage]
public partial class FolderActionPanel : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FolderActionPanel"/> class.
    /// </summary>
    public FolderActionPanel()
    {
        InitializeComponent();
    }

    private void SubfolderPrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is SubfolderItem item)
        {
            item.PrevPage();
        }
    }

    private void SubfolderNextPage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is SubfolderItem item)
        {
            item.NextPage();
        }
    }

    private void IntegerTextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        => NumericInputFilter.HandleDigitsOnly(e);
}
