using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using WindowsFileManager.Helpers;
using WindowsFileManager.Tests.Views;
using Xunit;
using WpfApp = System.Windows.Application;

namespace WindowsFileManager.Tests.Themes;

/// <summary>
/// Runtime gate for the Broadsheet component style library (T-003): every style, resolved through the real
/// <see cref="WpfApp"/> resources, applied to a live control of its target type.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BroadsheetComponentDictionaryTests"/> reads the dictionaries as XML, and that proved not to be
/// enough. The library was first built as four family files under one aggregator: every XML check passed and the
/// build was green, yet at runtime each style's <c>FocusVisualStyle</c> resolved to nothing and the drop-down
/// template threw "Cannot find resource", because WPF does not resolve a <c>{StaticResource}</c> from one merged
/// dictionary into a sibling. Only building the control finds that. This test does, for every style.
/// </para>
/// <para>
/// No window is shown and no message loop runs — it uses <see cref="WpfAppHost"/>, the suite's single application.
/// Layout is run explicitly where a check needs it (<c>Measure</c>/<c>Arrange</c> work without a window); nothing is
/// rendered.
/// </para>
/// </remarks>
public class BroadsheetStyleRuntimeTests
{
    /// <summary>Template parts each control looks up by literal name at runtime.</summary>
    private static readonly Dictionary<string, string[]> RequiredParts = new(StringComparer.Ordinal)
    {
        ["Broadsheet.Style.TextBox"] = new[] { "PART_ContentHost", "PART_Placeholder" },
        ["Broadsheet.Style.ComboBox"] = new[] { "PART_Popup", "PART_ToggleButton", "ContentSite" },
        ["Broadsheet.Style.TabControl.EditorStrip"] = new[] { "HeaderPanel", "StripRightHost", "PART_SelectedContentHost" },
        ["Broadsheet.Style.Tab.Editor"] = new[] { "TabRoot", "HeaderHost" },
        ["Broadsheet.Style.ListBoxItem.Row"] = new[] { "Bd", "Edge" },
        ["Broadsheet.Style.ListViewItem.Row"] = new[] { "Bd", "Edge" },
    };

