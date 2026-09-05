using FluentAssertions;
using WindowsFileManager.Core.Models;

namespace WindowsFileManager.Tests.Models;

public class SubfolderItemTests
{
    [Fact]
    public void Constructor_ShouldSetDefaults()
    {
        var item = new SubfolderItem();

        item.Name.Should().BeEmpty();
        item.Count.Should().Be(0);
        item.TotalSize.Should().Be(0);
        item.Locations.Should().BeEmpty();
        item.IsSelected.Should().BeFalse();
        item.LocationFilter.Should().BeEmpty();
        item.CurrentPage.Should().Be(0);
        item.TotalPages.Should().Be(1);
        item.FilteredCount.Should().Be(0);
        item.PagedLocations.Should().BeEmpty();
        item.CanGoPrevPage.Should().BeFalse();
        item.CanGoNextPage.Should().BeFalse();
    }

    [Fact]
    public void IsSelected_WhenChanged_ShouldRaisePropertyChanged()
    {
        var item = new SubfolderItem();
        var changes = new List<string>();
        item.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);

        item.IsSelected = true;
        item.IsSelected = true; // No change — should not fire

        changes.Should().ContainSingle(p => p == nameof(SubfolderItem.IsSelected));
    }

    [Fact]
    public void IsSelected_NoSubscribers_ShouldNotThrow()
    {
        var item = new SubfolderItem();
        var act = () => item.IsSelected = true;
        act.Should().NotThrow();
    }

    [Fact]
    public void LocationFilter_NoSubscribers_ShouldNotThrow()
    {
        var item = MakeItemWith(3);
        var act = () =>
        {
            item.LocationFilter = "test";
            item.NextPage();
            item.PrevPage();
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void Display_ShouldCombineNameAndCount()
    {
        var item = new SubfolderItem { Name = "node_modules", Count = 14 };

        item.Display.Should().Be("node_modules (14)");
    }

    [Fact]
    public void TotalSizeDisplay_Bytes_ShouldShowB()
    {
        new SubfolderItem { TotalSize = 512 }.TotalSizeDisplay.Should().Be("512 B");
    }

    [Fact]
    public void TotalSizeDisplay_Kilobytes_ShouldShowKB()
    {
        new SubfolderItem { TotalSize = 2048 }.TotalSizeDisplay.Should().Be("2 KB");
    }

    [Theory]
    [InlineData(2L * 1024 * 1024, "2 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3 GB")]
    [InlineData(1099511627776L, "1 TB")]
    [InlineData(1125899906842624L, "1 PB")]
    public void TotalSizeDisplay_LargerUnits_ShouldScale(long bytes, string expected)
    {
        new SubfolderItem { TotalSize = bytes }.TotalSizeDisplay.Should().Be(expected);
    }

    [Fact]
    public void TotalSizeDisplay_VeryLargeBytes_ShouldCapAtPB()
    {
        // 2 PB
        long bytes = 2L * 1024 * 1024 * 1024 * 1024 * 1024;
        new SubfolderItem { TotalSize = bytes }.TotalSizeDisplay.Should().Be("2 PB");
    }

    [Fact]
    public void TotalSizeDisplay_FractionalMB_ShouldFormat()
    {
        // 1.5 MB
        new SubfolderItem { TotalSize = 1_572_864 }.TotalSizeDisplay.Should().Be("1.5 MB");
    }

    [Fact]
    public void LocationFilter_NoFilter_PagedLocationsFirstPage()
    {
        var item = MakeItemWith(120);

        item.FilteredCount.Should().Be(120);
        item.TotalPages.Should().Be(3); // 50/50/20
        item.PagedLocations.Should().HaveCount(50);
        item.PagedLocations.First().FullPath.Should().EndWith("0.log");
        item.CanGoNextPage.Should().BeTrue();
        item.CanGoPrevPage.Should().BeFalse();
    }

    [Fact]
    public void NextPage_ShouldAdvanceAndShowNextSlice()
    {
        var item = MakeItemWith(120);
        item.NextPage();

        item.CurrentPage.Should().Be(1);
        item.PagedLocations.Should().HaveCount(50);
        item.PagedLocations.First().FullPath.Should().EndWith("50.log");
        item.CanGoPrevPage.Should().BeTrue();
    }

    [Fact]
    public void NextPage_OnLastPage_ShouldBeNoOp()
    {
        var item = MakeItemWith(120);
        item.NextPage();
        item.NextPage(); // now page 2 (last)
        item.NextPage(); // should stay

        item.CurrentPage.Should().Be(2);
        item.CanGoNextPage.Should().BeFalse();
    }

    [Fact]
    public void PrevPage_OnFirstPage_ShouldBeNoOp()
    {
        var item = MakeItemWith(120);
        item.PrevPage();

        item.CurrentPage.Should().Be(0);
    }

    [Fact]
    public void PrevPage_ShouldMoveBack()
    {
        var item = MakeItemWith(120);
        item.NextPage();
        item.NextPage();
        item.PrevPage();

        item.CurrentPage.Should().Be(1);
    }

    [Fact]
    public void LocationFilter_FiltersAndResetsPage()
    {
        var item = MakeItemWith(120);
        item.NextPage(); // on page 1
        item.LocationFilter = "5";

        // Paths contain: 0.log, 1.log, ... 119.log — substrings "5" match 5, 15, 25, 35, 45, 50-59, 65, 75, 85, 95, 105, 115
        // That's 1 + 1 + 1 + 1 + 1 + 10 + 1 + 1 + 1 + 1 + 1 + 1 = 21
        item.FilteredCount.Should().Be(21);
        item.CurrentPage.Should().Be(0); // reset
        item.PagedLocations.Should().HaveCount(21);
    }

    [Fact]
    public void LocationFilter_SameValue_DoesNotReset()
    {
        var item = MakeItemWith(120);
        item.LocationFilter = "5";
        item.NextPage();
        item.LocationFilter = "5"; // same — no-op

        // NextPage is a no-op because 21 items fit on one page (CanGoNextPage is false)
        item.CurrentPage.Should().Be(0);
    }

    [Fact]
    public void LocationFilter_NullTreatedAsEmpty()
    {
        var item = MakeItemWith(3);
        item.LocationFilter = null!;

        item.LocationFilter.Should().BeEmpty();
        item.FilteredCount.Should().Be(3);
    }

    [Fact]
    public void LocationFilter_MatchesParentPath()
    {
        var item = new SubfolderItem
        {
            Name = "bin",
            Locations = new List<SubfolderLocation>
            {
                new() { ParentPath = @"C:\ProjectA", FullPath = @"C:\ProjectA\bin" },
                new() { ParentPath = @"C:\ProjectB", FullPath = @"C:\ProjectB\bin" },
            },
        };
        item.LocationFilter = "ProjectA";

        item.FilteredCount.Should().Be(1);
        item.PagedLocations.Single().ParentPath.Should().Be(@"C:\ProjectA");
    }

    [Fact]
    public void EmptyLocations_TotalPagesIsOne_PageStatusNoMatches()
    {
        var item = new SubfolderItem();

        item.TotalPages.Should().Be(1);
        item.PageStatus.Should().Be("No matches");
    }

    [Fact]
    public void PageStatus_SingleResult_UsesSingular()
    {
        var item = MakeItemWith(1);

        item.PageStatus.Should().Be("Page 1 of 1 · 1 result");
    }

    [Fact]
    public void PageStatus_MultipleResults_UsesPlural()
    {
        var item = MakeItemWith(120);

        item.PageStatus.Should().Be("Page 1 of 3 · 120 results");
        item.NextPage();
        item.PageStatus.Should().Be("Page 2 of 3 · 120 results");
    }

    [Fact]
    public void PagedLocations_WithFilter_SkipsCorrectly()
    {
        var item = MakeItemWith(200); // 4 pages
        item.LocationFilter = "1"; // matches many

        item.CurrentPage.Should().Be(0);
        var firstPage = item.PagedLocations.ToList();
        firstPage.Should().HaveCount(50);
    }

    /// <summary>serves-spec: SPEC-008 rule 7 — the location filter compares OrdinalIgnoreCase, in both FilteredCount and the PagedLocations slice.</summary>
    [Fact]
    public void LocationFilter_IsOrdinalIgnoreCase()
    {
        var item = new SubfolderItem
        {
            Name = "bin",
            Count = 2,
            Locations = new List<SubfolderLocation>
            {
                new() { ParentPath = @"C:\Projects\ProjectA", FullPath = @"C:\Projects\ProjectA\bin" },
                new() { ParentPath = @"C:\Projects\Ledger", FullPath = @"C:\Projects\Ledger\bin" },
            },
        };

        item.LocationFilter = "projecta";

        item.FilteredCount.Should().Be(1);
        item.PagedLocations.Single().FullPath.Should().Be(@"C:\Projects\ProjectA\bin");
    }

    /// <summary>serves-spec: SPEC-008 rule 7 — a changed LocationFilter raises exactly the seven paging notifications, in the documented order.</summary>
    [Fact]
    public void LocationFilter_WhenChanged_RaisesTheSevenPagingNotifications()
    {
        var item = MakeItemWith(120);
        var changes = new List<string>();
        item.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);

        item.LocationFilter = "7";

        changes.Should().Equal(new[]
        {
            nameof(SubfolderItem.CurrentPage),
            nameof(SubfolderItem.TotalPages),
            nameof(SubfolderItem.FilteredCount),
            nameof(SubfolderItem.PagedLocations),
            nameof(SubfolderItem.PageStatus),
            nameof(SubfolderItem.CanGoNextPage),
            nameof(SubfolderItem.CanGoPrevPage),
        });
    }

    /// <summary>serves-spec: SPEC-008 invariant 11 — assigning the same LocationFilter value does nothing, which includes raising no PropertyChanged traffic at all.</summary>
    [Fact]
    public void LocationFilter_SameValue_RaisesNoNotifications()
    {
        var item = MakeItemWith(120);
        item.LocationFilter = "7";
        var changes = new List<string>();
        item.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);

        item.LocationFilter = "7";

        changes.Should().BeEmpty();
    }

    /// <summary>serves-spec: SPEC-008 rule 7 — NextPage reaches the same seven paging notifications as the filter setter.</summary>
    [Fact]
    public void NextPage_RaisesTheSevenPagingNotifications()
    {
        var item = MakeItemWith(120);
        var changes = new List<string>();
        item.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);

        item.NextPage();

        changes.Should().Equal(new[]
        {
            nameof(SubfolderItem.CurrentPage),
            nameof(SubfolderItem.TotalPages),
            nameof(SubfolderItem.FilteredCount),
            nameof(SubfolderItem.PagedLocations),
            nameof(SubfolderItem.PageStatus),
            nameof(SubfolderItem.CanGoNextPage),
            nameof(SubfolderItem.CanGoPrevPage),
        });
    }

    /// <summary>serves-spec: SPEC-008 rule 7 — TotalPages is a true ceiling: a location count that is an exact multiple of the 50-item page size gains no empty trailing page.</summary>
    [Fact]
    public void TotalPages_AtExactPageSizeMultiple_DoesNotAddAnEmptyPage()
    {
        MakeItemWith(SubfolderItem.PageSize).TotalPages.Should().Be(1);
        MakeItemWith(SubfolderItem.PageSize).CanGoNextPage.Should().BeFalse();
        MakeItemWith(SubfolderItem.PageSize * 2).TotalPages.Should().Be(2);
        MakeItemWith((SubfolderItem.PageSize * 2) + 1).TotalPages.Should().Be(3);
    }

    /// <summary>serves-spec: SPEC-008 rule 7 (edge-case row "Filter matches nothing inside an expanded item") — a populated Locations list filtered down to zero matches reports "No matches", TotalPages 1 and an empty page.</summary>
    [Fact]
    public void LocationFilter_MatchingNothing_ShowsNoMatchesAndEmptyPagedLocations()
    {
        var item = MakeItemWith(120);

        item.LocationFilter = "node_modules";

        item.FilteredCount.Should().Be(0);
        item.PagedLocations.Should().BeEmpty();
        item.PageStatus.Should().Be("No matches");
        item.TotalPages.Should().Be(1);
        item.CanGoNextPage.Should().BeFalse();
    }

    /// <summary>serves-spec: SPEC-008 invariant 9 — 1023 bytes is the last value under the 1024 byte threshold and stays in bytes.</summary>
    [Fact]
    public void TotalSizeDisplay_At1023Bytes_ShouldStayInBytes()
    {
        new SubfolderItem { TotalSize = 1023 }.TotalSizeDisplay.Should().Be("1023 B");
    }

    /// <summary>serves-spec: SPEC-008 invariant 9 (rule 6 renders scan-status sizes through this formatter) — an empty inventory renders as "0 B".</summary>
    [Fact]
    public void TotalSizeDisplay_AtZero_ShouldShowZeroBytes()
    {
        new SubfolderItem { Name = "obj", Count = 0 }.TotalSizeDisplay.Should().Be("0 B");
    }

    private static SubfolderItem MakeItemWith(int n)
    {
        var locs = new List<SubfolderLocation>();
        for (int i = 0; i < n; i++)
        {
            locs.Add(new SubfolderLocation
            {
                ParentPath = @"C:\root",
                FullPath = $@"C:\root\{i}.log",
            });
        }

        return new SubfolderItem { Name = "logs", Count = n, Locations = locs };
    }
}
