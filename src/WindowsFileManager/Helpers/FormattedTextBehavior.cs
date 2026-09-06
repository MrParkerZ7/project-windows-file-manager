using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WindowsFileManager.Helpers;

/// <summary>
/// Attached behavior that parses simple markup tags into formatted TextBlock inlines.
/// Supported tags: &lt;b&gt;bold&lt;/b&gt;, &lt;h&gt;highlight&lt;/h&gt;, &lt;w&gt;warning&lt;/w&gt;,
/// &lt;link=URL&gt;text&lt;/link&gt;.
/// Newlines (\n) are converted to LineBreak elements.
/// </summary>
[ExcludeFromCodeCoverage]
public static class FormattedTextBehavior
{
    // The four help-markup brushes now live in Themes/Legacy.Palette.xaml and are resolved
    // per target element rather than held as statics. TryFindResource walks the LOGICAL tree
    // from the element upward and terminates at Application.Resources — which is precisely why
    // the dictionaries are anchored there: these TextBlocks live inside HelpButtonStyle's
    // Popup, and a Popup hosts its child in a separate visual tree on its own HwndSource.
    //
    // Each keeps its original literal as a fallback, so a missing dictionary degrades to the
    // shipped colour rather than to WPF's default (which for Foreground is black, and would
    // silently erase the distinction between a heading, a warning and a link).
    private const string HighlightKey = "Legacy.Brush.Action.PrimaryDark";   // #0D47A1
    private const string WarningFgKey = "Legacy.Brush.Danger.Fg";            // #C62828
    private const string WarningBgKey = "Legacy.Brush.Help.WarnBg";          // #FFEBEE
    private const string LinkKey = "Legacy.Brush.Action.Primary";            // #1565C0

    /// <summary>
    /// Identifies the FormattedText attached property.
    /// </summary>
    public static readonly DependencyProperty FormattedTextProperty =
        DependencyProperty.RegisterAttached(
            "FormattedText",
            typeof(string),
            typeof(FormattedTextBehavior),
            new PropertyMetadata(null, OnFormattedTextChanged));

    /// <summary>
    /// Gets the formatted text for the specified TextBlock.
    /// </summary>
    /// <param name="obj">The dependency object to read from.</param>
    /// <returns>The formatted text string, or null.</returns>
    public static string? GetFormattedText(DependencyObject obj) =>
        (string?)obj.GetValue(FormattedTextProperty);

    /// <summary>
    /// Sets the formatted text for the specified TextBlock.
    /// </summary>
    /// <param name="obj">The dependency object to write to.</param>
    /// <param name="value">The formatted text to parse and display.</param>
    public static void SetFormattedText(DependencyObject obj, string? value) =>
        obj.SetValue(FormattedTextProperty, value);

    private static void OnFormattedTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock)
        {
            return;
        }

        textBlock.Inlines.Clear();

        if (e.NewValue is not string text || string.IsNullOrEmpty(text))
        {
            return;
        }

        ParseAndApply(textBlock, text);
    }

    private static void ParseAndApply(TextBlock textBlock, string text)
    {
        var position = 0;

        while (position < text.Length)
        {
            var tagStart = text.IndexOf('<', position);

            if (tagStart < 0)
            {
                AddPlainText(textBlock, text[position..]);
                break;
            }

            if (tagStart > position)
            {
                AddPlainText(textBlock, text[position..tagStart]);
            }

            var tagEnd = text.IndexOf('>', tagStart);
            if (tagEnd < 0)
            {
                AddPlainText(textBlock, text[tagStart..]);
                break;
            }

            var tag = text[(tagStart + 1)..tagEnd];

            if (tag is "b" or "h" or "w")
            {
                var closeTag = $"</{tag}>";
                var closePos = text.IndexOf(closeTag, tagEnd + 1, StringComparison.Ordinal);

                if (closePos < 0)
                {
                    AddPlainText(textBlock, text[tagStart..]);
                    break;
                }

                var content = text[(tagEnd + 1)..closePos];
                AddStyledRun(textBlock, content, tag);
                position = closePos + closeTag.Length;
            }
            else if (tag.StartsWith("link=", StringComparison.Ordinal))
            {
                var url = tag["link=".Length..];
                var closeTag = "</link>";
                var closePos = text.IndexOf(closeTag, tagEnd + 1, StringComparison.Ordinal);

                if (closePos < 0)
                {
                    AddPlainText(textBlock, text[tagStart..]);
                    break;
                }

                var displayText = text[(tagEnd + 1)..closePos];
                AddHyperlink(textBlock, displayText, url);
                position = closePos + closeTag.Length;
            }
            else
            {
                AddPlainText(textBlock, text[tagStart..(tagEnd + 1)]);
                position = tagEnd + 1;
            }

            continue;
        }
    }

    /// <summary>Resolve a palette brush from the target element, falling back to the shipped literal.</summary>
    /// <param name="target">The element to resolve from - resource lookup walks up from here.</param>
    /// <param name="key">The <c>Legacy.Brush.*</c> key to look up.</param>
    /// <param name="r">Red channel of the fallback literal.</param>
    /// <param name="g">Green channel of the fallback literal.</param>
    /// <param name="b">Blue channel of the fallback literal.</param>
    /// <returns>The dictionary brush, or a brush built from the literal when the key is absent.</returns>
    private static Brush Resolve(FrameworkElement target, string key, byte r, byte g, byte b)
        => target.TryFindResource(key) as Brush ?? new SolidColorBrush(Color.FromRgb(r, g, b));

    private static void AddPlainText(TextBlock textBlock, string text)
    {
        var parts = text.Split('\n');
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                textBlock.Inlines.Add(new Run(parts[i]));
            }

            if (i < parts.Length - 1)
            {
                textBlock.Inlines.Add(new LineBreak());
            }
        }
    }

    private static void AddHyperlink(TextBlock textBlock, string displayText, string url)
    {
        var hyperlink = new Hyperlink(new Run(displayText))
        {
            Foreground = Resolve(textBlock, LinkKey, 0x15, 0x65, 0xC0),
            TextDecorations = TextDecorations.Underline,
            Cursor = System.Windows.Input.Cursors.Hand,
        };

        hyperlink.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
            }
            catch
            {
                // Silently ignore if URL can't be opened
            }
        };

        textBlock.Inlines.Add(hyperlink);
    }

    private static void AddStyledRun(TextBlock textBlock, string content, string tag)
    {
        var parts = content.Split('\n');
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                var run = new Run(parts[i]);
                switch (tag)
                {
                    case "b":
                        run.FontWeight = FontWeights.Bold;
                        break;
                    case "h":
                        run.FontWeight = FontWeights.SemiBold;
                        run.Foreground = Resolve(textBlock, HighlightKey, 0x0D, 0x47, 0xA1);
                        break;
                    case "w":
                        run.FontWeight = FontWeights.SemiBold;
                        run.Foreground = Resolve(textBlock, WarningFgKey, 0xC6, 0x28, 0x28);
                        run.Background = Resolve(textBlock, WarningBgKey, 0xFF, 0xEB, 0xEE);
                        break;
                }

                textBlock.Inlines.Add(run);
            }

            if (i < parts.Length - 1)
            {
                textBlock.Inlines.Add(new LineBreak());
            }
        }
    }
}