    /// <summary>Every library style applies to a live control, its template loads, and every setter lands on the control
    /// with the value it declares — a StaticResource one resolved rather than thrown, a DynamicResource one resolved to
    /// the key it names, a focus visual to one of the library's own focus styles.</summary>
    [Fact]
    public void EveryLibraryStyle_AppliesToALiveControl_AndEveryValueResolves()
    {
        var failures = WpfAppHost.Run(() =>
        {
            var errors = new List<string>();
            var keys = new List<string>();
            CollectKeys(WpfApp.Current.Resources, keys, new HashSet<ResourceDictionary>());
            var styleKeys = keys.Where(k => k.StartsWith("Broadsheet.Style.", StringComparison.Ordinal)).Distinct().ToList();
            if (styleKeys.Count == 0)
            {
                errors.Add("no Broadsheet.Style.* key is reachable from Application.Resources");
            }

            var focusVisuals = styleKeys
                .Where(k => k.StartsWith("Broadsheet.Style.FocusVisual", StringComparison.Ordinal))
                .Select(k => WpfApp.Current.FindResource(k))
                .ToList();

            foreach (var key in styleKeys)
            {
                try
                {
                    if (WpfApp.Current.FindResource(key) is not Style style)
                    {
                        errors.Add($"{key}: is not a Style");
                        continue;
                    }

                    var element = (FrameworkElement)Activator.CreateInstance(style.TargetType)!;
                    element.Style = style;
                    CheckSetters(key, style, element, focusVisuals, errors);

                    if (element is Control control)
                    {
                        control.ApplyTemplate();
                        if (RequiredParts.TryGetValue(key, out var parts))
                        {
                            errors.AddRange(parts
                                .Where(part => control.Template.FindName(part, control) is null)
                                .Select(part => $"{key}: template has no '{part}'"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    // The inner exception carries the unresolved key; the outer one is "provide value ... threw".
                    errors.Add($"{key}: {ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}");
                }
            }

            return errors;
        });

        Assert.Empty(failures);
    }

    /// <summary>The placeholder slot is wired: shown while the field is empty, hidden once there is text, and out of
    /// layout when the slot is unset. Template triggers evaluate on property change, so no layout pass is needed.</summary>
    [Fact]
    public void ThePlaceholderSlot_ShowsOnlyWhileTheFieldIsEmpty()
    {
        var states = WpfAppHost.Run(() =>
        {
            var box = new TextBox { Style = (Style)WpfApp.Current.FindResource("Broadsheet.Style.TextBox") };
            box.ApplyTemplate();
            var placeholder = (TextBlock)box.Template.FindName("PART_Placeholder", box);

            BroadsheetSlots.SetPlaceholder(box, "text or /regex/");
            var empty = placeholder.Visibility;
            box.Text = "abc";
            var typed = placeholder.Visibility;
            box.Text = string.Empty;
            BroadsheetSlots.SetPlaceholder(box, null);
            var unset = placeholder.Visibility;
            return (Empty: empty, Typed: typed, Unset: unset);
        });

        Assert.Equal(Visibility.Visible, states.Empty);
        Assert.Equal(Visibility.Collapsed, states.Typed);
        Assert.Equal(Visibility.Collapsed, states.Unset);
    }

    /// <summary>The Keep mark serves both of T-007's open shapes: a free ToggleButton (a click toggles it off again) and
    /// a RadioButton group.</summary>
    [Fact]
    public void TheKeepMark_AppliesToAToggleButtonAndARadioButton()
    {
        var failures = WpfAppHost.Run(() =>
        {
            var errors = new List<string>();
            var style = (Style)WpfApp.Current.FindResource("Broadsheet.Style.KeepRadio");
            foreach (var control in new ToggleButton[] { new ToggleButton(), new RadioButton() })
            {
                try
                {
                    control.Style = style;
                    control.ApplyTemplate();
                }
                catch (Exception ex)
                {
                    errors.Add($"{control.GetType().Name}: {ex.Message}");
                }
            }

            return errors;
        });

        Assert.Empty(failures);
    }

    /// <summary>The status bar right-aligns a right-docked group even when it is declared last: its items panel must not
    /// fill with its last child, which is what StatusBar's own default does. Every item's stock padding is zeroed.</summary>
    [Fact]
    public void TheStatusBar_KeepsARightDockedLastItemAtTheRightEdge()
    {
        var layout = WpfAppHost.Run(() =>
        {
            var bar = new StatusBar { Style = (Style)WpfApp.Current.FindResource("Broadsheet.Style.StatusBar"), Width = 600 };
            bar.Items.Add(new StatusBarItem { Content = "Ready" });
            var threads = new StatusBarItem { Content = "31 threads" };
            var ram = new StatusBarItem { Content = "RAM 186 MB" };
            DockPanel.SetDock(threads, Dock.Right);
            DockPanel.SetDock(ram, Dock.Right);
            bar.Items.Add(threads);
            bar.Items.Add(ram);

            bar.Measure(new Size(600, 26));
            bar.Arrange(new Rect(0, 0, 600, 26));
            return (
                ThreadsRight: threads.TranslatePoint(new Point(threads.ActualWidth, 0), bar).X,
                RamRight: ram.TranslatePoint(new Point(ram.ActualWidth, 0), bar).X,
                RamWidth: ram.ActualWidth,
                RamDesired: ram.DesiredSize.Width,
                Padding: threads.Padding);
        });

        Assert.Equal(588, layout.ThreadsRight, 1);
        Assert.True(layout.RamRight < layout.ThreadsRight, "the last right-docked item sits left of the first");
        Assert.Equal(layout.RamDesired, layout.RamWidth, 1);
        Assert.Equal(new Thickness(0), layout.Padding);
    }

    /// <summary>
    /// Each property the style sets resolves to the value the setter declares. Only the most-derived setter of a
    /// property counts, and values are compared at rest, which is how a fresh control starts - no style trigger is
    /// active yet.
    /// </summary>
    private static void CheckSetters(string key, Style style, FrameworkElement element, List<object> focusVisuals, List<string> errors)
    {
        var seen = new HashSet<DependencyProperty>();
        for (var level = style; level is not null; level = level.BasedOn)
        {
            foreach (var setter in level.Setters.OfType<Setter>().Where(s => s.TargetName is null))
            {
                if (!seen.Add(setter.Property))
                {
                    continue;
                }

                var actual = element.GetValue(setter.Property);
                if (setter.Property == FrameworkElement.FocusVisualStyleProperty)
                {
                    if (!focusVisuals.Contains(actual))
                    {
                        errors.Add($"{key}: FocusVisualStyle is not one of the library's focus visuals");
                    }

                    continue;
                }

                switch (setter.Value)
                {
                    case DynamicResourceExtension dynamic:
                        var expected = WpfApp.Current.TryFindResource(dynamic.ResourceKey);
                        if (expected is null)
                        {
                            errors.Add($"{key}: {setter.Property.Name} names '{dynamic.ResourceKey}', which does not exist");
                        }
                        else if (!Equals(actual, expected))
                        {
                            errors.Add($"{key}: {setter.Property.Name} is not the '{dynamic.ResourceKey}' it names");
                        }

                        break;
                    case MarkupExtension:
                        break;
                    case { } value when value.GetType().IsPublic && !Equals(actual, value):
                        errors.Add($"{key}: {setter.Property.Name} is '{actual}', the style sets '{value}'");
                        break;
                }
            }
        }
    }

    private static void CollectKeys(ResourceDictionary dictionary, List<string> keys, HashSet<ResourceDictionary> seen)
    {
        if (!seen.Add(dictionary))
        {
            return;
        }

        keys.AddRange(dictionary.Keys.OfType<string>());
        foreach (var merged in dictionary.MergedDictionaries)
        {
            CollectKeys(merged, keys, seen);
        }
    }
}
