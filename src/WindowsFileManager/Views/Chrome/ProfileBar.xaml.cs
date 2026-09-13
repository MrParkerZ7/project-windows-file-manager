using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using WindowsFileManager.ViewModels;

namespace WindowsFileManager.Views.Chrome;

/// <summary>
/// Docked profile strip: the profile selector and its create / clone / rename / delete actions.
/// </summary>
[ExcludeFromCodeCoverage]
public partial class ProfileBar : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileBar"/> class.
    /// </summary>
    public ProfileBar()
    {
        InitializeComponent();
    }

    // `this` was the Window when these handlers lived in MainWindow, so the dialog's
    // Owner could be assigned from it directly. It is a UserControl now, so the owning
    // Window has to be looked up. Everything else is verbatim.
    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var dialog = new ProfileNameDialog(
            "New Profile",
            "Create a blank profile",
            "Starts empty — no target folders, no exclude folders, no filters. Use this when you want a clean slate.",
            "New Profile",
            vm.ProfileNames);
        SetOwner(dialog);

        if (dialog.ShowDialog() == true)
        {
            vm.CreateProfileCommand.Execute(dialog.EnteredName);
        }
    }

    private void CloneProfile_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var dialog = new ProfileNameDialog(
            "Clone Profile",
            $"Clone '{vm.ActiveProfileName}'",
            $"Starts as a copy of '{vm.ActiveProfileName}' — same target folders, filters, and tab state. You can then edit it independently.",
            $"Copy of {vm.ActiveProfileName}",
            vm.ProfileNames);
        SetOwner(dialog);

        if (dialog.ShowDialog() == true)
        {
            vm.CloneProfileCommand.Execute(dialog.EnteredName);
        }
    }

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var others = vm.ProfileNames.Where(n => !string.Equals(n, vm.ActiveProfileName, System.StringComparison.OrdinalIgnoreCase));
        var dialog = new ProfileNameDialog(
            "Rename Profile",
            $"Rename '{vm.ActiveProfileName}'",
            "Choose a new name for this profile. The profile's target folders, filters, and tab state stay exactly as they are.",
            vm.ActiveProfileName,
            others);
        SetOwner(dialog);

        if (dialog.ShowDialog() == true)
        {
            vm.RenameProfileCommand.Execute(dialog.EnteredName);
        }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (vm.ProfileNames.Count <= 1)
        {
            MessageBox.Show(
                "You can't delete the last remaining profile.",
                "Delete Profile",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Delete profile '{vm.ActiveProfileName}'? This cannot be undone.",
            "Delete Profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm == MessageBoxResult.Yes)
        {
            vm.DeleteProfileCommand.Execute(null);
        }
    }

    private void SetOwner(Window dialog)
    {
        if (Window.GetWindow(this) is { } owner)
        {
            dialog.Owner = owner;
        }
    }

    // UI Automation parity (T-002): a UserControl creates a UserControlAutomationPeer, so wrapping this
    // markup in one put an unnamed "Custom" node into the accessibility tree that the Grid/Border it
    // replaced never had. Returning no peer keeps that tree exactly as it was before the split.
    // T-011 owns replacing this with named regions - see docs/modules/ui.md.
    protected override AutomationPeer? OnCreateAutomationPeer() => null;
}
