using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace WindowsFileManager.Views;

/// <summary>
/// Modal dialog for entering or editing a profile name. Handles inline duplicate validation, Enter/Escape keys, and pre-selection of the initial value.
/// </summary>
[ExcludeFromCodeCoverage]
public partial class ProfileNameDialog : Window
{
    private readonly HashSet<string> _reservedNames;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileNameDialog"/> class.
    /// </summary>
    /// <param name="title">Window title.</param>
    /// <param name="headline">Bold headline shown at the top of the dialog.</param>
    /// <param name="prompt">One-line instruction shown below the headline.</param>
    /// <param name="initialValue">Pre-filled value (selected on open).</param>
    /// <param name="reservedNames">Names that must be rejected as duplicates (case-insensitive). Typically pass other profile names — exclude the current name if renaming.</param>
    public ProfileNameDialog(string title, string headline, string prompt, string initialValue, IEnumerable<string> reservedNames)
    {
        _reservedNames = new HashSet<string>(reservedNames, System.StringComparer.OrdinalIgnoreCase);
        InitializeComponent();
        Title = title;
        HeadlineText.Text = headline;
        PromptText.Text = prompt;
        NameInput.Text = initialValue;

        Loaded += (_, _) =>
        {
            NameInput.Focus();
            NameInput.SelectAll();
            Validate();
        };
    }

    /// <summary>
    /// Gets the trimmed name the user entered. Valid only when <see cref="Window.ShowDialog"/> returns <c>true</c>.
    /// </summary>
    public string EnteredName => NameInput.Text.Trim();

    private void NameInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Validate();

    private void NameInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && OkButton.IsEnabled)
        {
            Ok_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate())
        {
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private bool Validate()
    {
        var value = NameInput.Text.Trim();
        if (value.Length == 0)
        {
            ShowError("Name can't be empty.");
            return false;
        }

        if (_reservedNames.Contains(value))
        {
            ShowError($"A profile named '{value}' already exists.");
            return false;
        }

        HideError();
        return true;
    }

    private void ShowError(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;

        // Resolved from the palette, with the shipped literal as the fallback so a missing
        // dictionary degrades to the same red rather than to no border highlight at all.
        NameInput.BorderBrush = this.TryFindResource("Legacy.Brush.Danger.IndianRed") as System.Windows.Media.Brush
            ?? System.Windows.Media.Brushes.IndianRed;
        OkButton.IsEnabled = false;
    }

    private void HideError()
    {
        ValidationText.Visibility = Visibility.Collapsed;
        NameInput.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
        OkButton.IsEnabled = true;
    }
}
