using System.Text.Json;
using FluentAssertions;
using WindowsFileManager.Core.Models;

namespace WindowsFileManager.Tests.Models;

public class FolderSearchPatternTests
{
    [Fact]
    public void Constructor_ShouldSetDefaults()
    {
        var p = new FolderSearchPattern();

        p.Pattern.Should().BeEmpty();
        p.IsEnabled.Should().BeTrue();
        p.MatchType.Should().Be(FolderMatchType.Match);
        p.Priority.Should().Be(0);
    }

    [Fact]
    public void IsEnabled_WhenChanged_ShouldRaisePropertyChanged()
    {
        var p = new FolderSearchPattern();
        var changes = new List<string>();
        p.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);

        p.IsEnabled = false;
        p.IsEnabled = false; // no change

        changes.Should().ContainSingle(x => x == nameof(FolderSearchPattern.IsEnabled));
    }

    [Fact]
    public void MatchType_WhenChanged_ShouldRaisePropertyChanged()
    {
        var p = new FolderSearchPattern();
        var changes = new List<string>();
        p.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);

        p.MatchType = FolderMatchType.Include;
        p.MatchType = FolderMatchType.Include; // no change

        changes.Should().ContainSingle(x => x == nameof(FolderSearchPattern.MatchType));
    }

    [Fact]
    public void Priority_WhenChanged_ShouldRaisePropertyChanged()
    {
        var p = new FolderSearchPattern();
        var changes = new List<string>();
        p.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);

        p.Priority = 3;
        p.Priority = 3; // no change

        changes.Should().ContainSingle(x => x == nameof(FolderSearchPattern.Priority));
    }

    [Fact]
    public void PropertySetters_NoSubscribers_ShouldNotThrow()
    {
        var p = new FolderSearchPattern();
        var act = () =>
        {
            p.IsEnabled = false;
            p.MatchType = FolderMatchType.Include;
            p.Priority = 5;
        };
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(FolderMatchType.Include, 0)]
    [InlineData(FolderMatchType.Match, 1)]
    [InlineData(FolderMatchType.Contains, 2)]
    [InlineData(FolderMatchType.Exclude, 3)]
    [InlineData(FolderMatchType.Mismatch, 4)]
    [InlineData(FolderMatchType.NotContain, 5)]
    public void FolderMatchType_Ordinals_Preserved(FolderMatchType type, int expectedOrdinal)
    {
        ((int)type).Should().Be(expectedOrdinal);
    }

    /// <summary>serves-spec: SPEC-007 rule 2 (also SPEC-009 invariant 5) — Priority is [JsonIgnore], so it is never written to settings.json; priorities are rebuilt from list order.</summary>
    [Fact]
    public void FolderSearchPattern_Serialized_OmitsPriority()
    {
        var pattern = new FolderSearchPattern
        {
            Pattern = "node_modules",
            MatchType = FolderMatchType.Contains,
            Priority = 3,
        };

        var json = JsonSerializer.Serialize(pattern);

        json.Should().NotContain("Priority");
        json.Should().Contain("\"Pattern\":\"node_modules\"");
    }

    /// <summary>serves-spec: SPEC-007 rule 2 — the read half: a Priority hand-edited into settings.json is ignored, so a tampered file cannot reorder patterns.</summary>
    [Fact]
    public void FolderSearchPattern_Deserialized_IgnoresPriorityInJson()
    {
        var json = """{"Pattern":"src","IsEnabled":true,"MatchType":1,"Priority":9}""";

        var pattern = JsonSerializer.Deserialize<FolderSearchPattern>(json)!;

        pattern.Pattern.Should().Be("src");
        pattern.MatchType.Should().Be(FolderMatchType.Match);
        pattern.Priority.Should().Be(0);
    }
}
