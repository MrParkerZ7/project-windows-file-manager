using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;

namespace WindowsFileManager.Views.Support;

/// <summary>
/// Digits-only text-input filter shared by the two integer TextBoxes that used to
/// share MainWindow's single <c>IntegerTextBox_PreviewTextInput</c> handler. They now
/// live in different UserControls (FoldersScreen and FolderActionPanel), so the one
/// behaviour they share is held here rather than duplicated into both.
/// </summary>
[ExcludeFromCodeCoverage]
public static class NumericInputFilter
{
    /// <summary>
    /// Marks the composition handled unless every character in it is a digit.
    /// </summary>
    /// <param name="e">The text composition being previewed.</param>
    public static void HandleDigitsOnly(TextCompositionEventArgs e)
    {
        foreach (var ch in e.Text)
        {
            if (!char.IsDigit(ch))
            {
                e.Handled = true;
                return;
            }
        }
    }
}
