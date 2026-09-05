using System.Text.Json;
using FluentAssertions;
using WindowsFileManager.Core.Models;

namespace WindowsFileManager.Tests.Models;

public class AppSettingsTests
{
    [Fact]
    public void Constructor_ShouldSetDefaults()
    {
        var settings = new AppSettings();

        settings.Profiles.Should().BeEmpty();
        settings.ActiveProfileName.Should().Be("Default");
        settings.ActionHistory.Should().BeEmpty();
        settings.WindowLeft.Should().BeNull();
        settings.WindowTop.Should().BeNull();
        settings.WindowWidth.Should().BeNull();
        settings.WindowHeight.Should().BeNull();
        settings.IsMaximized.Should().BeFalse();
    }

    [Fact]
    public void Properties_ShouldSetAndGet()
    {
        var profile = new ProfileSettings { Name = "Work" };
        var settings = new AppSettings
        {
            Profiles = new List<ProfileSettings> { profile },
            ActiveProfileName = "Work",
            ActionHistory = new List<ActionHistoryEntry>
            {
                new() { Kind = ActionHistoryKind.RecycleFiles, Summary = "Recycled 3 files" },
            },
            WindowLeft = 120,
            WindowTop = 80,
            WindowWidth = 1200,
            WindowHeight = 800,
            IsMaximized = true,
        };

        settings.Profiles.Should().ContainSingle();
        settings.Profiles[0].Name.Should().Be("Work");
        settings.ActiveProfileName.Should().Be("Work");
        settings.ActionHistory.Should().ContainSingle();
        settings.ActionHistory[0].Summary.Should().Be("Recycled 3 files");
        settings.WindowLeft.Should().Be(120);
        settings.WindowTop.Should().Be(80);
        settings.WindowWidth.Should().Be(1200);
        settings.WindowHeight.Should().Be(800);
        settings.IsMaximized.Should().BeTrue();
    }

    /// <summary>serves-spec: SPEC-004 rule 26 — ActionHistoryKind ordinals are frozen because System.Text.Json writes them into settings.json as plain numbers; the entry survives the round trip with its kind and paths intact.</summary>
    [Fact]
    public void ActionHistory_JsonRoundTrip_ShouldWriteKindAsOrdinalNumber()
    {
        var settings = new AppSettings
        {
            Profiles = new List<ProfileSettings> { new() { Name = "Photos" } },
            ActiveProfileName = "Photos",
            ActionHistory = new List<ActionHistoryEntry>
            {
                new()
                {
                    Kind = ActionHistoryKind.RecycleDirectories,
                    RecycledPaths = new List<string> { @"D:\Media\Photos\2019\.thumbnails" },
                    Summary = "Recycled 1 folder",
                    Timestamp = new DateTime(2026, 3, 14, 9, 30, 0),
                },
                new()
                {
                    Kind = ActionHistoryKind.MoveFiles,
                    Moves = new List<ActionHistoryMove>
                    {
                        new() { Source = @"D:\Media\Photos\IMG_4821.jpg", Destination = @"E:\Quarantine\IMG_4821.jpg" },
                    },
                    Summary = "Moved 1 file",
                    Timestamp = new DateTime(2026, 3, 14, 9, 28, 0),
                },
            },
        };

        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        json.Should().Contain("\"Kind\":2").And.Contain("\"Kind\":0");
        json.Should().NotContain("\"RecycleDirectories\"");
        restored!.ActionHistory.Should().HaveCount(2);
        restored.ActionHistory[0].Kind.Should().Be(ActionHistoryKind.RecycleDirectories);
        restored.ActionHistory[0].RecycledPaths.Should().ContainSingle().Which.Should().Be(@"D:\Media\Photos\2019\.thumbnails");
        restored.ActionHistory[0].Summary.Should().Be("Recycled 1 folder");
        restored.ActionHistory[0].Timestamp.Should().Be(new DateTime(2026, 3, 14, 9, 30, 0));
        restored.ActionHistory[1].Kind.Should().Be(ActionHistoryKind.MoveFiles);
        restored.ActionHistory[1].Moves.Should().ContainSingle();
        restored.ActionHistory[1].Moves[0].Source.Should().Be(@"D:\Media\Photos\IMG_4821.jpg");
        restored.ActionHistory[1].Moves[0].Destination.Should().Be(@"E:\Quarantine\IMG_4821.jpg");
    }
}
