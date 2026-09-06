using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WindowsFileManager.Core.Models;
using WindowsFileManager.Views.Support;

namespace WindowsFileManager.Views.Screens;

/// <summary>
/// Folder search screen: the search controls and the sortable results ListView.
/// </summary>
[ExcludeFromCodeCoverage]
public partial class FoldersScreen : UserControl
{
    private GridViewColumnHeader? _lastFolderSortHeader;
    private ListSortDirection _lastFolderSortDirection = ListSortDirection.Ascending;

    /// <summary>
    /// Initializes a new instance of the <see cref="FoldersScreen"/> class.
    /// </summary>
    public FoldersScreen()
    {
        InitializeComponent();
    }

    private void FolderResults_ColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header || header.Role == GridViewColumnHeaderRole.Padding)
        {
            return;
        }

        // Determine the sort property. Known columns override the binding path so that
        // formatted display strings (e.g. "500 KB" / "2 GB") don't get sorted lexicographically.
        string? sortBy = (header.Column?.Header as string)?.TrimEnd(' ', '▲', '▼') switch
        {
            "Size" => nameof(FolderSearchResult.TotalSize),
            "Full Path" => nameof(FolderSearchResult.FullPath),
            _ => null,
        };

        if (sortBy == null && header.Column?.DisplayMemberBinding is Binding binding)
        {
            sortBy = binding.Path.Path;
        }

        if (sortBy == null)
        {
            return;
        }

        // Toggle direction if same column clicked again
        var direction = ListSortDirection.Ascending;
        if (header == _lastFolderSortHeader)
        {
            direction = _lastFolderSortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        }

        _lastFolderSortHeader = header;
        _lastFolderSortDirection = direction;

        if (sender is ListView listView)
        {
            var view = CollectionViewSource.GetDefaultView(listView.ItemsSource);
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(sortBy, direction));

            // Update header text with sort indicator
            foreach (var col in ((GridView)listView.View).Columns)
            {
                if (col.Header is string h)
                {
                    col.Header = h.TrimEnd(' ', '▲', '▼');
                }
            }

            var arrow = direction == ListSortDirection.Ascending ? " ▲" : " ▼";
            if (header.Column?.Header is string currentHeader)
            {
                header.Column.Header = currentHeader.TrimEnd(' ', '▲', '▼') + arrow;
            }
        }
    }

    private void IntegerTextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        => NumericInputFilter.HandleDigitsOnly(e);
}
