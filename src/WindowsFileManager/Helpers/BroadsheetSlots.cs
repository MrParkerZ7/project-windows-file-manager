using System.Diagnostics.CodeAnalysis;
using System.Windows;

namespace WindowsFileManager.Helpers;

/// <summary>
/// Typed content slots for the Broadsheet component templates (decision D1-B on T-003).
/// </summary>
/// <remarks>
/// Two stock controls need one piece of content their templates have no property for: the editor
/// tab strip's right-aligned trailing content (the <c>Scan</c> button and its caption) and a text
/// field's placeholder. These attached properties carry it. They replace riding on <c>Tag</c>,
/// which would fail silently on a misspelling and would carry four unrelated meanings across the
/// epic; a misspelled attached property fails the XAML build instead. Both default to
/// <see langword="null"/>, and the templates bind to that as nothing, so an unset slot lays out as
/// if the slot did not exist.
/// </remarks>
[ExcludeFromCodeCoverage]
public static class BroadsheetSlots
{
    /// <summary>
    /// Identifies the TrailingContent attached property: content shown right-aligned on the tab strip of
    /// a <c>Broadsheet.Style.TabControl.EditorStrip</c> tab control.
    /// </summary>
    public static readonly DependencyProperty TrailingContentProperty =
        DependencyProperty.RegisterAttached(
            "TrailingContent",
            typeof(object),
            typeof(BroadsheetSlots),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Placeholder attached property: the hint text a <c>Broadsheet.Style.TextBox</c> text
    /// field shows while it is empty.
    /// </summary>
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.RegisterAttached(
            "Placeholder",
            typeof(string),
            typeof(BroadsheetSlots),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets the trailing tab-strip content of the specified element.
    /// </summary>
    /// <param name="obj">The dependency object to read from.</param>
    /// <returns>The trailing content, or <see langword="null"/> when none is set.</returns>
    public static object? GetTrailingContent(DependencyObject obj) =>
        obj.GetValue(TrailingContentProperty);

    /// <summary>
    /// Sets the trailing tab-strip content of the specified element.
    /// </summary>
    /// <param name="obj">The dependency object to write to.</param>
    /// <param name="value">The content to show at the right end of the tab strip.</param>
    public static void SetTrailingContent(DependencyObject obj, object? value) =>
        obj.SetValue(TrailingContentProperty, value);

    /// <summary>
    /// Gets the placeholder text of the specified element.
    /// </summary>
    /// <param name="obj">The dependency object to read from.</param>
    /// <returns>The placeholder text, or <see langword="null"/> when none is set.</returns>
    public static string? GetPlaceholder(DependencyObject obj) =>
        (string?)obj.GetValue(PlaceholderProperty);

    /// <summary>
    /// Sets the placeholder text of the specified element.
    /// </summary>
    /// <param name="obj">The dependency object to write to.</param>
    /// <param name="value">The text to show while the field is empty.</param>
    public static void SetPlaceholder(DependencyObject obj, string? value) =>
        obj.SetValue(PlaceholderProperty, value);
}
