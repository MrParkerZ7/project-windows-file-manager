using System.Text;
using FluentAssertions;
using Moq;
using WindowsFileManager.Application.Services;
using WindowsFileManager.Core.Models;
using WindowsFileManager.Core.Services;

namespace WindowsFileManager.Tests.Services;

public class DuplicateScannerServiceTests
{
    private readonly Mock<IFileSystemService> _mockFileSystem;
    private readonly FileHashService _hashService;
    private readonly DuplicateScannerService _service;

    public DuplicateScannerServiceTests()
    {
        _mockFileSystem = new Mock<IFileSystemService>();
        _hashService = new FileHashService(_mockFileSystem.Object);
        _service = new DuplicateScannerService(_mockFileSystem.Object, _hashService);
    }

    [Fact]
    public void Scan_EmptyTargetPaths_ShouldThrow()
    {
        var options = new ScanOptions { TargetPaths = new List<string>() };

        var act = () => _service.Scan(options);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*At least one target path*");
    }

    [Fact]
    public void Scan_DirectoryNotFound_ShouldThrow()
    {
        _mockFileSystem.Setup(fs => fs.DirectoryExists(It.IsAny<string>())).Returns(false);

        var options = new ScanOptions { TargetPaths = new List<string> { @"C:\nonexistent" } };

        var act = () => _service.Scan(options);

        act.Should().Throw<DirectoryNotFoundException>()
            .WithMessage("*C:\\nonexistent*");
    }

