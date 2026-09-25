using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using WindowsFileManager.Helpers;
using Xunit;

namespace WindowsFileManager.Tests.Themes;

/// <summary>
/// Structural gate for the Broadsheet component style library introduced by T-003
/// (<c>Themes/Broadsheet.Styles.xaml</c>).
/// <para>
/// Like <see cref="DesignTokenContractTests"/>, this reads the dictionaries as XML and constructs no WPF
/// object, so it needs no STA thread and runs in the ordinary test pass. That matters here more than
/// anywhere: the library ships keyed and unconsumed, so no screen exercises it when it lands, and the
/// failures it is exposed to — a resource key that does not resolve, a template missing the part name
/// the control looks it up by, an implicit style that silently restyles the whole process — all build
/// green and fail only at runtime, or never visibly at all.
/// </para>
/// <para>
/// What this cannot prove is how the styles look. That is checked against the 3a artboard by rendering
/// them, not here.
/// </para>
/// </summary>
public class BroadsheetComponentDictionaryTests
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string Aggregator = "Broadsheet.Styles.xaml";
    private const string Tokens = "Broadsheet.Tokens.xaml";

    private static readonly Regex ResourceReference =
        new(@"^\{(StaticResource|DynamicResource)\s+([A-Za-z0-9_.]+)\s*\}$", RegexOptions.Compiled);

    /// <summary>
    /// The published catalogue: every key T-004..T-011 bind by name, with the type it must target.
    /// Deleting or renaming any of these strands a consumer on a key that no longer exists.
    /// </summary>
    private static readonly Dictionary<string, string> RequiredStyles = new(StringComparer.Ordinal)
    {
        ["Broadsheet.Style.FocusVisual"] = "Control",
        ["Broadsheet.Style.FocusVisual.Inset"] = "Control",
        ["Broadsheet.Style.FocusVisual.TabUnderline"] = "Control",
        ["Broadsheet.Style.FocusVisual.Splitter.Vertical"] = "Control",
        ["Broadsheet.Style.FocusVisual.Splitter.Horizontal"] = "Control",
        ["Broadsheet.Style.ToolWindow.Header"] = "Border",
        ["Broadsheet.Style.ToolWindow.HeaderTitle"] = "TextBlock",
        ["Broadsheet.Style.ToolWindow.HeaderActions"] = "StackPanel",
        ["Broadsheet.Style.Rule.Hairline"] = "Border",
        ["Broadsheet.Style.Rule.Row"] = "Border",
        ["Broadsheet.Style.Rule.Vertical"] = "Border",
        ["Broadsheet.Style.Band.Title"] = "Border",
        ["Broadsheet.Style.Band.TabStrip"] = "Border",
        ["Broadsheet.Style.Band.Toolbar"] = "Border",
        ["Broadsheet.Style.Band.ActionBar"] = "Border",
        ["Broadsheet.Style.Band.SubToolbar"] = "Border",
        ["Broadsheet.Style.PaneSplitter.Vertical"] = "GridSplitter",
        ["Broadsheet.Style.PaneSplitter.Horizontal"] = "GridSplitter",
        ["Broadsheet.Style.StatusBar"] = "StatusBar",
        ["Broadsheet.Style.StatusBarItem"] = "StatusBarItem",
        ["Broadsheet.Style.Tab.Editor"] = "TabItem",
        ["Broadsheet.Style.TabControl.EditorStrip"] = "TabControl",
        ["Broadsheet.Style.Text.Kicker"] = "TextBlock",
        ["Broadsheet.Style.Text.Caption"] = "TextBlock",
        ["Broadsheet.Style.Text.MetaValue"] = "TextBlock",
        ["Broadsheet.Style.Text.Code"] = "TextBlock",
        ["Broadsheet.Style.Text.Glyph"] = "TextBlock",
        ["Broadsheet.Style.Text.GroupTitle"] = "TextBlock",
        ["Broadsheet.Style.Tag.Accent"] = "Border",
        ["Broadsheet.Style.Tag.Neutral"] = "Border",
        ["Broadsheet.Style.Toggle.Glyph"] = "ToggleButton",
        ["Broadsheet.Style.Button.Primary"] = "Button",
        ["Broadsheet.Style.Button.Secondary"] = "Button",
        ["Broadsheet.Style.Button.Ghost"] = "Button",
        ["Broadsheet.Style.Button.Icon"] = "Button",
        ["Broadsheet.Style.Button.Link"] = "Button",
        ["Broadsheet.Style.Button.LinkMuted"] = "Button",
        ["Broadsheet.Style.TextBox"] = "TextBox",
        ["Broadsheet.Style.TextBox.Numeric"] = "TextBox",
        ["Broadsheet.Style.ComboBox"] = "ComboBox",
        ["Broadsheet.Style.ComboBoxItem"] = "ComboBoxItem",
        ["Broadsheet.Style.CheckBox"] = "CheckBox",
        ["Broadsheet.Style.KeepRadio"] = "ToggleButton",
        ["Broadsheet.Style.Segment.Group"] = "Border",
        ["Broadsheet.Style.Segment.Option"] = "RadioButton",
        ["Broadsheet.Style.Segment.OptionFirst"] = "RadioButton",
        ["Broadsheet.Style.ListBoxItem.Row"] = "ListBoxItem",
        ["Broadsheet.Style.ListViewItem.Row"] = "ListViewItem",
        ["Broadsheet.Style.CopyRow"] = "Border",
    };

    /// <summary>
    /// The reference kind each declared resource type requires (T-001 contract 3). Brushes and colours are
    /// themeable, so they are read dynamically; everything structural is read statically so a typo throws
    /// on first use instead of silently leaving the property at its default.
    /// </summary>
    private static readonly Dictionary<string, string> RequiredKindByDeclaringElement = new(StringComparer.Ordinal)
    {
        ["SolidColorBrush"] = "DynamicResource",
        ["Color"] = "DynamicResource",
        ["CornerRadius"] = "StaticResource",
        ["Thickness"] = "StaticResource",
        ["Double"] = "StaticResource",
        ["FontFamily"] = "StaticResource",
        ["Style"] = "StaticResource",
        ["ControlTemplate"] = "StaticResource",
    };

    private static readonly string[] ButtonBaseTargets =
    {
        "ButtonBase", "Button", "ToggleButton", "RadioButton", "CheckBox", "RepeatButton",
    };

    private static string RepositoryRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WindowsFileManager.sln")))
            {
                dir = dir.Parent;
            }

            dir.Should().NotBeNull("the tests must be able to locate the repository root");
            return dir!.FullName;
        }
    }

    private static string ThemesDirectory => Path.Combine(RepositoryRoot, "src", "WindowsFileManager", "Themes");

    /// <summary>BCL-01. Every published key exists exactly once, targeting the type its consumers assume.</summary>
    [Fact]
    public void EveryPublishedStyle_ExistsExactlyOnce_WithItsTargetType()
    {
        var declared = LibraryFiles()
            .SelectMany(f => Declarations(f).Where(d => d.Element.Name.LocalName == "Style"))
            .ToList();

        var problems = new List<string>();
        foreach (var (key, targetType) in RequiredStyles)
        {
            var matches = declared.Where(d => d.Key == key).ToList();
            if (matches.Count != 1)
            {
                problems.Add($"'{key}' is declared {matches.Count} times; the catalogue requires exactly one");
                continue;
            }

            var actual = matches[0].Element.Attribute("TargetType")?.Value;
            if (actual != targetType)
            {
                problems.Add($"'{key}' targets '{actual}', but its consumers bind it to a {targetType}");
            }
        }

        problems.Should().BeEmpty();
    }

    /// <summary>BCL-02. Every resource reference in the library resolves to a key the tokens or the library declare.
    /// A missing key is not a build error in WPF — this is the only place it fails before runtime.</summary>
    [Fact]
    public void EveryReferenceInTheLibrary_Resolves()
    {
        var declared = DeclaredTypes();
        var dangling = References()
            .Where(r => !declared.ContainsKey(r.Key))
            .Select(r => $"{r.File} references '{r.Key}', which neither the tokens nor the library declare")
            .ToList();

        dangling.Should().BeEmpty();
    }

    /// <summary>BCL-03. The reference kind follows the declared type, both ways round: a brush read statically
    /// cannot be re-themed, and a radius read dynamically falls back to 0 on a typo instead of throwing.</summary>
    [Fact]
    public void EveryReference_UsesTheKindItsDeclaredTypeRequires()
    {
        var declared = DeclaredTypes();
        var wrong = new List<string>();
        foreach (var reference in References().Where(r => declared.ContainsKey(r.Key)))
        {
            var type = declared[reference.Key];
            if (!RequiredKindByDeclaringElement.TryGetValue(type, out var required))
            {
                wrong.Add($"'{reference.Key}' is a {type}, which has no rule — add it to the table deliberately");
                continue;
            }

            if (reference.Kind != required)
            {
                wrong.Add($"{reference.File}: '{reference.Key}' is a {type} and must be read with {required}, not {reference.Kind}");
            }
        }

        wrong.Should().BeEmpty();
    }

    /// <summary>BCL-04. No colour literal anywhere in the library - no hex of any length, no named system brush, and no
    /// named colour such as <c>Red</c> in an attribute or setter: every colour is a token. <c>Transparent</c> is the one
    /// allowed name.</summary>
    [Fact]
    public void TheLibrary_ContainsNoColourLiteral()
    {
        var colourNames = typeof(System.Windows.Media.Colors)
            .GetProperties()
            .Select(p => p.Name)
            .Where(n => n != "Transparent")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var offenders = LibraryFiles()
            .SelectMany(f =>
            {
                var text = File.ReadAllText(Path.Combine(ThemesDirectory, f));
                var literals = Regex.Matches(text, @"#[0-9A-Fa-f]{3,8}\b|\bBrushes\.[A-Za-z]+|\bSystemColors\.[A-Za-z]+")
                    .Select(m => $"{f}: {m.Value}");
                var named = XDocument.Load(Path.Combine(ThemesDirectory, f))
                    .Descendants()
                    .SelectMany(e => e.Attributes())
                    .Where(a => colourNames.Contains(a.Value.Trim()))
                    .Select(a => $"{f}: {a.Name.LocalName}=\"{a.Value}\"");
                return literals.Concat(named);
            })
            .ToList();

        offenders.Should().BeEmpty();
    }

    /// <summary>BCL-05. Every style carries a key. An implicit style in an application-level dictionary restyles
    /// every instance of its type in the process — the 21 help popups and ProfileNameDialog included — and
    /// Themes/Generic.xaml would be auto-discovered and applied the same way.</summary>
    [Fact]
    public void NoStyle_IsImplicit_AndNoGenericXamlExists()
    {
        var implicitStyles = LibraryFiles()
            .SelectMany(f => XDocument.Load(Path.Combine(ThemesDirectory, f))
                .Descendants(XName.Get("Style", PresentationNs))
                .Where(s => s.Attribute(XName.Get("Key", XamlNs)) is null)
                .Select(s => $"{f}: an unkeyed style targeting '{s.Attribute("TargetType")?.Value}'"))
            .ToList();

        implicitStyles.Should().BeEmpty();
        File.Exists(Path.Combine(ThemesDirectory, "Generic.xaml")).Should()
            .BeFalse("Themes/Generic.xaml is auto-discovered by WPF and would apply its styles implicitly");
    }

    /// <summary>BCL-05b. The library defines only the categories T-001 reserved for it. Style keys are
    /// Broadsheet.Style.*, shared templates Broadsheet.Template.*, converters Broadsheet.Converter.*.</summary>
    [Fact]
    public void EveryLibraryKey_UsesACategoryReservedForIt()
    {
        var offenders = LibraryFiles()
            .SelectMany(f => Declarations(f).Select(d => (File: f, d.Key)))
            .Where(d => !Regex.IsMatch(d.Key, @"^Broadsheet\.(Style|Template|Converter)(\.[A-Za-z0-9_]+)+$"))
            .Select(d => $"{d.File}: '{d.Key}'")
            .ToList();

        offenders.Should().BeEmpty("T-001 reserves Broadsheet.Style|Template|Converter.* for this library, and nothing else");
    }

    /// <summary>BCL-06. No key is declared twice anywhere App.xaml merges. WPF resolves a duplicate by letting the
    /// later dictionary win, silently.</summary>
    [Fact]
    public void NoKey_IsDeclaredTwice_AcrossEverythingAppXamlMerges()
    {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        var duplicates = new List<string>();
        foreach (var file in MergedFromAppXaml())
        {
            foreach (var declaration in Declarations(file))
            {
                if (owners.TryGetValue(declaration.Key, out var first))
                {
                    duplicates.Add($"'{declaration.Key}' is declared in both {first} and {file}");
                }
                else
                {
                    owners[declaration.Key] = file;
                }
            }
        }

        duplicates.Should().BeEmpty();
    }

    /// <summary>BCL-07. App.xaml merges the tokens before the library, as exactly one entry, and the library is ONE
    /// dictionary. Splitting it into family files merged by an aggregator was built and measured: WPF does not resolve a
    /// StaticResource from one merged dictionary into a sibling under the same aggregator, so every FocusVisualStyle came
    /// back unset and the drop-down template threw at runtime — while this build and every XML check stayed green.</summary>
    [Fact]
    public void TheLibrary_IsOneDictionary_MergedOnceAfterTheTokens()
    {
        var appSources = Sources(Path.Combine(RepositoryRoot, "src", "WindowsFileManager", "App.xaml"));
        appSources.Should().Contain("Themes/" + Tokens).And.Contain("Themes/" + Aggregator);
        appSources.IndexOf("Themes/" + Tokens).Should().BeLessThan(
            appSources.IndexOf("Themes/" + Aggregator),
            "StaticResource resolves only backwards through merge order");
        appSources.Where(s => s.StartsWith("Themes/Broadsheet.Styles", StringComparison.Ordinal)).Should()
            .ContainSingle("the library adds exactly one App.xaml entry");

        Sources(Path.Combine(ThemesDirectory, Aggregator)).Should()
            .BeEmpty("a family merged beside another cannot StaticResource-reference it — keep the library one dictionary");
        Directory.GetFiles(ThemesDirectory, "Broadsheet.Styles.*.xaml").Should()
            .BeEmpty("the library's sections live in Broadsheet.Styles.xaml itself");
    }

    /// <summary>BCL-08. The template parts the controls look up by literal name are present. Without them the build is
    /// green and the control fails at runtime: a text box that shows nothing and takes no keystrokes, a drop-down
    /// that never opens, a tab strip with no tabs.</summary>
    [Fact]
    public void RequiredTemplateParts_ArePresent()
    {
        var missing = new List<string>();
        void Require(string styleKey, Func<XElement, bool> predicate, string what)
        {
            if (!StyleElement(styleKey).Descendants().Any(predicate))
            {
                missing.Add($"'{styleKey}' has no {what}");
            }
        }

        Require("Broadsheet.Style.TextBox", e => NameOf(e) == "PART_ContentHost", "PART_ContentHost");
        Require(
            "Broadsheet.Style.ComboBox",
            e => NameOf(e) == "PART_Popup" && e.Name.LocalName == "Popup" && OpensWithTheDropDown(e.Attribute("IsOpen")?.Value),
            "PART_Popup whose IsOpen binds the ComboBox's IsDropDownOpen");
        Require(
            "Broadsheet.Style.ComboBox",
            e => NameOf(e) == "PART_ToggleButton" && OpensWithTheDropDown(e.Attribute("IsChecked")?.Value),
            "PART_ToggleButton whose IsChecked binds the ComboBox's IsDropDownOpen");
        Require("Broadsheet.Style.ComboBox", e => NameOf(e) == "ContentSite", "ContentSite");
        Require(
            "Broadsheet.Style.TabControl.EditorStrip",
            e => e.Name.LocalName == "TabPanel" && e.Attribute("IsItemsHost")?.Value == "True",
            "TabPanel with IsItemsHost=\"True\"");
        Require("Broadsheet.Style.TabControl.EditorStrip", e => NameOf(e) == "PART_SelectedContentHost", "PART_SelectedContentHost");

        missing.Should().BeEmpty();
    }

    /// <summary>BCL-09. Every ContentPresenter inside a button-family template recognises access keys, so an underscore
    /// in a caption is an access key rather than a literal character. T-011 depends on this.</summary>
    [Fact]
    public void EveryButtonTemplatePresenter_RecognizesAccessKeys()
    {
        var offenders = LibraryFiles()
            .SelectMany(f => XDocument.Load(Path.Combine(ThemesDirectory, f))
                .Descendants(XName.Get("ControlTemplate", PresentationNs))
                .Where(t => ButtonBaseTargets.Contains(t.Attribute("TargetType")?.Value))
                .SelectMany(t => t.Descendants(XName.Get("ContentPresenter", PresentationNs))
                    .Where(p => p.Attribute("RecognizesAccessKey")?.Value != "True")
                    .Select(_ => $"{f}: a ContentPresenter in a {t.Attribute("TargetType")?.Value} template")))
            .ToList();

        offenders.Should().BeEmpty();
    }

    /// <summary>BCL-10. The glyph face is a token, puts Segoe UI Symbol first, and is not Segoe Fluent Icons — which is
    /// Windows 11 only, while the app supports Windows 10 1809.</summary>
    [Fact]
    public void TheGlyphFace_IsATokenThatExistsOnWindows10()
    {
        var glyph = XDocument.Load(Path.Combine(ThemesDirectory, Tokens))
            .Descendants()
            .SingleOrDefault(e => e.Attribute(XName.Get("Key", XamlNs))?.Value == "Broadsheet.FontFamily.Glyph");

        glyph.Should().NotBeNull("the library's glyph-bearing styles read Broadsheet.FontFamily.Glyph");
        glyph!.Value.Trim().Should().StartWith("Segoe UI Symbol").And.NotContain("Fluent");
    }

    /// <summary>BCL-11. Every one-pixel element snaps to device pixels. Unsnapped, a 1px rule renders as a 1.25px smear
    /// or vanishes at 125% and 150% scaling.</summary>
    [Fact]
    public void EveryOnePixelStyle_SnapsToDevicePixels()
    {
        var offenders = LibraryFiles()
            .SelectMany(f => XDocument.Load(Path.Combine(ThemesDirectory, f))
                .Descendants(XName.Get("Style", PresentationNs))
                .Where(s => Setters(s).Any(p => (p.Property is "Height" or "Width") && p.Value == "1"))
                .Where(s => !Setters(s).Any(p => p.Property == "SnapsToDevicePixels" && p.Value == "True")
                            || !Setters(s).Any(p => p.Property == "UseLayoutRounding" && p.Value == "True"))
                .Select(s => $"{f}: '{s.Attribute(XName.Get("Key", XamlNs))?.Value}'"))
            .ToList();

        offenders.Should().BeEmpty();
    }

    /// <summary>BCL-12. The link styles set no font size or face, so they inherit the band they sit in — one key renders
    /// at 12px in a tool-window header and 12.5px in the results toolbar, as the artboard does.</summary>
    [Fact]
    public void LinkStyles_InheritTheirBandsType()
    {
        foreach (var key in new[] { "Broadsheet.Style.Button.Link", "Broadsheet.Style.Button.LinkMuted" })
        {
            Setters(StyleElement(key)).Select(s => s.Property).Should()
                .NotContain(new[] { "FontSize", "FontFamily" }, $"'{key}' must inherit its band's type");
        }
    }

    /// <summary>BCL-14. The tab styles set no font property on the TabItem or TabControl themselves. A TabItem is the
    /// logical parent of its page, so a FontSize/FontWeight/FontFamily set there is inherited by the whole page - the
    /// label's type lives on the tab template instead.</summary>
    [Fact]
    public void TabStyles_KeepTheLabelTypeOffTheTabItem()
    {
        var fontProperties = new[] { "FontSize", "FontFamily", "FontWeight", "FontStyle", "FontStretch" };
        foreach (var key in new[] { "Broadsheet.Style.Tab.Editor", "Broadsheet.Style.TabControl.EditorStrip" })
        {
            Setters(StyleElement(key))
                .Select(s => s.Property.Replace("TextElement.", string.Empty, StringComparison.Ordinal))
                .Should().NotContain(fontProperties, $"'{key}' would hand its type to every page it hosts");
        }
    }

    /// <summary>BCL-15. No text is painted in Neutral500, Neutral600 or Accent. T-011a's Fork 1 was answered option A:
    /// those fail AA contrast at 11-13px, so secondary text is Neutral700 and link text Accent700. They remain for
    /// non-text marks; the disclosure glyph is the one styled glyph that uses Neutral600.</summary>
    [Fact]
    public void NoTextIsPaintedInAFailingInk()
    {
        var failing = new[] { "Neutral500", "Neutral600", "Accent" }
            .Select(k => $"{{DynamicResource Broadsheet.Brush.{k}}}")
            .ToHashSet(StringComparer.Ordinal);
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "Broadsheet.Style.Toggle.Glyph" };

        var offenders = new List<string>();
        foreach (var style in LibraryFiles().SelectMany(f => XDocument.Load(Path.Combine(ThemesDirectory, f))
                     .Descendants(XName.Get("Style", PresentationNs))))
        {
            var key = style.Attribute(XName.Get("Key", XamlNs))?.Value ?? "(unkeyed)";
            if (allowed.Contains(key))
            {
                continue;
            }

            var setterHits = style.Descendants(XName.Get("Setter", PresentationNs))
                .Where(s => (s.Attribute("Property")?.Value ?? string.Empty).EndsWith("Foreground", StringComparison.Ordinal)
                            && failing.Contains(s.Attribute("Value")?.Value ?? string.Empty));
            var attributeHits = style.Descendants()
                .SelectMany(e => e.Attributes())
                .Where(a => a.Name.LocalName.EndsWith("Foreground", StringComparison.Ordinal) && failing.Contains(a.Value));
            offenders.AddRange(setterHits.Select(_ => $"'{key}': a Foreground setter uses a failing ink"));
            offenders.AddRange(attributeHits.Select(a => $"'{key}': {a.Name.LocalName} uses a failing ink"));
        }

        offenders.Should().BeEmpty();
    }

    /// <summary>BCL-13. The typed slots the templates bind exist on the helper under exactly those names (decision D1-B).
    /// A rename on one side only would leave a template bound to nothing.</summary>
    [Fact]
    public void TheSlotsTheTemplatesBind_ExistOnTheHelper()
    {
        var bound = LibraryFiles()
            .SelectMany(f => Regex.Matches(File.ReadAllText(Path.Combine(ThemesDirectory, f)), @"\(helpers:BroadsheetSlots\.([A-Za-z]+)\)")
                .Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        bound.Should().BeEquivalentTo(new[] { "TrailingContent", "Placeholder" });
        foreach (var slot in bound)
        {
            typeof(BroadsheetSlots).GetField(slot + "Property").Should()
                .NotBeNull($"the templates bind BroadsheetSlots.{slot}");
        }
    }

    private static bool OpensWithTheDropDown(string? binding) =>
        binding is not null
        && binding.Contains("IsDropDownOpen", StringComparison.Ordinal)
        && binding.Contains("TemplatedParent", StringComparison.Ordinal);

    /// <summary>The library's files: the dictionary itself, plus anything it merges (nothing, while BCL-07 holds).</summary>
    private static List<string> LibraryFiles() =>
        Sources(Path.Combine(ThemesDirectory, Aggregator)).Prepend(Aggregator).ToList();

    /// <summary>Every Themes/ dictionary App.xaml merges, following aggregators, as file names relative to Themes/.</summary>
    private static List<string> MergedFromAppXaml()
    {
        var result = new List<string>();
        void Walk(string file)
        {
            result.Add(file);
            foreach (var child in Sources(Path.Combine(ThemesDirectory, file)))
            {
                Walk(child);
            }
        }

        foreach (var source in Sources(Path.Combine(RepositoryRoot, "src", "WindowsFileManager", "App.xaml")))
        {
            Walk(source["Themes/".Length..]);
        }

        return result;
    }

    /// <summary>The Source of every merged ResourceDictionary in a XAML file, in document order.</summary>
    private static List<string> Sources(string path) =>
        XDocument.Load(path)
            .Descendants(XName.Get("ResourceDictionary", PresentationNs))
            .Select(d => d.Attribute("Source")?.Value)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();

    /// <summary>Every keyed element in a Themes/ file.</summary>
    private static IEnumerable<(string Key, XElement Element)> Declarations(string file) =>
        XDocument.Load(Path.Combine(ThemesDirectory, file))
            .Descendants()
            .Select(e => (Key: e.Attribute(XName.Get("Key", XamlNs))?.Value, Element: e))
            .Where(d => d.Key is not null)
            .Select(d => (d.Key!, d.Element));

    /// <summary>Every key the tokens and the library declare, mapped to the element type that declares it.</summary>
    private static Dictionary<string, string> DeclaredTypes() =>
        LibraryFiles().Prepend(Tokens)
            .SelectMany(Declarations)
            .GroupBy(d => d.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Element.Name.LocalName, StringComparer.Ordinal);

    /// <summary>Every {StaticResource}/{DynamicResource} reference written in a library attribute.</summary>
    private static IEnumerable<(string File, string Kind, string Key)> References() =>
        LibraryFiles().SelectMany(f => XDocument.Load(Path.Combine(ThemesDirectory, f))
            .Descendants()
            .SelectMany(e => e.Attributes())
            .Select(a => ResourceReference.Match(a.Value))
            .Where(m => m.Success)
            .Select(m => (File: f, Kind: m.Groups[1].Value, Key: m.Groups[2].Value)));

    private static XElement StyleElement(string key) =>
        LibraryFiles()
            .SelectMany(f => XDocument.Load(Path.Combine(ThemesDirectory, f)).Descendants(XName.Get("Style", PresentationNs)))
            .Single(s => s.Attribute(XName.Get("Key", XamlNs))?.Value == key);

    /// <summary>The style's own Setters — not the setters of templates or triggers nested inside it.</summary>
    private static IEnumerable<(string Property, string? Value)> Setters(XElement style) =>
        style.Elements(XName.Get("Setter", PresentationNs))
            .Select(s => (Property: s.Attribute("Property")?.Value ?? string.Empty, Value: s.Attribute("Value")?.Value));

    private static string? NameOf(XElement element) => element.Attribute(XName.Get("Name", XamlNs))?.Value;
}
