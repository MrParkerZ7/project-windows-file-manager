using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using WindowsFileManager.ViewModels;

namespace WindowsFileManager.Views;

/// <summary>
/// Composition root. Owns window geometry, tab state, and the single cross-control
/// wire that the T-002 decomposition could not express in XAML.
/// </summary>
[ExcludeFromCodeCoverage]
public partial class MainWindow : Window
{
    private bool _savedPreviewVisible;
    private bool _savedAnalyticsVisible;
    private bool _isWindowLoaded;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;
        Loaded += MainWindow_Loaded;

        DuplicatesView.GroupSelectionChanged += OnDuplicateGroupSelectionChanged;
    }

    /// <summary>
    /// The one real coupling the decomposition had to re-express. The Duplication list's
    /// <c>SelectionChanged</c> used to stop the preview players by naming them directly;
    /// they are sibling controls now, and only the composition root legitimately knows
    /// about both, so the reach-across is routed through here.
    /// </summary>
    /// <param name="sender">The screen that raised it.</param>
    /// <param name="e">Always <see cref="EventArgs.Empty"/>.</param>
    private void OnDuplicateGroupSelectionChanged(object? sender, EventArgs e) => PreviewView.StopMedia();

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var settings = vm.GetSettings();
            if (settings.WindowWidth != null && settings.WindowHeight != null)
            {
                var left = settings.WindowLeft ?? 0;
                var top = settings.WindowTop ?? 0;
                var width = settings.WindowWidth.Value;
                var height = settings.WindowHeight.Value;

                // Check if the saved position is visible on any current monitor
                // Uses virtual screen bounds (spans all monitors)
                var isOnScreen = left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                                 left + width > SystemParameters.VirtualScreenLeft &&
                                 top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight &&
                                 top + height > SystemParameters.VirtualScreenTop;

                if (isOnScreen)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = left;
                    Top = top;
                    Width = width;
                    Height = height;
                }

                if (settings.IsMaximized)
                {
                    WindowState = WindowState.Maximized;
                }
            }

            // Save initial panel states for tab switching
            _savedPreviewVisible = vm.IsPreviewVisible;
            _savedAnalyticsVisible = vm.IsAnalyticsVisible;

            // Folder Control is the first tab — hide panels on startup
            vm.IsPreviewVisible = false;
            vm.IsAnalyticsVisible = false;
            vm.IsFolderControlActive = true;
        }

        _isWindowLoaded = true;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            // Save window state using RestoreBounds (gives normal size even when maximized)
            var bounds = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
            vm.SaveWindowState(bounds.Left, bounds.Top, bounds.Width, bounds.Height, WindowState == WindowState.Maximized);
            vm.SaveSettings();
        }
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isWindowLoaded || sender is not TabControl tabControl || e.Source != tabControl || DataContext is not MainViewModel vm)
        {
            return;
        }

        // `Header as string` is load-bearing. The History TabItem's header is a TextBlock,
        // not a string, so it takes the else branch and IsHistoryActive is never set true.
        // Documented as an invariant in docs/modules/ui.md rule 18, SPEC-005:140 and
        // SPEC-006:127 — do not "tidy" that header into a plain string.
        var header = (tabControl.SelectedItem as TabItem)?.Header as string;

        if (header == "Folder")
        {
            // Save current panel states and hide them
            _savedPreviewVisible = vm.IsPreviewVisible;
            _savedAnalyticsVisible = vm.IsAnalyticsVisible;
            vm.IsPreviewVisible = false;
            vm.IsAnalyticsVisible = false;
            vm.IsFolderControlActive = true;
            vm.IsHistoryActive = false;
        }
        else if (header == "History")
        {
            _savedPreviewVisible = vm.IsPreviewVisible;
            _savedAnalyticsVisible = vm.IsAnalyticsVisible;
            vm.IsPreviewVisible = false;
            vm.IsAnalyticsVisible = false;
            vm.IsFolderControlActive = false;
            vm.IsHistoryActive = true;
        }
        else
        {
            // Restore panel states when switching back
            vm.IsFolderControlActive = false;
            vm.IsHistoryActive = false;
            vm.IsPreviewVisible = _savedPreviewVisible;
            vm.IsAnalyticsVisible = _savedAnalyticsVisible;
        }
    }
}
