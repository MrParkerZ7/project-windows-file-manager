using System.Text.Json;
using FluentAssertions;
using WindowsFileManager.Core.Models;

namespace WindowsFileManager.Tests.Models;

public class FilterRuleTests
{
    [Fact]
    public void Constructor_ShouldSetDefaults()
    {
        var rule = new FilterRule();

        rule.Pattern.Should().BeEmpty();
        rule.IsEnabled.Should().BeTrue();
        rule.IsRegex.Should().BeFalse();
        rule.IgnoreCase.Should().BeTrue();
        rule.Action.Should().Be(FilterAction.Include);
        rule.Target.Should().Be(FilterTarget.Filename);
    }

    [Fact]
    public void DisplaySummary_IncludeWithFlags_ShouldFormat()
    {
        var rule = new FilterRule
        {
            Pattern = "*.jpg",
            Action = FilterAction.Include,
            Target = FilterTarget.Filename,
            IsRegex = true,
            IgnoreCase = true,
        };

        rule.DisplaySummary.Should().Be("Include | Filename | \"*.jpg\" [Regex, IgnoreCase]");
    }

    [Fact]
    public void DisplaySummary_ExcludeNoFlags_ShouldFormat()
    {
        var rule = new FilterRule
        {
            Pattern = "backup",
            Action = FilterAction.Exclude,
            Target = FilterTarget.Filepath,
            IsRegex = false,
            IgnoreCase = false,
        };

        rule.DisplaySummary.Should().Be("Exclude | Filepath | \"backup\"");
    }

    [Fact]
    public void JsonSerialize_ShouldNotIncludeDisplaySummary()
    {
        var rule = new FilterRule { Pattern = "test" };
        var json = JsonSerializer.Serialize(rule);

        json.Should().NotContain("DisplaySummary");
    }

    [Fact]
    public void JsonSerialize_ShouldNotIncludePriority()
    {
        var rule = new FilterRule { Pattern = "test", Priority = 5 };
        var json = JsonSerializer.Serialize(rule);

        json.Should().NotContain("Priority");
    }

    [Fact]
    public void JsonRoundTrip_ShouldPreserveAllProperties()
    {
        var rule = new FilterRule
        {
            Pattern = "photo",
            IsEnabled = false,
            IsRegex = true,
            IgnoreCase = false,
            Action = FilterAction.Exclude,
            Target = FilterTarget.Filepath,
        };

        var json = JsonSerializer.Serialize(rule);
        var deserialized = JsonSerializer.Deserialize<FilterRule>(json)!;

        deserialized.Pattern.Should().Be("photo");
        deserialized.IsEnabled.Should().BeFalse();
        deserialized.IsRegex.Should().BeTrue();
        deserialized.IgnoreCase.Should().BeFalse();
        deserialized.Action.Should().Be(FilterAction.Exclude);
        deserialized.Target.Should().Be(FilterTarget.Filepath);
    }

