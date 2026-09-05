using FluentAssertions;
using WindowsFileManager.Core.Models;

namespace WindowsFileManager.Tests.Models;

public class ActionHistoryEntryTests
{
    [Fact]
    public void Constructor_ShouldSetDefaults()
    {
        var entry = new ActionHistoryEntry();

        entry.Kind.Should().Be(ActionHistoryKind.MoveFiles);
        entry.Moves.Should().BeEmpty();
        entry.RecycledPaths.Should().BeEmpty();
        entry.CreatedShortcuts.Should().BeEmpty();
        entry.Summary.Should().BeEmpty();
        entry.Timestamp.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(2));
        entry.ItemCount.Should().Be(0);
    }

    [Fact]
    public void ItemCount_MoveFiles_ShouldReturnMovesCount()
    {
        var entry = new ActionHistoryEntry
        {
            Kind = ActionHistoryKind.MoveFiles,
            Moves = new List<ActionHistoryMove>
            {
                new() { Source = @"C:\a\1.txt", Destination = @"C:\b\1.txt" },
                new() { Source = @"C:\a\2.txt", Destination = @"C:\b\2.txt" },
                new() { Source = @"C:\a\3.txt", Destination = @"C:\b\3.txt" },
            },
            RecycledPaths = new List<string> { "unused" },
        };

        entry.ItemCount.Should().Be(3);
    }

    [Fact]
    public void ItemCount_RecycleFiles_ShouldReturnRecycledPathsCount()
    {
        var entry = new ActionHistoryEntry
        {
            Kind = ActionHistoryKind.RecycleFiles,
            Moves = new List<ActionHistoryMove> { new() { Source = @"C:\a", Destination = @"C:\b" } },
            RecycledPaths = new List<string> { @"C:\x\1.log", @"C:\x\2.log" },
        };

        entry.ItemCount.Should().Be(2);
    }

    [Fact]
    public void ItemCount_RecycleDirectories_ShouldReturnRecycledPathsCount()
    {
        var entry = new ActionHistoryEntry
        {
            Kind = ActionHistoryKind.RecycleDirectories,
            RecycledPaths = new List<string> { @"C:\a\bin", @"C:\b\bin", @"C:\c\bin", @"C:\d\bin" },
        };

        entry.ItemCount.Should().Be(4);
    }

    [Fact]
    public void ActionHistoryMove_Defaults_AreEmpty()
    {
        var m = new ActionHistoryMove();

        m.Source.Should().BeEmpty();
        m.Destination.Should().BeEmpty();
    }

    [Fact]
    public void ActionHistoryMove_Properties_RoundTrip()
    {
        var m = new ActionHistoryMove { Source = @"C:\src\a.txt", Destination = @"D:\dst\a.txt" };

        m.Source.Should().Be(@"C:\src\a.txt");
        m.Destination.Should().Be(@"D:\dst\a.txt");
    }

    [Fact]
    public void ItemCount_CreateShortcuts_ShouldReturnCreatedShortcutsCount()
    {
        var entry = new ActionHistoryEntry
        {
            Kind = ActionHistoryKind.CreateShortcuts,
            CreatedShortcuts = new List<string> { @"C:\1\2.lnk", @"C:\1\3.lnk", @"C:\2\1.lnk" },
            RecycledPaths = new List<string> { "unused" },
        };

        entry.ItemCount.Should().Be(3);
    }

    [Fact]
    public void CreatedShortcuts_RoundTrip_ShouldPreserveValues()
    {
        var entry = new ActionHistoryEntry
        {
            Kind = ActionHistoryKind.CreateShortcuts,
            CreatedShortcuts = new List<string> { @"C:\a\b.lnk", @"C:\a\c.lnk" },
            Summary = "Created 2 shortcuts",
        };

        entry.Kind.Should().Be(ActionHistoryKind.CreateShortcuts);
        entry.CreatedShortcuts.Should().BeEquivalentTo(new[] { @"C:\a\b.lnk", @"C:\a\c.lnk" });
    }

    [Fact]
    public void ActionHistoryKind_Ordinals_Preserved()
    {
        ((int)ActionHistoryKind.MoveFiles).Should().Be(0);
        ((int)ActionHistoryKind.RecycleFiles).Should().Be(1);
        ((int)ActionHistoryKind.RecycleDirectories).Should().Be(2);
        ((int)ActionHistoryKind.CreateShortcuts).Should().Be(3);
    }

    [Fact]
    public void Properties_ShouldRoundTrip()
    {
        var ts = new DateTime(2026, 4, 18, 10, 30, 0);
        var entry = new ActionHistoryEntry
        {
            Kind = ActionHistoryKind.RecycleFiles,
            RecycledPaths = new List<string> { @"C:\a\b.log" },
            Summary = "Recycled 1 file",
            Timestamp = ts,
        };

        entry.Kind.Should().Be(ActionHistoryKind.RecycleFiles);
        entry.RecycledPaths.Should().ContainSingle(p => p == @"C:\a\b.log");
        entry.Summary.Should().Be("Recycled 1 file");
        entry.Timestamp.Should().Be(ts);
    }

    /// <summary>serves-spec: SPEC-004 rule 25 (history pushed by SPEC-008 rule 11) — both recycle kinds fall through to RecycledPaths.Count, so RecycleDirectories ignores a populated Moves list.</summary>
    [Fact]
    public void ItemCount_RecycleDirectories_WithPopulatedMoves_StillReturnsRecycledPathsCount()
    {
        var entry = new ActionHistoryEntry
        {
            Kind = ActionHistoryKind.RecycleDirectories,
            Moves = new List<ActionHistoryMove>
            {
                new() { Source = @"D:\Media\Raw\clip-01.mov", Destination = @"D:\Media\Archive\clip-01.mov" },
                new() { Source = @"D:\Media\Raw\clip-02.mov", Destination = @"D:\Media\Archive\clip-02.mov" },
            },
            RecycledPaths = new List<string>
            {
                @"D:\Projects\web-shop\node_modules",
                @"D:\Projects\web-shop\.next",
                @"D:\Projects\api\bin",
            },
            Summary = "Recycled 3 folders",
        };

        entry.ItemCount.Should().Be(3);
    }
}