    [Fact]
    public void Scan_EmptyDirectory_ShouldReturnEmptyResult()
    {
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"C:\empty")).Returns(true);
        _mockFileSystem.Setup(fs => fs.EnumerateFiles(@"C:\empty", "*.*", SearchOption.AllDirectories))
            .Returns(Array.Empty<string>());

        var options = new ScanOptions { TargetPaths = new List<string> { @"C:\empty" } };
        var result = _service.Scan(options);

        result.TotalFilesScanned.Should().Be(0);
        result.DuplicateGroups.Should().BeEmpty();
        result.TotalDuplicates.Should().Be(0);
        result.TotalWastedBytes.Should().Be(0);
    }

    [Fact]
    public void Scan_NoDuplicates_ShouldReturnEmptyGroups()
    {
        SetupDirectory(@"C:\test", new[]
        {
            ("file1.txt", 100L, "content1"),
            ("file2.txt", 200L, "content2"),
            ("file3.txt", 300L, "content3"),
        });

        var options = new ScanOptions { TargetPaths = new List<string> { @"C:\test" } };
        var result = _service.Scan(options);

        result.TotalFilesScanned.Should().Be(3);
        result.DuplicateGroups.Should().BeEmpty();
    }

    [Fact]
    public void Scan_WithDuplicates_ShouldFindThem()
    {
        SetupDirectory(@"C:\test", new[]
        {
            ("a.txt", 100L, "same content"),
            ("b.txt", 100L, "same content"),
            ("c.txt", 200L, "unique"),
        });

        var options = new ScanOptions { TargetPaths = new List<string> { @"C:\test" } };
        var result = _service.Scan(options);

        result.DuplicateGroups.Should().HaveCount(1);
        result.DuplicateGroups[0].Files.Should().HaveCount(2);
        result.DuplicateGroups[0].FileSize.Should().Be(100);
        result.TotalDuplicates.Should().Be(2);
        result.TotalWastedBytes.Should().Be(100);
    }

    [Fact]
    public void Scan_SameSizeDifferentContent_ShouldNotBeDuplicates()
    {
        SetupDirectory(@"C:\test", new[]
        {
            ("a.txt", 100L, "content A"),
            ("b.txt", 100L, "content B"),
        });

        var options = new ScanOptions { TargetPaths = new List<string> { @"C:\test" } };
        var result = _service.Scan(options);

        result.DuplicateGroups.Should().BeEmpty();
    }

    [Fact]
    public void Scan_MinimumFileSize_ShouldFilterSmallFiles()
    {
        SetupDirectory(@"C:\test", new[]
        {
            ("tiny.txt", 10L, "same"),
            ("tiny2.txt", 10L, "same"),
            ("big.txt", 1000L, "same big"),
            ("big2.txt", 1000L, "same big"),
        });

        var options = new ScanOptions { TargetPaths = new List<string> { @"C:\test" }, MinimumFileSize = 100 };
        var result = _service.Scan(options);

        result.TotalFilesScanned.Should().Be(2);
        result.DuplicateGroups.Should().HaveCount(1);
        result.DuplicateGroups[0].FileSize.Should().Be(1000);
    }

    [Fact]
    public void Scan_FileExtensionFilter_ShouldFilterByExtension()
    {
        SetupDirectory(@"C:\test", new[]
        {
            ("a.txt", 100L, "same"),
            ("b.txt", 100L, "same"),
            ("c.pdf", 100L, "same"),
        });

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"C:\test" },
            FileExtensions = new List<string> { "txt" },
        };
        var result = _service.Scan(options);

        result.TotalFilesScanned.Should().Be(2);
        result.DuplicateGroups.Should().HaveCount(1);
        result.DuplicateGroups[0].Files.Should().HaveCount(2);
    }

    [Fact]
    public void Scan_TopDirectoryOnly_ShouldNotRecurse()
    {
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"C:\test")).Returns(true);
        _mockFileSystem.Setup(fs => fs.EnumerateFiles(@"C:\test", "*.*", SearchOption.TopDirectoryOnly))
            .Returns(new[] { @"C:\test\a.txt" });
        _mockFileSystem.Setup(fs => fs.GetFileSize(@"C:\test\a.txt")).Returns(100L);
        _mockFileSystem.Setup(fs => fs.GetFileName(@"C:\test\a.txt")).Returns("a.txt");
        _mockFileSystem.Setup(fs => fs.GetLastWriteTime(It.IsAny<string>())).Returns(DateTime.Now);

        var options = new ScanOptions { TargetPaths = new List<string> { @"C:\test" }, IncludeSubdirectories = false };
        var result = _service.Scan(options);

        _mockFileSystem.Verify(fs => fs.EnumerateFiles(@"C:\test", "*.*", SearchOption.TopDirectoryOnly), Times.Once);
        result.TotalFilesScanned.Should().Be(1);
    }

    [Fact]
    public void Scan_ProgressCallback_ShouldReport()
    {
        SetupDirectory(@"C:\test", new[]
        {
            ("a.txt", 100L, "x"),
            ("b.txt", 200L, "y"),
        });

        var progressValues = new List<int>();
        var options = new ScanOptions { TargetPaths = new List<string> { @"C:\test" } };
        _service.Scan(options, count => progressValues.Add(count));

        progressValues.Should().Contain(2);
        progressValues.Last().Should().Be(2);
    }

    [Fact]
    public void Scan_ProgressCallback_ShouldThrottleEvery100Files()
    {
        var files = Enumerable.Range(1, 250)
            .Select(i => ($"file{i}.txt", (long)i, $"content{i}"))
            .ToArray();
        SetupDirectory(@"C:\big", files);

        var progressValues = new List<int>();
        var options = new ScanOptions { TargetPaths = new List<string> { @"C:\big" } };
        _service.Scan(options, count => progressValues.Add(count));

        progressValues.Should().Contain(100);
        progressValues.Should().Contain(200);
        progressValues.Last().Should().Be(250);
    }

    [Fact]
    public void Scan_Cancellation_ShouldThrow()
    {
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"C:\test")).Returns(true);
        _mockFileSystem.Setup(fs => fs.EnumerateFiles(@"C:\test", "*.*", SearchOption.AllDirectories))
            .Returns(new[] { @"C:\test\a.txt", @"C:\test\b.txt" });
        _mockFileSystem.Setup(fs => fs.GetFileSize(It.IsAny<string>())).Returns(100L);
        _mockFileSystem.Setup(fs => fs.GetFileName(It.IsAny<string>())).Returns("a.txt");
        _mockFileSystem.Setup(fs => fs.GetLastWriteTime(It.IsAny<string>())).Returns(DateTime.Now);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var options = new ScanOptions { TargetPaths = new List<string> { @"C:\test" } };
        var act = () => _service.Scan(options, cancellationToken: cts.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void Scan_MultipleDuplicateGroups_ShouldSortByWastedSpace()
    {
        SetupDirectory(@"C:\test", new[]
        {
            ("small1.txt", 100L, "small same"),
            ("small2.txt", 100L, "small same"),
            ("big1.txt", 10000L, "big same"),
            ("big2.txt", 10000L, "big same"),
        });

        var options = new ScanOptions { TargetPaths = new List<string> { @"C:\test" } };
        var result = _service.Scan(options);

        result.DuplicateGroups.Should().HaveCount(2);
        result.DuplicateGroups[0].FileSize.Should().Be(10000);
        result.DuplicateGroups[1].FileSize.Should().Be(100);
    }

    [Fact]
    public void Scan_DuplicateFiles_ShouldBeSortedByPath()
    {
        SetupDirectory(@"C:\test", new[]
        {
            (@"z\file.txt", 100L, "same"),
            (@"a\file.txt", 100L, "same"),
        });

        var options = new ScanOptions { TargetPaths = new List<string> { @"C:\test" } };
        var result = _service.Scan(options);

        result.DuplicateGroups[0].Files[0].FilePath.Should().Contain("a");
        result.DuplicateGroups[0].Files[1].FilePath.Should().Contain("z");
    }

    [Fact]
    public void Scan_FileExtensionFilter_CaseInsensitive()
    {
        SetupDirectory(@"C:\test", new[]
        {
            ("a.TXT", 100L, "same"),
            ("b.txt", 100L, "same"),
        });

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"C:\test" },
            FileExtensions = new List<string> { "txt" },
        };
        var result = _service.Scan(options);

        result.TotalFilesScanned.Should().Be(2);
    }

    [Fact]
    public void Scan_MultipleFolders_ShouldFindCrossFolderDuplicates()
    {
        SetupDirectory(@"C:\folder1", new[]
        {
            ("a.txt", 100L, "shared content"),
        });
        SetupDirectory(@"C:\folder2", new[]
        {
            ("b.txt", 100L, "shared content"),
        });

        var options = new ScanOptions { TargetPaths = new List<string> { @"C:\folder1", @"C:\folder2" } };
        var result = _service.Scan(options);

        result.TotalFilesScanned.Should().Be(2);
        result.DuplicateGroups.Should().HaveCount(1);
        result.DuplicateGroups[0].Files.Should().HaveCount(2);
    }

    [Fact]
    public void Scan_OverlappingPaths_ShouldDeduplicateFiles()
    {
        // D:\ contains D:\sub, so files under D:\sub appear in both enumerations
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"D:\")).Returns(true);
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"D:\sub")).Returns(true);

        _mockFileSystem.Setup(fs => fs.EnumerateFiles(@"D:\", "*.*", It.IsAny<SearchOption>()))
            .Returns(new[] { @"D:\root.txt", @"D:\sub\child.txt" });
        _mockFileSystem.Setup(fs => fs.EnumerateFiles(@"D:\sub", "*.*", It.IsAny<SearchOption>()))
            .Returns(new[] { @"D:\sub\child.txt" });

        _mockFileSystem.Setup(fs => fs.GetFileSize(@"D:\root.txt")).Returns(100L);
        _mockFileSystem.Setup(fs => fs.GetFileSize(@"D:\sub\child.txt")).Returns(100L);
        _mockFileSystem.Setup(fs => fs.GetFileName(@"D:\root.txt")).Returns("root.txt");
        _mockFileSystem.Setup(fs => fs.GetFileName(@"D:\sub\child.txt")).Returns("child.txt");
        _mockFileSystem.Setup(fs => fs.GetLastWriteTime(It.IsAny<string>())).Returns(DateTime.Now);
        _mockFileSystem.Setup(fs => fs.OpenRead(@"D:\root.txt"))
            .Returns(() => new MemoryStream(Encoding.UTF8.GetBytes("content A")));
        _mockFileSystem.Setup(fs => fs.OpenRead(@"D:\sub\child.txt"))
            .Returns(() => new MemoryStream(Encoding.UTF8.GetBytes("content B")));

        var options = new ScanOptions { TargetPaths = new List<string> { @"D:\", @"D:\sub" } };
        var result = _service.Scan(options);

        // child.txt should only be counted once despite appearing in both paths
        result.TotalFilesScanned.Should().Be(2);
        result.DuplicateGroups.Should().BeEmpty();
    }

    [Fact]
    public void Scan_MultipleFolders_OneNotFound_ShouldThrow()
    {
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"C:\exists")).Returns(true);
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"C:\missing")).Returns(false);

        var options = new ScanOptions { TargetPaths = new List<string> { @"C:\exists", @"C:\missing" } };
        var act = () => _service.Scan(options);

        act.Should().Throw<DirectoryNotFoundException>()
            .WithMessage("*C:\\missing*");
    }

    [Fact]
    public void Scan_WithExcludeFolderNames_ShouldSkipExcludedSubfolders()
    {
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"C:\root")).Returns(true);

        // Root has a few files
        _mockFileSystem.Setup(fs => fs.EnumerateFiles(@"C:\root", "*.*", SearchOption.TopDirectoryOnly))
            .Returns(new[] { @"C:\root\a.txt", @"C:\root\b.txt" });

        // Root contains "src" (kept) and "node_modules" (excluded)
        _mockFileSystem.Setup(fs => fs.EnumerateDirectories(@"C:\root"))
            .Returns(new[] { @"C:\root\src", @"C:\root\node_modules" });

        _mockFileSystem.Setup(fs => fs.EnumerateFiles(@"C:\root\src", "*.*", SearchOption.TopDirectoryOnly))
            .Returns(new[] { @"C:\root\src\code.cs" });
        _mockFileSystem.Setup(fs => fs.EnumerateDirectories(@"C:\root\src"))
            .Returns(Array.Empty<string>());

        _mockFileSystem.Setup(fs => fs.GetFileSize(It.IsAny<string>())).Returns(100L);
        _mockFileSystem.Setup(fs => fs.GetFileName(It.IsAny<string>()))
            .Returns((string p) => Path.GetFileName(p));
        _mockFileSystem.Setup(fs => fs.GetLastWriteTime(It.IsAny<string>())).Returns(DateTime.Now);
        _mockFileSystem.Setup(fs => fs.OpenRead(It.IsAny<string>()))
            .Returns(() => new MemoryStream(Encoding.UTF8.GetBytes("unique")));

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"C:\root" },
            ExcludeFolderNames = new List<string> { "node_modules" },
        };
        var result = _service.Scan(options);

        result.TotalFilesScanned.Should().Be(3); // a.txt, b.txt, src/code.cs — node_modules never enumerated
        _mockFileSystem.Verify(fs => fs.EnumerateDirectories(@"C:\root\node_modules"), Times.Never);
    }

    [Fact]
    public void Scan_WithExcludeFolderNames_UnauthorizedAccess_ShouldYieldBreak()
    {
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"C:\locked")).Returns(true);
        _mockFileSystem.Setup(fs => fs.EnumerateFiles(@"C:\locked", "*.*", SearchOption.TopDirectoryOnly))
            .Returns(Array.Empty<string>());
        _mockFileSystem.Setup(fs => fs.EnumerateDirectories(@"C:\locked"))
            .Throws(new UnauthorizedAccessException("denied"));

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"C:\locked" },
            ExcludeFolderNames = new List<string> { "skip" },
        };
        var result = _service.Scan(options);

        result.TotalFilesScanned.Should().Be(0);
    }

    [Fact]
    public void Scan_WithExcludeFolderNames_IOException_ShouldYieldBreak()
    {
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"C:\ioerr")).Returns(true);
        _mockFileSystem.Setup(fs => fs.EnumerateFiles(@"C:\ioerr", "*.*", SearchOption.TopDirectoryOnly))
            .Returns(Array.Empty<string>());
        _mockFileSystem.Setup(fs => fs.EnumerateDirectories(@"C:\ioerr"))
            .Throws(new IOException("bad"));

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"C:\ioerr" },
            ExcludeFolderNames = new List<string> { "skip" },
        };
        var result = _service.Scan(options);

        result.TotalFilesScanned.Should().Be(0);
    }

    [Fact]
    public void Scan_RegexMode_GroupsFilesByCaptures_IgnoringSize()
    {
        SetupDirectory(@"C:\test", new[]
        {
            ("abc-12345678.jpg", 100L, "totally different content"),
            ("bla bla abc 12345678.png", 9999L, "also different content"),
            ("abc-87654321.jpg", 100L, "different key"),
            ("xyz-99999999.jpg", 50L, "no abc"),
        });

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"C:\test" },
            MatchRegex = "(?i)(abc).*?(\\d{8})",
        };
        var result = _service.Scan(options);

        result.DuplicateGroups.Should().HaveCount(1);
        result.DuplicateGroups[0].Files.Should().HaveCount(2);
        result.DuplicateGroups[0].Files.Select(f => f.FileName)
            .Should().BeEquivalentTo(new[] { "abc-12345678.jpg", "bla bla abc 12345678.png" });

        // FileSize = max of group; WastedBytes = sum - max = 9999+100 - 9999 = 100
        result.DuplicateGroups[0].FileSize.Should().Be(9999);
        result.DuplicateGroups[0].WastedBytes.Should().Be(100);
    }

    [Fact]
    public void Scan_RegexMode_NoCaptureGroups_UsesFullMatchAsKey()
    {
        SetupDirectory(@"C:\test", new[]
        {
            ("invoice-2026.pdf", 100L, "x"),
            ("backup_invoice-2026.pdf.bak", 9999L, "y"),
            ("invoice-2025.pdf", 100L, "z"),
        });

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"C:\test" },
            MatchRegex = "invoice-2026",
        };
        var result = _service.Scan(options);

        result.DuplicateGroups.Should().HaveCount(1);
        result.DuplicateGroups[0].Files.Should().HaveCount(2);
    }

    [Fact]
    public void Scan_RegexMode_CaseSensitiveByDefault()
    {
        SetupDirectory(@"C:\test", new[]
        {
            ("ABC-1.txt", 100L, "x"),
            ("abc-1.txt", 100L, "y"),
        });

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"C:\test" },
            MatchRegex = "(abc)",
        };
        var result = _service.Scan(options);

        // Without (?i), 'ABC-1.txt' does not match 'abc' → only one file matches → no group
        result.DuplicateGroups.Should().BeEmpty();
    }

    [Fact]
    public void Scan_RegexMode_InvalidPattern_ShouldThrowArgumentException()
    {
        SetupDirectory(@"C:\test", new[]
        {
            ("a.txt", 100L, "x"),
            ("b.txt", 100L, "y"),
        });

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"C:\test" },
            MatchRegex = "[unclosed",
        };

        var act = () => _service.Scan(options);

        act.Should().Throw<ArgumentException>().WithMessage("*Invalid duplicate-match regex*");
    }

    [Fact]
    public void Scan_RegexMode_CatastrophicBacktracking_ShouldThrowArgumentException()
    {
        // 60-char input with no terminating 'X' → classic catastrophic backtracking trigger
        // for nested-quantifier patterns like (a+)+. Without the 1s MatchTimeout the scan would hang.
        var bigName = new string('a', 60) + ".txt";
        SetupDirectory(@"C:\test", new[]
        {
            (bigName, 100L, "x"),
            ("other.txt", 100L, "y"),
        });

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"C:\test" },
            MatchRegex = "(a+)+X",
        };

        var act = () => _service.Scan(options);

        act.Should().Throw<ArgumentException>().WithMessage("*timed out*");
    }

    [Fact]
    public void Scan_RegexMode_FilesNotMatching_ShouldBeSkipped()
    {
        SetupDirectory(@"C:\test", new[]
        {
            ("abc-1.txt", 100L, "x"),
            ("abc-1-copy.txt", 100L, "y"),
            ("readme.md", 100L, "z"),
        });

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"C:\test" },
            MatchRegex = "(abc-\\d+)",
        };
        var result = _service.Scan(options);

        result.DuplicateGroups.Should().HaveCount(1);
        result.DuplicateGroups[0].Files.Should().HaveCount(2);
    }

    /// <summary>serves-spec: SPEC-001 rule 1 — both guards complete before any enumeration, so a three-folder scan whose third folder is missing enumerates nothing.</summary>
    [Fact]
    public void Scan_ThirdPathMissing_EnumeratesNothingBeforeThrowing()
    {
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"D:\Media\Photos\2024")).Returns(true);
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"D:\Media\Photos\2025")).Returns(true);
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"D:\Media\Photos\2026-archive")).Returns(false);

        var options = new ScanOptions
        {
            TargetPaths = new List<string>
            {
                @"D:\Media\Photos\2024",
                @"D:\Media\Photos\2025",
                @"D:\Media\Photos\2026-archive",
            },
        };

        var act = () => _service.Scan(options);

        act.Should().Throw<DirectoryNotFoundException>()
            .WithMessage(@"*D:\Media\Photos\2026-archive*");
        _mockFileSystem.Verify(
            fs => fs.EnumerateFiles(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SearchOption>()),
            Times.Never);
        _mockFileSystem.Verify(fs => fs.EnumerateDirectories(It.IsAny<string>()), Times.Never);
    }

    /// <summary>serves-spec: SPEC-001 rule 2 — the stopwatch starts once the paths validate and runs over the whole scan, so a completed scan reports a positive Duration.</summary>
    [Fact]
    public void Scan_CompletedScan_ReportsPositiveDuration()
    {
        SetupDirectory(@"E:\Backups\2026-01", new[]
        {
            ("vm-image-a.vhdx", 10485760L, "disk image alpha"),
            ("vm-image-b.vhdx", 10485760L, "disk image alpha"),
            ("config-1.xml", 524288L, "config payload"),
            ("config-2.xml", 524288L, "config payload"),
        });

        var options = new ScanOptions { TargetPaths = new List<string> { @"E:\Backups\2026-01" } };
        var result = _service.Scan(options);

        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    /// <summary>serves-spec: SPEC-001 rule 3 — with no exclusions and IncludeSubdirectories true, one EnumerateFiles per target runs with SearchOption.AllDirectories.</summary>
    [Fact]
    public void Scan_IncludeSubdirectoriesTrue_NoExclusions_UsesAllDirectories()
    {
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"D:\Media\Photos")).Returns(true);
        _mockFileSystem.Setup(fs => fs.EnumerateFiles(@"D:\Media\Photos", "*.*", SearchOption.AllDirectories))
            .Returns(new[] { @"D:\Media\Photos\2024\IMG_4821.jpg" });
        _mockFileSystem.Setup(fs => fs.GetFileSize(@"D:\Media\Photos\2024\IMG_4821.jpg")).Returns(2458112L);
        _mockFileSystem.Setup(fs => fs.GetFileName(@"D:\Media\Photos\2024\IMG_4821.jpg")).Returns("IMG_4821.jpg");
        _mockFileSystem.Setup(fs => fs.GetLastWriteTime(It.IsAny<string>()))
            .Returns(new DateTime(2024, 11, 3, 14, 22, 51, DateTimeKind.Utc));

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"D:\Media\Photos" },
            IncludeSubdirectories = true,
        };
        var result = _service.Scan(options);

        result.TotalFilesScanned.Should().Be(1);
        _mockFileSystem.Verify(
            fs => fs.EnumerateFiles(@"D:\Media\Photos", "*.*", SearchOption.AllDirectories),
            Times.Once);
        _mockFileSystem.Verify(
            fs => fs.EnumerateFiles(It.IsAny<string>(), It.IsAny<string>(), SearchOption.TopDirectoryOnly),
            Times.Never);
        _mockFileSystem.Verify(fs => fs.EnumerateDirectories(It.IsAny<string>()), Times.Never);
    }

    /// <summary>serves-spec: SPEC-001 rule 3 — exclusions are bypassed when IncludeSubdirectories is false: the excluding walker is not used and no directory is listed.</summary>
    [Fact]
    public void Scan_ExcludeNamesSetButIncludeSubdirectoriesFalse_ExclusionsBypassed()
    {
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"C:\Projects\WebApp")).Returns(true);
        _mockFileSystem.Setup(fs => fs.EnumerateFiles(@"C:\Projects\WebApp", "*.*", SearchOption.TopDirectoryOnly))
            .Returns(new[] { @"C:\Projects\WebApp\package.json" });
        _mockFileSystem.Setup(fs => fs.GetFileSize(@"C:\Projects\WebApp\package.json")).Returns(1842L);
        _mockFileSystem.Setup(fs => fs.GetFileName(@"C:\Projects\WebApp\package.json")).Returns("package.json");
        _mockFileSystem.Setup(fs => fs.GetLastWriteTime(It.IsAny<string>()))
            .Returns(new DateTime(2026, 1, 14, 9, 5, 33, DateTimeKind.Utc));

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"C:\Projects\WebApp" },
            IncludeSubdirectories = false,
            ExcludeFolderNames = new List<string> { "node_modules", ".git" },
        };
        var result = _service.Scan(options);

        result.TotalFilesScanned.Should().Be(1);
        _mockFileSystem.Verify(
            fs => fs.EnumerateFiles(@"C:\Projects\WebApp", "*.*", SearchOption.TopDirectoryOnly),
            Times.Once);
        _mockFileSystem.Verify(fs => fs.EnumerateDirectories(It.IsAny<string>()), Times.Never);
    }

    /// <summary>serves-spec: SPEC-001 rule 3 — the exclude set is OrdinalIgnoreCase, so a folder named NODE_MODULES is skipped by an exclusion spelled node_modules.</summary>
    [Fact]
    public void Scan_ExcludedFolderNameCasingDiffers_StillSkipped()
    {
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"C:\Projects\WebApp")).Returns(true);
        _mockFileSystem.Setup(fs => fs.EnumerateFiles(@"C:\Projects\WebApp", "*.*", SearchOption.TopDirectoryOnly))
            .Returns(new[] { @"C:\Projects\WebApp\package.json" });
        _mockFileSystem.Setup(fs => fs.EnumerateDirectories(@"C:\Projects\WebApp"))
            .Returns(new[] { @"C:\Projects\WebApp\src", @"C:\Projects\WebApp\NODE_MODULES" });
        _mockFileSystem.Setup(fs => fs.EnumerateFiles(@"C:\Projects\WebApp\src", "*.*", SearchOption.TopDirectoryOnly))
            .Returns(new[] { @"C:\Projects\WebApp\src\App.tsx" });
        _mockFileSystem.Setup(fs => fs.EnumerateDirectories(@"C:\Projects\WebApp\src"))
            .Returns(Array.Empty<string>());
        _mockFileSystem.Setup(fs => fs.GetFileSize(@"C:\Projects\WebApp\package.json")).Returns(1842L);
        _mockFileSystem.Setup(fs => fs.GetFileSize(@"C:\Projects\WebApp\src\App.tsx")).Returns(6310L);
        _mockFileSystem.Setup(fs => fs.GetFileName(It.IsAny<string>()))
            .Returns((string p) => Path.GetFileName(p));
        _mockFileSystem.Setup(fs => fs.GetLastWriteTime(It.IsAny<string>()))
            .Returns(new DateTime(2026, 1, 14, 9, 5, 33, DateTimeKind.Utc));

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"C:\Projects\WebApp" },
            ExcludeFolderNames = new List<string> { "node_modules" },
        };
        var result = _service.Scan(options);

        result.TotalFilesScanned.Should().Be(2);
        _mockFileSystem.Verify(
            fs => fs.EnumerateFiles(@"C:\Projects\WebApp\NODE_MODULES", "*.*", It.IsAny<SearchOption>()),
            Times.Never);
        _mockFileSystem.Verify(fs => fs.EnumerateDirectories(@"C:\Projects\WebApp\NODE_MODULES"), Times.Never);
    }

    /// <summary>serves-spec: SPEC-001 rule 3 — EnumerateFilesExcluding recurses into itself, so the exclude set applies at every depth, not only directly under a target.</summary>
    [Fact]
    public void Scan_ExcludedFolderNestedTwoLevelsDeep_StillSkipped()
    {
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"D:\Repos\storefront")).Returns(true);
        _mockFileSystem.Setup(fs => fs.EnumerateFiles(@"D:\Repos\storefront", "*.*", SearchOption.TopDirectoryOnly))
            .Returns(new[] { @"D:\Repos\storefront\README.md" });
        _mockFileSystem.Setup(fs => fs.EnumerateDirectories(@"D:\Repos\storefront"))
            .Returns(new[] { @"D:\Repos\storefront\packages" });
        _mockFileSystem.Setup(fs => fs.EnumerateFiles(@"D:\Repos\storefront\packages", "*.*", SearchOption.TopDirectoryOnly))
            .Returns(Array.Empty<string>());
        _mockFileSystem.Setup(fs => fs.EnumerateDirectories(@"D:\Repos\storefront\packages"))
            .Returns(new[] { @"D:\Repos\storefront\packages\ui" });
        _mockFileSystem.Setup(fs => fs.EnumerateFiles(@"D:\Repos\storefront\packages\ui", "*.*", SearchOption.TopDirectoryOnly))
            .Returns(new[] { @"D:\Repos\storefront\packages\ui\index.ts" });
        _mockFileSystem.Setup(fs => fs.EnumerateDirectories(@"D:\Repos\storefront\packages\ui"))
            .Returns(new[] { @"D:\Repos\storefront\packages\ui\node_modules" });
        _mockFileSystem.Setup(fs => fs.GetFileSize(@"D:\Repos\storefront\README.md")).Returns(3204L);
        _mockFileSystem.Setup(fs => fs.GetFileSize(@"D:\Repos\storefront\packages\ui\index.ts")).Returns(11890L);
        _mockFileSystem.Setup(fs => fs.GetFileName(It.IsAny<string>()))
            .Returns((string p) => Path.GetFileName(p));
        _mockFileSystem.Setup(fs => fs.GetLastWriteTime(It.IsAny<string>()))
            .Returns(new DateTime(2026, 2, 2, 18, 40, 12, DateTimeKind.Utc));

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"D:\Repos\storefront" },
            ExcludeFolderNames = new List<string> { "node_modules" },
        };
        var result = _service.Scan(options);

        result.TotalFilesScanned.Should().Be(2);
        _mockFileSystem.Verify(
            fs => fs.EnumerateFiles(@"D:\Repos\storefront\packages\ui\node_modules", "*.*", It.IsAny<SearchOption>()),
            Times.Never);
        _mockFileSystem.Verify(
            fs => fs.EnumerateDirectories(@"D:\Repos\storefront\packages\ui\node_modules"),
            Times.Never);
    }

    /// <summary>serves-spec: SPEC-001 rule 4 — overlapping targets are de-duplicated with StringComparer.OrdinalIgnoreCase, so one file reached under two path casings is counted once.</summary>
    [Fact]
    public void Scan_OverlappingTargetsWithDifferentPathCasing_DeduplicatedOnce()
    {
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"D:\Archive")).Returns(true);
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"D:\Archive\Contracts")).Returns(true);
        _mockFileSystem.Setup(fs => fs.EnumerateFiles(@"D:\Archive", "*.*", It.IsAny<SearchOption>()))
            .Returns(new[] { @"D:\Archive\Contracts\Lease-Agreement-2026.pdf" });
        _mockFileSystem.Setup(fs => fs.EnumerateFiles(@"D:\Archive\Contracts", "*.*", It.IsAny<SearchOption>()))
            .Returns(new[] { @"d:\archive\contracts\lease-agreement-2026.pdf" });
        _mockFileSystem.Setup(fs => fs.GetFileSize(It.IsAny<string>())).Returns(184320L);
        _mockFileSystem.Setup(fs => fs.GetFileName(It.IsAny<string>()))
            .Returns((string p) => Path.GetFileName(p));
        _mockFileSystem.Setup(fs => fs.GetLastWriteTime(It.IsAny<string>()))
            .Returns(new DateTime(2026, 1, 9, 11, 47, 2, DateTimeKind.Utc));

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"D:\Archive", @"D:\Archive\Contracts" },
        };
        var result = _service.Scan(options);

        result.TotalFilesScanned.Should().Be(1);
        result.DuplicateGroups.Should().BeEmpty();
        _mockFileSystem.Verify(fs => fs.OpenRead(It.IsAny<string>()), Times.Never);
    }

    /// <summary>serves-spec: SPEC-001 rule 5 — a surviving file becomes a ScannedFile carrying path, name, size and GetLastWriteTime.</summary>
    [Fact]
    public void Scan_KeptFile_CarriesPathNameSizeAndLastWriteTime()
    {
        var shotAt = new DateTime(2024, 11, 3, 14, 22, 51, DateTimeKind.Utc);
        var copiedAt = new DateTime(2025, 6, 18, 7, 3, 9, DateTimeKind.Utc);

        SetupDirectory(@"D:\Media\Photos\2024", new[]
        {
            ("IMG_4821.jpg", 2458112L, "JFIF camera raw payload for IMG_4821"),
            ("IMG_4822.jpg", 2458112L, "JFIF camera raw payload for IMG_4821"),
        });
        _mockFileSystem.Setup(fs => fs.GetLastWriteTime(@"D:\Media\Photos\2024\IMG_4821.jpg")).Returns(shotAt);
        _mockFileSystem.Setup(fs => fs.GetLastWriteTime(@"D:\Media\Photos\2024\IMG_4822.jpg")).Returns(copiedAt);

        var options = new ScanOptions { TargetPaths = new List<string> { @"D:\Media\Photos\2024" } };
        var result = _service.Scan(options);

        var kept = result.DuplicateGroups.Should().ContainSingle().Subject.Files;
        kept[0].FilePath.Should().Be(@"D:\Media\Photos\2024\IMG_4821.jpg");
        kept[0].FileName.Should().Be("IMG_4821.jpg");
        kept[0].FileSize.Should().Be(2458112L);
        kept[0].LastModified.Should().Be(shotAt);
        kept[1].LastModified.Should().Be(copiedAt);
    }

    /// <summary>serves-spec: SPEC-001 rule 5 — the size guard drops a file only when size is strictly below MinimumFileSize, so a file exactly at the minimum is kept.</summary>
    [Fact]
    public void Scan_FileSizeExactlyMinimumFileSize_IsKept()
    {
        SetupDirectory(@"E:\Backups\2026-01", new[]
        {
            ("db-snapshot-a.bak", 1048576L, "snapshot payload"),
            ("db-snapshot-b.bak", 1048576L, "snapshot payload"),
            ("thumbs.db", 1048575L, "shell thumbnail cache"),
        });

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"E:\Backups\2026-01" },
            MinimumFileSize = 1048576L,
        };
        var result = _service.Scan(options);

        result.TotalFilesScanned.Should().Be(2);
        result.DuplicateGroups.Should().ContainSingle()
            .Subject.Files.Should().OnlyContain(f => f.FileSize == 1048576L);
        _mockFileSystem.Verify(fs => fs.GetFileName(@"E:\Backups\2026-01\thumbs.db"), Times.Never);
    }

    /// <summary>serves-spec: SPEC-001 edge case "Zero-byte file" — the default MinimumFileSize of 1 excludes a zero-byte file from the scan.</summary>
    [Fact]
    public void Scan_ZeroByteFileWithDefaultOptions_IsExcluded()
    {
        SetupDirectory(@"C:\Users\priva\Documents", new[]
        {
            ("placeholder.txt", 0L, string.Empty),
            ("budget-2026.xlsx", 24576L, "spreadsheet payload"),
        });

        var options = new ScanOptions { TargetPaths = new List<string> { @"C:\Users\priva\Documents" } };
        var result = _service.Scan(options);

        result.TotalFilesScanned.Should().Be(1);
        _mockFileSystem.Verify(fs => fs.GetFileName(@"C:\Users\priva\Documents\placeholder.txt"), Times.Never);
    }

    /// <summary>serves-spec: SPEC-001 rule 5 — an extensionless file yields an empty extension, which matches no entry in a non-empty FileExtensions list.</summary>
    [Fact]
    public void Scan_FileWithNoExtension_AndExtensionFilterSet_IsDropped()
    {
        SetupDirectory(@"D:\Repos\storefront", new[]
        {
            ("LICENSE", 1071L, "MIT license text"),
            ("README.txt", 4096L, "readme text"),
        });

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"D:\Repos\storefront" },
            FileExtensions = new List<string> { "txt" },
        };
        var result = _service.Scan(options);

        result.TotalFilesScanned.Should().Be(1);
    }

    /// <summary>serves-spec: SPEC-001 rule 7 — the unconditional final progress call fires even when every enumerated file was filtered out.</summary>
    [Fact]
    public void Scan_ZeroKeptFiles_ProgressInvokedOnceWithZero()
    {
        SetupDirectory(@"E:\Backups\2026-01", new[]
        {
            ("desktop.ini", 282L, "shell folder settings"),
            ("thumbs.db", 512L, "shell thumbnail cache"),
        });

        var progressValues = new List<int>();
        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"E:\Backups\2026-01" },
            MinimumFileSize = 1024L,
        };
        var result = _service.Scan(options, count => progressValues.Add(count));

        result.TotalFilesScanned.Should().Be(0);
        progressValues.Should().ContainSingle().Which.Should().Be(0);
    }

    /// <summary>serves-spec: SPEC-001 rule 7 — at exactly 100 kept files the every-100 throttle and the unconditional final call collide, so 100 is reported twice.</summary>
    [Fact]
    public void Scan_ExactlyOneHundredKeptFiles_ProgressReportsHundredTwice()
    {
        var files = Enumerable.Range(1, 100)
            .Select(i => ($"page-{i:D3}.html", 4096L + i, $"<html>page {i}</html>"))
            .ToArray();
        SetupDirectory(@"D:\Repos\storefront\dist", files);

        var progressValues = new List<int>();
        var options = new ScanOptions { TargetPaths = new List<string> { @"D:\Repos\storefront\dist" } };
        var result = _service.Scan(options, count => progressValues.Add(count));

        result.TotalFilesScanned.Should().Be(100);
        progressValues.Should().Equal(100, 100);
    }

    /// <summary>serves-spec: SPEC-001 rule 8 — grouping mode is chosen with IsNullOrWhiteSpace, so a whitespace-only MatchRegex falls back to size plus content hash.</summary>
    [Fact]
    public void Scan_MatchRegexWhitespaceOnly_FallsBackToSizeAndHash()
    {
        SetupDirectory(@"D:\Media\Photos\2024", new[]
        {
            ("IMG_4821.jpg", 2458112L, "JFIF camera raw payload for IMG_4821"),
            ("IMG_4822.jpg", 2458112L, "JFIF camera raw payload for IMG_4821"),
        });

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"D:\Media\Photos\2024" },
            MatchRegex = "   ",
        };
        var result = _service.Scan(options);

        var group = result.DuplicateGroups.Should().ContainSingle().Subject;
        group.Files.Should().HaveCount(2);
        group.Hash.Should().MatchRegex("^[0-9A-F]{64}$");
        _mockFileSystem.Verify(fs => fs.OpenRead(@"D:\Media\Photos\2024\IMG_4821.jpg"), Times.Once);
        _mockFileSystem.Verify(fs => fs.OpenRead(@"D:\Media\Photos\2024\IMG_4822.jpg"), Times.Once);
    }

    /// <summary>serves-spec: SPEC-001 invariant "a unique-sized file is never opened" — the size pre-filter is what makes three-stage detection cheap (ADR-003).</summary>
    [Fact]
    public void Scan_UniqueSizedFile_IsNeverOpened()
    {
        SetupDirectory(@"D:\Media\Music", new[]
        {
            ("track-01.flac", 41943040L, "FLAC stream for track 01"),
            ("track-01 (1).flac", 41943040L, "FLAC stream for track 01"),
            ("cover.jpg", 262144L, "album art payload"),
        });

        var options = new ScanOptions { TargetPaths = new List<string> { @"D:\Media\Music" } };
        var result = _service.Scan(options);

        result.TotalFilesScanned.Should().Be(3);
        _mockFileSystem.Verify(fs => fs.OpenRead(@"D:\Media\Music\cover.jpg"), Times.Never);
        _mockFileSystem.Verify(fs => fs.OpenRead(@"D:\Media\Music\track-01.flac"), Times.Once);
        _mockFileSystem.Verify(fs => fs.OpenRead(@"D:\Media\Music\track-01 (1).flac"), Times.Once);
    }

    /// <summary>serves-spec: SPEC-001 invariant "the hash is SHA-256 over the entire stream, uppercase hex" — the group Hash is that digest of the shared content.</summary>
    [Fact]
    public void Scan_HashMode_GroupHashIsUppercaseHexSha256OfContent()
    {
        const string ExpectedSha256 = "000E381D1976B9ED047441EBDD304FDFD50520509CD06AD92C7C69B4CAD52815";

        SetupDirectory(@"D:\Media\Photos\2024", new[]
        {
            ("IMG_4821.jpg", 2458112L, "JFIF camera raw payload for IMG_4821"),
            ("IMG_4822.jpg", 2458112L, "JFIF camera raw payload for IMG_4821"),
        });

        var options = new ScanOptions { TargetPaths = new List<string> { @"D:\Media\Photos\2024" } };
        var result = _service.Scan(options);

        var group = result.DuplicateGroups.Should().ContainSingle().Subject;
        group.Hash.Should().Be(ExpectedSha256);
        group.Hash.Should().HaveLength(64);
        group.Files.Should().OnlyContain(f => f.Hash == ExpectedSha256);
    }

    /// <summary>serves-spec: SPEC-001 rule 9 — three byte-identical files of equal size collapse into a single group of three, not into pairs.</summary>
    [Fact]
    public void Scan_ThreeIdenticalFiles_FormOneGroupOfThree()
    {
        SetupDirectory(@"E:\Backups\2026-01", new[]
        {
            ("config-1.xml", 524288L, "config payload"),
            ("config-2.xml", 524288L, "config payload"),
            ("config-3.xml", 524288L, "config payload"),
        });

        var options = new ScanOptions { TargetPaths = new List<string> { @"E:\Backups\2026-01" } };
        var result = _service.Scan(options);

        var group = result.DuplicateGroups.Should().ContainSingle().Subject;
        group.Files.Should().HaveCount(3);
        group.FileSize.Should().Be(524288L);
        group.WastedBytes.Should().Be(1048576L);
        result.TotalDuplicates.Should().Be(3);
    }

    /// <summary>serves-spec: SPEC-001 rule 9 — inside one size group the hash re-grouping keeps only the byte-identical members and drops the odd one out.</summary>
    [Fact]
    public void Scan_SizeGroupOfThreeWithOnlyTwoIdentical_GroupsOnlyTheMatchingPair()
    {
        SetupDirectory(@"E:\Backups\2026-01", new[]
        {
            ("vm-image-a.vhdx", 5242880L, "disk image alpha"),
            ("vm-image-b.vhdx", 5242880L, "disk image alpha"),
            ("vm-image-c.vhdx", 5242880L, "disk image gamma"),
        });

        var options = new ScanOptions { TargetPaths = new List<string> { @"E:\Backups\2026-01" } };
        var result = _service.Scan(options);

        var group = result.DuplicateGroups.Should().ContainSingle().Subject;
        group.Files.Select(f => f.FileName)
            .Should().BeEquivalentTo(new[] { "vm-image-a.vhdx", "vm-image-b.vhdx" });
        result.TotalDuplicates.Should().Be(2);
    }

    /// <summary>serves-spec: SPEC-001 rule 9 — one size group holding two distinct content hashes yields two separate duplicate groups.</summary>
    [Fact]
    public void Scan_OneSizeGroupWithTwoDistinctHashPairs_ProducesTwoGroups()
    {
        SetupDirectory(@"D:\Documents\Manuscript", new[]
        {
            ("chapter-01.docx", 3145728L, "manuscript chapter one body"),
            ("chapter-01-backup.docx", 3145728L, "manuscript chapter one body"),
            ("chapter-02.docx", 3145728L, "manuscript chapter two body"),
            ("chapter-02-backup.docx", 3145728L, "manuscript chapter two body"),
        });

        var options = new ScanOptions { TargetPaths = new List<string> { @"D:\Documents\Manuscript" } };
        var result = _service.Scan(options);

        result.DuplicateGroups.Should().HaveCount(2);
        result.DuplicateGroups.Should().OnlyContain(g => g.Files.Count == 2);
        result.DuplicateGroups.Select(g => g.Hash).Should().OnlyHaveUniqueItems();
        result.DuplicateGroups.SelectMany(g => g.Files).Select(f => f.FileName)
            .Should().BeEquivalentTo(new[]
            {
                "chapter-01.docx",
                "chapter-01-backup.docx",
                "chapter-02.docx",
                "chapter-02-backup.docx",
            });
    }

    /// <summary>serves-spec: SPEC-001 invariant "cancellation is observed per size-group before hashing" — cancelling from the final progress callback aborts before any file is opened.</summary>
    [Fact]
    public void Scan_CancelledInsideProgressCallback_ThrowsBeforeAnyOpenRead()
    {
        SetupDirectory(@"D:\Media\Photos\2024", new[]
        {
            ("IMG_4821.jpg", 2458112L, "JFIF camera raw payload for IMG_4821"),
            ("IMG_4822.jpg", 2458112L, "JFIF camera raw payload for IMG_4821"),
        });

        using var cts = new CancellationTokenSource();
        var options = new ScanOptions { TargetPaths = new List<string> { @"D:\Media\Photos\2024" } };

        var act = () => _service.Scan(options, _ => cts.Cancel(), cts.Token);

        act.Should().Throw<OperationCanceledException>();
        _mockFileSystem.Verify(fs => fs.OpenRead(It.IsAny<string>()), Times.Never);
    }

    /// <summary>serves-spec: SPEC-001 invariant "in regex mode neither size nor content is read" — the content half: no file is opened, though GetFileSize still runs for the minimum-size filter.</summary>
    [Fact]
    public void Scan_RegexMode_NeverCallsOpenRead()
    {
        SetupDirectory(@"D:\Finance\Invoices", new[]
        {
            ("acme-INV-2026-0042.pdf", 500000L, "acme invoice body"),
            ("acme-INV-2026-0042-copy.pdf", 400000L, "acme invoice rescan body"),
        });

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"D:\Finance\Invoices" },
            MatchRegex = @"(INV-\d{4}-\d{4})",
        };
        var result = _service.Scan(options);

        result.DuplicateGroups.Should().ContainSingle();
        _mockFileSystem.Verify(fs => fs.OpenRead(It.IsAny<string>()), Times.Never);
        _mockFileSystem.Verify(fs => fs.GetFileSize(It.IsAny<string>()), Times.Exactly(2));
    }

    /// <summary>serves-spec: SPEC-001 rule 10 — the pattern is matched against FileName only, so a pattern that occurs solely in the directory segment groups nothing.</summary>
    [Fact]
    public void Scan_RegexMode_PatternMatchingOnlyADirectorySegment_ProducesNoGroup()
    {
        SetupDirectory(@"C:\Photos", new[]
        {
            ("sunset-over-bay.jpg", 3407872L, "sunset jpeg payload"),
            ("sunrise-over-bay.jpg", 3211264L, "sunrise jpeg payload"),
        });

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"C:\Photos" },
            MatchRegex = "Photos",
        };
        var result = _service.Scan(options);

        result.TotalFilesScanned.Should().Be(2);
        result.DuplicateGroups.Should().BeEmpty();
    }

    /// <summary>serves-spec: SPEC-001 rule 10 — regex mode carries its own OrderBy, so files inside a regex-keyed group are ordered by FilePath ascending.</summary>
    [Fact]
    public void Scan_RegexMode_FilesWithinGroup_SortedByPathAscending()
    {
        SetupDirectory(@"D:\Finance\Invoices", new[]
        {
            ("zeta-vendor-INV-2026-0042.pdf", 88064L, "zeta invoice body"),
            ("alpha-vendor-INV-2026-0042.pdf", 91136L, "alpha invoice body"),
        });

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"D:\Finance\Invoices" },
            MatchRegex = @"(INV-\d{4}-\d{4})",
        };
        var result = _service.Scan(options);

        var group = result.DuplicateGroups.Should().ContainSingle().Subject;
        group.Files.Select(f => f.FileName).Should().ContainInOrder(
            "alpha-vendor-INV-2026-0042.pdf",
            "zeta-vendor-INV-2026-0042.pdf");
    }

    /// <summary>serves-spec: SPEC-001 rule 10 — three regex keys where only two hold two or more members produce two groups; the bucket of one is dropped.</summary>
    [Fact]
    public void Scan_RegexMode_ThreeKeysOnlyTwoWithPairs_ProducesTwoGroups()
    {
        SetupDirectory(@"D:\Finance\Invoices", new[]
        {
            ("acme-INV-2026-0042.pdf", 500000L, "acme invoice body"),
            ("acme-INV-2026-0042-copy.pdf", 400000L, "acme invoice rescan body"),
            ("globex-INV-2026-0117.pdf", 300000L, "globex invoice body"),
            ("globex-INV-2026-0117-scan.pdf", 100000L, "globex invoice rescan body"),
            ("initech-INV-2026-0203.pdf", 250000L, "initech invoice body"),
        });

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"D:\Finance\Invoices" },
            MatchRegex = @"(INV-\d{4}-\d{4})",
        };
        var result = _service.Scan(options);

        result.DuplicateGroups.Should().HaveCount(2);
        result.DuplicateGroups[0].Hash.Should().Be("INV-2026-0042");
        result.DuplicateGroups[0].FileSize.Should().Be(500000L);
        result.DuplicateGroups[0].WastedBytes.Should().Be(400000L);
        result.DuplicateGroups[1].Hash.Should().Be("INV-2026-0117");
        result.DuplicateGroups[1].WastedBytes.Should().Be(100000L);
    }

    /// <summary>serves-spec: SPEC-001 rule 11 — the timeout ArgumentException names the file whose name blew the 1-second per-match budget.</summary>
    [Fact]
    public void Scan_RegexMode_TimeoutMessage_NamesTheOffendingFileName()
    {
        // A 60-character run with no terminating 'X' is the classic catastrophic-backtracking
        // trigger for a nested-quantifier pattern; this name is deliberately pathological.
        var pathologicalName = new string('a', 60) + ".log";
        SetupDirectory(@"D:\Logs", new[]
        {
            (pathologicalName, 12582912L, "log payload"),
            ("server-2026-01-14.log", 8388608L, "other log payload"),
        });

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"D:\Logs" },
            MatchRegex = "(a+)+X",
        };

        var act = () => _service.Scan(options);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*timed out*")
            .WithMessage($"*'{pathologicalName}'*");
    }

    /// <summary>serves-spec: SPEC-001 rule 13 — TotalDuplicates and TotalWastedBytes are sums across every group, not just the first.</summary>
    [Fact]
    public void Scan_TwoDuplicateGroups_TotalDuplicatesAndTotalWastedBytesAreSums()
    {
        SetupDirectory(@"E:\Backups\2026-01", new[]
        {
            ("vm-image-a.vhdx", 10485760L, "disk image alpha"),
            ("vm-image-b.vhdx", 10485760L, "disk image alpha"),
            ("config-1.xml", 524288L, "config payload"),
            ("config-2.xml", 524288L, "config payload"),
            ("config-3.xml", 524288L, "config payload"),
        });

        var options = new ScanOptions { TargetPaths = new List<string> { @"E:\Backups\2026-01" } };
        var result = _service.Scan(options);

        result.DuplicateGroups.Should().HaveCount(2);
        result.TotalDuplicates.Should().Be(5);
        result.TotalWastedBytes.Should().Be(11534336L);
    }

    /// <summary>serves-spec: SPEC-001 invariant "the engine never writes" — no file is created, moved or deleted by a scan, in either grouping mode.</summary>
    /// <param name="matchRegex">The name-pattern regex to group by; empty selects content-hash grouping, so the two cases cover both grouping modes.</param>
    [Theory]
    [InlineData("")]
    [InlineData(@"(IMG_\d{4})")]
    public void Scan_AnyMode_NeverCallsWriteAllTextOrCreateDirectory(string matchRegex)
    {
        SetupDirectory(@"D:\Media\Photos\2024", new[]
        {
            ("IMG_4821.jpg", 2458112L, "JFIF camera raw payload for IMG_4821"),
            ("IMG_4822.jpg", 2458112L, "JFIF camera raw payload for IMG_4821"),
        });

        var options = new ScanOptions
        {
            TargetPaths = new List<string> { @"D:\Media\Photos\2024" },
            MatchRegex = matchRegex,
        };
        var result = _service.Scan(options);

        result.TotalFilesScanned.Should().Be(2);
        _mockFileSystem.Verify(fs => fs.WriteAllText(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockFileSystem.Verify(fs => fs.CreateDirectory(It.IsAny<string>()), Times.Never);
    }

    private void SetupDirectory(string path, (string name, long size, string content)[] files)
    {
        _mockFileSystem.Setup(fs => fs.DirectoryExists(path)).Returns(true);

        var filePaths = files.Select(f => Path.Combine(path, f.name)).ToArray();
        _mockFileSystem.Setup(fs => fs.EnumerateFiles(path, "*.*", It.IsAny<SearchOption>()))
            .Returns(filePaths);

        foreach (var (name, size, content) in files)
        {
            var fullPath = Path.Combine(path, name);
            _mockFileSystem.Setup(fs => fs.GetFileSize(fullPath)).Returns(size);
            _mockFileSystem.Setup(fs => fs.GetFileName(fullPath)).Returns(Path.GetFileName(name));
            _mockFileSystem.Setup(fs => fs.GetLastWriteTime(fullPath)).Returns(DateTime.Now);
            _mockFileSystem.Setup(fs => fs.OpenRead(fullPath))
                .Returns(() => new MemoryStream(Encoding.UTF8.GetBytes(content)));
        }
    }
}
