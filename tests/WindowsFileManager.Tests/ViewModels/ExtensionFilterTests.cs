using FluentAssertions;
using WindowsFileManager.ViewModels;

namespace WindowsFileManager.Tests.ViewModels;

public class ExtensionFilterTests
{
    /// <summary>serves-spec: SPEC-002 rule 1 — every facet entry is built with IsChecked = true, so a fresh scan shows every extension.</summary>
    [Fact]
    public void Constructor_ShouldSetDefaults()
    {
        var filter = new ExtensionFilter();

        filter.IsChecked.Should().BeTrue();
        filter.Extension.Should().BeEmpty();
        filter.FileCount.Should().Be(0);
        filter.TotalSize.Should().Be(0);
    }

    /// <summary>serves-spec: SPEC-002 rule 1 — the facet row's displayed aggregate is TotalSize rendered through ScannedFile.FormatFileSize.</summary>
    [Fact]
    public void FormattedSize_ShouldFormatTotalSize()
    {
        var filter = new ExtensionFilter
        {
            Extension = ".jpg",
            FileCount = 214,
            TotalSize = 4_294_967_296,
        };

        filter.FormattedSize.Should().Be("4.00 GB");
    }

    /// <summary>serves-spec: SPEC-002 rule 2 — the IsChecked setter raises PropertyChanged; that notification is what the facet's ApplyFilters handler rides on.</summary>
    [Fact]
    public void IsChecked_Changed_ShouldRaisePropertyChanged()
    {
        var filter = new ExtensionFilter { Extension = ".mp4", FileCount = 12, TotalSize = 8_589_934_592 };
        var changes = new List<string>();
        filter.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);

        filter.IsChecked = false;

        filter.IsChecked.Should().BeFalse();
        changes.Should().ContainSingle(p => p == nameof(ExtensionFilter.IsChecked));
    }

    /// <summary>serves-spec: SPEC-002 rule 2 — the facet handler is not scoped to IsChecked, so a FileCount change notifies too and would re-run the whole filter pass.</summary>
    [Fact]
    public void FileCount_Changed_ShouldRaisePropertyChanged()
    {
        var filter = new ExtensionFilter { Extension = ".png", FileCount = 31, TotalSize = 268_435_456 };
        var changes = new List<string>();
        filter.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);

        filter.FileCount = 34;

        filter.FileCount.Should().Be(34);
        changes.Should().ContainSingle(p => p == nameof(ExtensionFilter.FileCount));
    }

    /// <summary>serves-spec: SPEC-002 rule 2 — a TotalSize change notifies for the same reason, which is the other half of that unscoped-handler hazard.</summary>
    [Fact]
    public void TotalSize_Changed_ShouldRaisePropertyChanged()
    {
        var filter = new ExtensionFilter { Extension = ".mkv", FileCount = 5, TotalSize = 12_884_901_888 };
        var changes = new List<string>();
        filter.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);

        filter.TotalSize = 15_032_385_536;

        filter.FormattedSize.Should().Be("14.00 GB");
        changes.Should().ContainSingle(p => p == nameof(ExtensionFilter.TotalSize));
    }

    /// <summary>serves-spec: SPEC-002 rule 2 — SetProperty swallows a no-op write, so re-checking an already-checked extension raises nothing and triggers no redundant refilter pass.</summary>
    [Fact]
    public void IsChecked_SetToSameValue_ShouldNotRaisePropertyChanged()
    {
        var filter = new ExtensionFilter { Extension = ".raw", FileCount = 48, TotalSize = 21_474_836_480 };
        var changes = new List<string>();
        filter.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);

        filter.IsChecked = true;

        filter.IsChecked.Should().BeTrue();
        changes.Should().BeEmpty();
    }
}