    [Fact]
    public void JsonDeserialize_MissingIsEnabled_ShouldDefaultToTrue()
    {
        var json = """{"Pattern":"test","IsRegex":false,"IgnoreCase":true,"Action":0,"Target":0}""";
        var rule = JsonSerializer.Deserialize<FilterRule>(json)!;

        rule.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void JsonDeserialize_WithDisplaySummary_ShouldNotFail()
    {
        var json = """{"Pattern":"old","IsRegex":false,"IgnoreCase":true,"Action":0,"Target":0,"DisplaySummary":"Select | Filename | \"old\" [IgnoreCase]"}""";
        var rule = JsonSerializer.Deserialize<FilterRule>(json)!;

        rule.Pattern.Should().Be("old");
        rule.Action.Should().Be(FilterAction.Include);
    }

    [Fact]
    public void IsEnabled_NoSubscribers_ShouldNotThrow()
    {
        var rule = new FilterRule();
        rule.IsEnabled = false;
        rule.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_Change_AfterUnsubscribe_ShouldNotRaise()
    {
        var rule = new FilterRule();
        var changes = 0;
        void Handler(object? s, System.ComponentModel.PropertyChangedEventArgs e) => changes++;
        rule.PropertyChanged += Handler;
        rule.IsEnabled = false;
        rule.PropertyChanged -= Handler;
        rule.IsEnabled = true;

        changes.Should().Be(1);
    }

    /// <summary>serves-spec: SPEC-003 model table — Priority is the one documented default the constructor test omits; a freshly built rule carries 0 until the engine renumbers it.</summary>
    [Fact]
    public void FilterRule_Constructor_PriorityDefaultsToZero()
    {
        new FilterRule().Priority.Should().Be(0);
    }

    /// <summary>serves-spec: SPEC-003 model table (DisplaySummary row) — the bracket suffix appears when either flag is set; with Regex alone it reads "[Regex]".</summary>
    [Fact]
    public void DisplaySummary_RegexOnly_AppendsRegexAlone()
    {
        var rule = new FilterRule
        {
            Pattern = @"^IMG_\d{4}\.(jpg|png)$",
            Action = FilterAction.Include,
            Target = FilterTarget.Filename,
            IsRegex = true,
            IgnoreCase = false,
        };

        rule.DisplaySummary.Should().Be("""Include | Filename | "^IMG_\d{4}\.(jpg|png)$" [Regex]""");
    }

    /// <summary>serves-spec: SPEC-003 model table (DisplaySummary row) — the other single-flag permutation, and the shape a freshly added rule shows, since IgnoreCase defaults true and IsRegex defaults false.</summary>
    [Fact]
    public void DisplaySummary_IgnoreCaseOnly_AppendsIgnoreCaseAlone()
    {
        var rule = new FilterRule
        {
            Pattern = "Thumbs.db",
            Action = FilterAction.Exclude,
            Target = FilterTarget.Filename,
        };

        rule.DisplaySummary.Should().Be("""Exclude | Filename | "Thumbs.db" [IgnoreCase]""");
    }

    /// <summary>serves-spec: SPEC-003 model table — IsEnabled raises PropertyChanged only on an actual change, so re-assigning the value it already holds raises nothing.</summary>
    [Fact]
    public void IsEnabled_SetToSameValue_DoesNotRaisePropertyChanged()
    {
        var rule = new FilterRule { Pattern = @"\Downloads\", Target = FilterTarget.Filepath };
        var changes = new List<string>();
        rule.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);

        rule.IsEnabled = true; // already true — must not raise
        rule.IsEnabled = false; // real change — raises once
        rule.IsEnabled = false; // already false — must not raise

        changes.Should().ContainSingle(p => p == nameof(FilterRule.IsEnabled));
    }

    /// <summary>serves-spec: SPEC-003 model table — IsEnabled is the only change-notifying property on the type, an invariant rule 9's re-render depends on.</summary>
    [Fact]
    public void FilterRule_PatternOrActionSetter_DoesNotRaisePropertyChanged()
    {
        var rule = new FilterRule();
        var changes = new List<string>();
        rule.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);

        rule.Pattern = "*.psd";
        rule.IsRegex = true;
        rule.IgnoreCase = false;
        rule.Action = FilterAction.Exclude;
        rule.Target = FilterTarget.Filepath;
        rule.Priority = 4;

        changes.Should().BeEmpty();
    }

    /// <summary>serves-spec: SPEC-003 rule 2 invariant — the enum ordinals are the on-disk format: Exclude and Filepath must serialize as 1, never as their names.</summary>
    [Fact]
    public void JsonSerialize_ExcludeAndFilepath_WriteOrdinalOneNotNames()
    {
        var rule = new FilterRule
        {
            Pattern = @"\node_modules\",
            Action = FilterAction.Exclude,
            Target = FilterTarget.Filepath,
        };

        var json = JsonSerializer.Serialize(rule);

        json.Should().Contain("\"Action\":1");
        json.Should().Contain("\"Target\":1");
        json.Should().NotContain("Exclude");
        json.Should().NotContain("Filepath");
    }

    /// <summary>serves-spec: SPEC-003 rule 2 invariant — the read half: a Target ordinal of 1 in a 1.0.0 settings.json maps back to Filepath.</summary>
    [Fact]
    public void JsonDeserialize_TargetOrdinalOne_MapsToFilepath()
    {
        var json = """{"Pattern":"\\AppData\\Local\\Temp\\","IsEnabled":true,"IsRegex":false,"IgnoreCase":true,"Action":1,"Target":1}""";

        var rule = JsonSerializer.Deserialize<FilterRule>(json)!;

        rule.Target.Should().Be(FilterTarget.Filepath);
        rule.Action.Should().Be(FilterAction.Exclude);
        rule.Pattern.Should().Be(@"\AppData\Local\Temp\");
    }

    /// <summary>serves-spec: SPEC-009 invariant 5 — FilterAction and FilterTarget ordinals are frozen for settings back-compat, the same guarantee FolderMatchType and ActionHistoryKind already carry.</summary>
    [Fact]
    public void FilterActionAndFilterTarget_Ordinals_Preserved()
    {
        ((int)FilterAction.Include).Should().Be(0);
        ((int)FilterAction.Exclude).Should().Be(1);
        ((int)FilterTarget.Filename).Should().Be(0);
        ((int)FilterTarget.Filepath).Should().Be(1);
    }
}
