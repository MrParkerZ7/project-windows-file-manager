using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace WindowsFileManager.Tests.Themes;

/// <summary>
/// Contract tests for the design-token dictionary layer introduced by T-001.
/// <para>
/// These assert on the XAML as XML, read from disk, rather than through the WPF resource
/// system. That is deliberate: it needs no <c>Application</c>, no STA thread and no pack://
/// URI registration, so it runs in the ordinary test pass — which is the point. The failure
/// this layer is exposed to is a mistyped resource key, which produces no error anywhere at
/// runtime: WPF silently falls back and the app renders a wrong-but-plausible default across
/// any number of sites. A test that only runs under a full WPF host would not have been
/// written before the substitution, and the substitution is where that failure is introduced.
/// </para>
/// </summary>
public class DesignTokenContractTests
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string[] DictionaryFiles =
    {
        "Broadsheet.Tokens.xaml",
        "Legacy.Palette.xaml",
        "Legacy.Metrics.xaml",
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

    private static string ViewsDirectory => Path.Combine(RepositoryRoot, "src", "WindowsFileManager", "Views");

    /// <summary>
    /// Every XAML the shell owns, minus the dictionaries themselves and build output. Deliberately the
    /// whole shell project rather than <c>Views/</c> alone: a colour literal or a resource reference in a
    /// folder nobody thought to scan is exactly the kind of thing that survives a migration unnoticed.
    /// </summary>
    private static IEnumerable<string> ShellXamlFiles()
    {
        var shell = Path.Combine(RepositoryRoot, "src", "WindowsFileManager");
        return Directory.GetFiles(shell, "*.xaml", SearchOption.AllDirectories)
            .Where(f =>
            {
                var rel = Path.GetRelativePath(shell, f).Replace('\\', '/');
                return !rel.StartsWith("Themes/", StringComparison.Ordinal)
                       && !rel.StartsWith("bin/", StringComparison.Ordinal)
                       && !rel.StartsWith("obj/", StringComparison.Ordinal);
            });
    }

    /// <summary>1. Every key referenced by a view exists in the dictionaries. This is the assertion the
    /// substitution is validated against, batch by batch, as it lands.</summary>
    [Fact]
    public void EveryReferencedResourceKey_IsDeclaredByTheDictionaries()
    {
        var declared = DeclaredKeys();
        var referencePattern = new Regex(@"\{(?:Dynamic|Static)Resource\s+([A-Za-z0-9_.]+)\s*\}", RegexOptions.Compiled);
        var dangling = new List<string>();

        foreach (var view in ShellXamlFiles())
        {
            var text = File.ReadAllText(view);
            foreach (Match match in referencePattern.Matches(text))
            {
                var key = match.Groups[1].Value;

                // Only the token layer's own namespaces are this test's business; a view may
                // legitimately reference a converter or style declared in its own Resources.
                if (!key.StartsWith("Broadsheet.", StringComparison.Ordinal)
                    && !key.StartsWith("Legacy.", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!declared.ContainsKey(key))
                {
                    dangling.Add($"{Path.GetFileName(view)} references '{key}', which no dictionary declares");
                }
            }
        }

        dangling.Should().BeEmpty();
    }

    /// <summary>2. Only the two sanctioned prefixes exist. 'Bs*' and per-task inventions are rejected —
    /// eight downstream tasks independently invented eight key shapes, and this is what stops that.</summary>
    [Fact]
    public void EveryDeclaredKey_UsesASanctionedPrefix()
    {
        var offenders = DeclaredKeys().Keys
            .Where(k => !k.StartsWith("Broadsheet.", StringComparison.Ordinal)
                        && !k.StartsWith("Legacy.", StringComparison.Ordinal))
            .ToList();

        offenders.Should().BeEmpty("'Broadsheet.' and 'Legacy.' are the only legal prefixes");
    }

    /// <summary>3. Reserved-but-empty categories stay empty in T-001. Defining one here would put a
    /// rendered-surface change in the task whose entire purpose is to change nothing.</summary>
    [Fact]
    public void ReservedCategories_AreStillEmpty()
    {
        var reserved = DeclaredKeys().Keys
            .Where(k => k.StartsWith("Broadsheet.Style.", StringComparison.Ordinal)
                        || k.StartsWith("Broadsheet.Template.", StringComparison.Ordinal)
                        || k.StartsWith("Broadsheet.Converter.", StringComparison.Ordinal))
            .ToList();

        reserved.Should().BeEmpty("those categories are published as a naming contract and defined by T-003");
    }

    /// <summary>4. The Broadsheet values are the vendored design system's values, not typed ones.
    /// Read from the frozen canvas so a drifted transcription fails the build.</summary>
    /// <param name="resourceKey">The dictionary key whose Color attribute is checked.</param>
    /// <param name="cssToken">The design-system custom property it must equal, without the leading dashes.</param>
    [Theory]
    [InlineData("Broadsheet.Brush.Bg", "color-bg")]
    [InlineData("Broadsheet.Brush.Surface", "color-surface")]
    [InlineData("Broadsheet.Brush.Text", "color-text")]
    [InlineData("Broadsheet.Brush.Accent", "color-accent")]
    [InlineData("Broadsheet.Brush.Accent2", "color-accent-2")]
    [InlineData("Broadsheet.Brush.Neutral600", "color-neutral-600")]
    [InlineData("Broadsheet.Brush.Neutral700", "color-neutral-700")]
    [InlineData("Broadsheet.Brush.Accent700", "color-accent-700")]
    [InlineData("Broadsheet.Brush.ProcessYellow", "color-process-yellow")]
    public void BroadsheetBrush_MatchesTheVendoredDesignSystem(string resourceKey, string cssToken)
    {
        var cssPath = Path.Combine(RepositoryRoot, "docs", "design", "canvas", "_ds");
        cssPath = Path.Combine(cssPath, "broadsheet-20e204aa-d8f4-4c1f-9771-d781d63fcc9d", "styles.css");
        var css = File.ReadAllText(cssPath);

        var expected = Regex.Match(css, $@"--{Regex.Escape(cssToken)}\s*:\s*(#[0-9a-fA-F]{{6}})\s*;").Groups[1].Value;
        expected.Should().NotBeEmpty($"'{cssToken}' must exist in the vendored styles.css");

        BrushColor(resourceKey).Should().Be("#FF" + expected.TrimStart('#').ToUpperInvariant());
    }

    /// <summary>5. Both divider forms exist. WPF has no color-mix, so the token ships twice: the alpha
    /// form composites correctly over Bg, Surface and Neutral100 alike, while the opaque form is there for
    /// surfaces that cannot take a translucent brush.
    /// <para>
    /// The opaque value is <c>#D1D0D0</c>, not the <c>#D3D2D2</c> the T-001 spec states. <c>#D3D2D2</c> is
    /// the <b>15%</b> composite of <c>#201e1d</c> over <c>#f3f2f2</c>; the design system declares
    /// <b>16%</b>, which gives <c>#D1D0D0</c>. Verified across the ramp: 14% → #D5D4D4, 15% → #D3D2D2,
    /// 16% → #D1D0D0, 17% → #CFCECE. The declared percentage wins over the spec's arithmetic.
    /// </para></summary>
    [Fact]
    public void BothDividerForms_Exist_AndTheOpaqueOneMatchesTheDeclaredSixteenPercent()
    {
        BrushColor("Broadsheet.Brush.Divider.Alpha").Should().Be("#29201E1D");
        BrushColor("Broadsheet.Brush.Divider").Should().Be("#FFD1D0D0");
    }

    /// <summary>9. Every Broadsheet brush indirects through a Color of the same name. That pairing is what
    /// lets a value serve both a brush and a Color-typed consumer; a brush with an inline literal would
    /// silently break the second use.</summary>
    [Fact]
    public void EveryBroadsheetBrush_HasAMatchingColorEntry()
    {
        var declared = DeclaredKeys().Keys.ToHashSet(StringComparer.Ordinal);
        var orphans = declared
            .Where(k => k.StartsWith("Broadsheet.Brush.", StringComparison.Ordinal))
            .Select(k => "Broadsheet.Color." + k["Broadsheet.Brush.".Length..])
            .Where(expected => !declared.Contains(expected))
            .ToList();

        orphans.Should().BeEmpty("every Broadsheet.Brush.X must have a Broadsheet.Color.X to indirect through");
    }

    /// <summary>6. The typeface decision is pinned. User decision 2026-09-04 (T-001 DECISION 1),
    /// option (b): Cambria, because this shell renders at 11-13px where a display serif degrades.</summary>
    [Fact]
    public void HeadingAndBodyFonts_AreCambria()
    {
        ResourceText("Broadsheet.FontFamily.Heading").Should().Be("Cambria");
        ResourceText("Broadsheet.FontFamily.Body").Should().Be("Cambria");
    }

    /// <summary>7. App.xaml merges exactly the dictionaries this layer declares, in the order that
    /// makes StaticResource resolvable — Broadsheet first, because resolution runs backwards through
    /// merged-dictionary parse order.</summary>
    [Fact]
    public void AppXaml_MergesTheDictionariesInOrder()
    {
        var app = XDocument.Load(Path.Combine(RepositoryRoot, "src", "WindowsFileManager", "App.xaml"));
        var sources = app.Descendants(XName.Get("ResourceDictionary", PresentationNs))
            .Select(d => d.Attribute("Source")?.Value)
            .Where(s => s is not null)
            .ToList();

        // Grows by exactly ONE entry per task: T-001 published the token layer's three, T-002
        // appended a single aggregator for the two families it lifted out of Window.Resources.
        // A task splicing several entries in here is the deviation this assertion exists to catch.
        sources.Should().Equal(
            "Themes/Broadsheet.Tokens.xaml",
            "Themes/Legacy.Palette.xaml",
            "Themes/Legacy.Metrics.xaml",
            "Themes/Legacy.Shell.xaml");
    }

    /// <summary>8. The legacy layer is transitional and its retirement is owned. Once no view
    /// references a Legacy.* key, both dictionaries must be deleted — that deletion belongs to
    /// <b>T-009</b>, the last task in the epic to touch a rendered surface. (T-001's spec body says
    /// T-011; the coherence pass reassigned it and that amendment governs.)</summary>
    [Fact]
    public void LegacyLayer_IsEitherStillReferencedOrGone()
    {
        var legacyStillDeclared = DeclaredKeys().Keys.Any(k => k.StartsWith("Legacy.", StringComparison.Ordinal));
        if (!legacyStillDeclared)
        {
            return;
        }

        var views = ShellXamlFiles().Select(File.ReadAllText).ToList();

        var referenced = views.Any(v => v.Contains("Resource Legacy.", StringComparison.Ordinal));

        // Before T-001's substitution lands, the dictionaries exist but nothing binds them yet —
        // the views still carry raw hex literals. That interim state is legitimate and is what
        // makes the two-commit split (dictionaries, then substitution) reviewable. Once the
        // substitution has run there is no raw hex left, so the only way to stay green is real
        // references; and once T-009 rebinds the last surface to Broadsheet.*, the references go
        // and this fails until the dead dictionaries are deleted. That is the intended liveness.
        var substitutionPending = views.Any(v => Regex.IsMatch(v, @"=""#[0-9A-Fa-f]{6,8}"""));

        (referenced || substitutionPending).Should().BeTrue(
            "no view references a Legacy.* key any more and no raw hex literals remain, so " +
            "Themes/Legacy.Palette.xaml and Themes/Legacy.Metrics.xaml are dead — T-009 must delete them");
    }

    /// <summary>Every resource key the dictionaries publish, mapped to the file that declares it.</summary>
    private static Dictionary<string, string> DeclaredKeys()
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in DictionaryFiles)
        {
            var doc = XDocument.Load(Path.Combine(ThemesDirectory, file));
            foreach (var element in doc.Descendants())
            {
                var key = element.Attribute(XName.Get("Key", XamlNs))?.Value;
                if (key is not null)
                {
                    keys.ContainsKey(key).Should().BeFalse($"resource key '{key}' is declared more than once");
                    keys[key] = file;
                }
            }
        }

        return keys;
    }

    /// <summary>
    /// The literal ARGB a brush resolves to. Brushes indirect through a <c>&lt;Color&gt;</c> entry
    /// (<c>Color="{StaticResource Broadsheet.Color.X}"</c>) so the same value can serve a brush and a
    /// <c>DropShadowEffect</c>, which takes a Color rather than a Brush. Resolving the indirection here
    /// means these tests also prove the indirection is wired correctly, not just that a hex is present.
    /// </summary>
    private static string BrushColor(string key)
    {
        var raw = FindByKey(key).Attribute("Color")?.Value
                  ?? throw new InvalidOperationException($"'{key}' has no Color attribute");

        var reference = Regex.Match(raw, @"^\{StaticResource\s+([A-Za-z0-9_.]+)\s*\}$");
        if (reference.Success)
        {
            raw = FindByKey(reference.Groups[1].Value).Value.Trim();
        }

        return raw.ToUpperInvariant();
    }

    private static string ResourceText(string key) => FindByKey(key).Value.Trim();

    private static XElement FindByKey(string key)
    {
        foreach (var file in DictionaryFiles)
        {
            var match = XDocument.Load(Path.Combine(ThemesDirectory, file))
                .Descendants()
                .FirstOrDefault(e => e.Attribute(XName.Get("Key", XamlNs))?.Value == key);
            if (match is not null)
            {
                return match;
            }
        }

        throw new InvalidOperationException($"resource key '{key}' is declared by no dictionary");
    }
}
