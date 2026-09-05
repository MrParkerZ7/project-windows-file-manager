using System.Text;
using FluentAssertions;
using Moq;
using WindowsFileManager.Application.Services;
using WindowsFileManager.Core.Services;

namespace WindowsFileManager.Tests.Services;

public class FileHashServiceTests
{
    private readonly Mock<IFileSystemService> _mockFileSystem;
    private readonly FileHashService _service;

    public FileHashServiceTests()
    {
        _mockFileSystem = new Mock<IFileSystemService>();
        _service = new FileHashService(_mockFileSystem.Object);
    }

    [Fact]
    public void ComputeHash_ShouldReturnConsistentHash()
    {
        var content = Encoding.UTF8.GetBytes("hello world");
        _mockFileSystem.Setup(fs => fs.OpenRead("file.txt"))
            .Returns(new MemoryStream(content));

        var hash1 = _service.ComputeHash("file.txt");

        _mockFileSystem.Setup(fs => fs.OpenRead("file.txt"))
            .Returns(new MemoryStream(content));

        var hash2 = _service.ComputeHash("file.txt");

        hash1.Should().NotBeEmpty();
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeHash_DifferentContent_ShouldReturnDifferentHash()
    {
        _mockFileSystem.Setup(fs => fs.OpenRead("a.txt"))
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes("content A")));

        var hashA = _service.ComputeHash("a.txt");

        _mockFileSystem.Setup(fs => fs.OpenRead("b.txt"))
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes("content B")));

        var hashB = _service.ComputeHash("b.txt");

        hashA.Should().NotBe(hashB);
    }

    [Fact]
    public void ComputeHash_EmptyFile_ShouldReturnHash()
    {
        _mockFileSystem.Setup(fs => fs.OpenRead("empty.txt"))
            .Returns(new MemoryStream(Array.Empty<byte>()));

        var hash = _service.ComputeHash("empty.txt");

        hash.Should().NotBeEmpty();
    }

    /// <summary>serves-spec: SPEC-001 invariant "the hash is SHA-256 over the entire stream, uppercase hex; never truncated, never sampled" — pinned against a literal digest.</summary>
    [Fact]
    public void ComputeHash_KnownContent_ReturnsUppercase64CharSha256Hex()
    {
        const string Content = "invoice,2026-01-14,4820.50,THB";
        const string ExpectedSha256 = "B1951448CDC3ADA42400359D9EC2659CD887FC347EB3F4A429395BBCDC602DA7";

        _mockFileSystem.Setup(fs => fs.OpenRead(@"D:\Finance\Invoices\acme-INV-2026-0042.csv"))
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes(Content)));

        var hash = _service.ComputeHash(@"D:\Finance\Invoices\acme-INV-2026-0042.csv");

        hash.Should().Be(ExpectedSha256);
        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9A-F]{64}$");
    }
}
