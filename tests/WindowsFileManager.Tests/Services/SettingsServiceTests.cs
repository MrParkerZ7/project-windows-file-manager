using System.Text.Json;
using FluentAssertions;
using Moq;
using WindowsFileManager.Application.Services;
using WindowsFileManager.Core.Models;
using WindowsFileManager.Core.Services;

namespace WindowsFileManager.Tests.Services;

public class SettingsServiceTests
{
    private readonly Mock<IFileSystemService> _mockFileSystem;
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        _mockFileSystem = new Mock<IFileSystemService>();
        _service = new SettingsService(_mockFileSystem.Object, @"C:\app\settings.json");
    }

    [Fact]
    public void Load_FileNotExists_ShouldReturnDefaultsWithDefaultProfile()
    {
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(false);

        var settings = _service.Load();

        settings.Profiles.Should().ContainSingle();
        settings.Profiles[0].Name.Should().Be("Default");
        settings.ActiveProfileName.Should().Be("Default");
    }

    [Fact]
    public void Load_JsonDeserializesToNull_ShouldReturnDefaults()
    {
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns("null");

        var settings = _service.Load();

        settings.Profiles.Should().ContainSingle();
        settings.Profiles[0].Name.Should().Be("Default");
    }

    [Fact]
    public void Load_InvalidJson_ShouldReturnDefaults()
    {
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns("not json{{{");

        var settings = _service.Load();

        settings.Profiles.Should().ContainSingle();
        settings.ActiveProfileName.Should().Be("Default");
    }

    [Fact]
    public void Load_ValidNewFormat_ShouldDeserializeProfiles()
    {
        var json = """
            {
              "Profiles": [
                { "Name": "Work", "TargetPaths": ["C:\\projects"], "IncludeSubdirectories": false },
                { "Name": "Photos", "TargetPaths": ["D:\\photos"] }
              ],
              "ActiveProfileName": "Photos"
            }
            """;
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(json);

        var settings = _service.Load();

        settings.Profiles.Should().HaveCount(2);
        settings.Profiles[0].Name.Should().Be("Work");
        settings.Profiles[0].TargetPaths.Should().ContainSingle().Which.Should().Be(@"C:\projects");
        settings.Profiles[0].IncludeSubdirectories.Should().BeFalse();
        settings.Profiles[1].Name.Should().Be("Photos");
        settings.ActiveProfileName.Should().Be("Photos");
    }

    [Fact]
    public void Load_MissingActiveProfile_ShouldFallBackToFirst()
    {
        var json = """
            {
              "Profiles": [
                { "Name": "A" },
                { "Name": "B" }
              ],
              "ActiveProfileName": "NoSuchProfile"
            }
            """;
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(json);

        var settings = _service.Load();

        settings.ActiveProfileName.Should().Be("A");
    }

    [Fact]
    public void Load_EmptyActiveProfileName_ShouldFallBackToFirst()
    {
        var json = """
            {
              "Profiles": [
                { "Name": "Alpha" }
              ],
              "ActiveProfileName": ""
            }
            """;
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(json);

        var settings = _service.Load();

        settings.ActiveProfileName.Should().Be("Alpha");
    }

    [Fact]
    public void Load_LegacyFlatJson_ShouldMigrateIntoDefaultProfile()
    {
        var json = """
            {
              "TargetPaths": ["C:\\folder1", "C:\\folder2"],
              "DisabledTargetPaths": ["C:\\folder2"],
              "ExcludeFolderNames": ["node_modules", ".git"],
              "DisabledExcludeFolderNames": [".git"],
              "FolderSearchResultPaths": ["C:\\folder1\\src"],
              "SelectedFolderSearchResultPaths": ["C:\\folder1\\src"],
              "IncludeSubdirectories": false,
              "IsMiniPreview": false,
              "IsAutoPreview": false,
              "IsAutoPlay": true,
              "MinimumFileSize": 2048,
              "Volume": 0.25,
              "SelectedSortOption": "Name (A-Z)",
              "MoveTargetPath": "D:\\sorted",
              "FilterRules": [
                { "Pattern": "*.jpg", "Action": 0, "Target": 0, "IsRegex": false, "IgnoreCase": true, "IsEnabled": true }
              ],
              "FolderSearchPatterns": [
                { "Pattern": "src", "MatchType": 0, "IsEnabled": true }
              ]
            }
            """;
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(json);

        var settings = _service.Load();

        settings.Profiles.Should().ContainSingle();
        var profile = settings.Profiles[0];
        profile.Name.Should().Be("Default");
        profile.TargetPaths.Should().BeEquivalentTo(new[] { @"C:\folder1", @"C:\folder2" });
        profile.DisabledTargetPaths.Should().ContainSingle().Which.Should().Be(@"C:\folder2");
        profile.ExcludeFolderNames.Should().BeEquivalentTo(new[] { "node_modules", ".git" });
        profile.DisabledExcludeFolderNames.Should().ContainSingle().Which.Should().Be(".git");
        profile.FolderSearchResultPaths.Should().ContainSingle();
        profile.SelectedFolderSearchResultPaths.Should().ContainSingle();
        profile.IncludeSubdirectories.Should().BeFalse();
        profile.IsMiniPreview.Should().BeFalse();
        profile.IsAutoPreview.Should().BeFalse();
        profile.IsAutoPlay.Should().BeTrue();
        profile.MinimumFileSize.Should().Be(2048);
        profile.Volume.Should().Be(0.25);
        profile.SelectedSortOption.Should().Be("Name (A-Z)");
        profile.MoveTargetPath.Should().Be(@"D:\sorted");
        profile.FilterRules.Should().ContainSingle().Which.Pattern.Should().Be("*.jpg");
        profile.FolderSearchPatterns.Should().ContainSingle().Which.Pattern.Should().Be("src");
        settings.ActiveProfileName.Should().Be("Default");
    }

    [Fact]
    public void Load_LegacyMalformedNestedFilter_ShouldStillMigrateOtherFields()
    {
        // FilterRules contains one valid and one broken entry — broken should skip, valid should appear.
        var json = """
            {
              "TargetPaths": ["C:\\ok"],
              "FilterRules": [
                { "Pattern": "*.ok", "Action": 0, "Target": 0, "IsRegex": false, "IgnoreCase": true, "IsEnabled": true }
              ]
            }
            """;
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(json);

        var settings = _service.Load();

        settings.Profiles.Should().ContainSingle();
        settings.Profiles[0].TargetPaths.Should().ContainSingle().Which.Should().Be(@"C:\ok");
        settings.Profiles[0].FilterRules.Should().ContainSingle();
    }

    [Fact]
    public void Load_LegacyNonArrayFields_ShouldIgnoreThem()
    {
        var json = """
            {
              "TargetPaths": "not-an-array",
              "FilterRules": "also-not-an-array",
              "IncludeSubdirectories": "not-a-bool"
            }
            """;
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(json);

        var settings = _service.Load();

        var profile = settings.Profiles.Should().ContainSingle().Subject;
        profile.TargetPaths.Should().BeEmpty();
        profile.FilterRules.Should().BeEmpty();
        profile.IncludeSubdirectories.Should().BeTrue();
    }

    [Fact]
    public void Load_LegacyFilterRulesWithTypeMismatch_ShouldSwallowJsonException()
    {
        // Action expects a number; passing an object triggers JsonException deep in nested deserialization.
        // The migration code must catch it and still return a (partially populated) profile.
        var json = """
            {
              "TargetPaths": ["C:\\still-here"],
              "FilterRules": [
                { "Pattern": "oops", "Action": { "nested": "object" }, "Target": 0, "IsRegex": false, "IgnoreCase": true, "IsEnabled": true }
              ]
            }
            """;
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(json);

        var settings = _service.Load();

        settings.Profiles.Should().ContainSingle();
        settings.Profiles[0].Name.Should().Be("Default");
    }

    [Fact]
    public void Load_LegacyArrayAtRoot_ShouldYieldEmptyDefaultProfile()
    {
        // JSON is a valid JSON array, not an object — migration must cope by falling back to defaults.
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns("[]");

        var settings = _service.Load();

        var profile = settings.Profiles.Should().ContainSingle().Subject;
        profile.Name.Should().Be("Default");
        profile.TargetPaths.Should().BeEmpty();
    }

    [Fact]
    public void Load_LegacyStringListWithNullEntry_ShouldSkipNull()
    {
        // When a string list contains a null element, migration should skip it without throwing.
        var json = """
            {
              "TargetPaths": ["C:\\real", null, "C:\\other"]
            }
            """;
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(json);

        var settings = _service.Load();

        settings.Profiles[0].TargetPaths.Should().BeEquivalentTo(new[] { @"C:\real", @"C:\other" });
    }

    [Fact]
    public void Load_LegacyObjectListWithNullEntry_ShouldSkipNull()
    {
        var json = """
            {
              "FilterRules": [
                { "Pattern": "ok", "Action": 0, "Target": 0, "IsRegex": false, "IgnoreCase": true, "IsEnabled": true },
                null
              ]
            }
            """;
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(json);

        var settings = _service.Load();

        settings.Profiles[0].FilterRules.Should().ContainSingle();
    }

    [Fact]
    public void Load_LegacyBoolean_ShouldRespectFalseExplicitly()
    {
        var json = """
            {
              "IsAutoPreview": false,
              "IsMiniPreview": true
            }
            """;
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(json);

        var settings = _service.Load();

        settings.Profiles[0].IsAutoPreview.Should().BeFalse();
        settings.Profiles[0].IsMiniPreview.Should().BeTrue();
    }

    [Fact]
    public void Load_LegacyNumericOverflow_ShouldFallBack()
    {
        // MinimumFileSize > Int64 max — TryGetInt64 fails, falls back to default.
        var json = """
            {
              "MinimumFileSize": 999999999999999999999999999,
              "Volume": "not-a-number"
            }
            """;
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(json);

        var settings = _service.Load();

        settings.Profiles[0].MinimumFileSize.Should().Be(1);
        settings.Profiles[0].Volume.Should().Be(0.5);
    }

    [Fact]
    public void Save_ShouldWriteJsonToFile()
    {
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"C:\app")).Returns(true);

        var settings = new AppSettings
        {
            Profiles = new List<ProfileSettings>
            {
                new() { Name = "Default", TargetPaths = new List<string> { @"C:\test" } },
            },
            ActiveProfileName = "Default",
        };

        _service.Save(settings);

        _mockFileSystem.Verify(
            fs => fs.WriteAllText(
                @"C:\app\settings.json",
                It.Is<string>(s => s.Contains("C:\\\\test") && s.Contains("Default"))),
            Times.Once);
    }

    [Fact]
    public void Save_DirectoryNotExists_ShouldCreateIt()
    {
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"C:\app")).Returns(false);

        _service.Save(new AppSettings());

        _mockFileSystem.Verify(fs => fs.CreateDirectory(@"C:\app"), Times.Once);
    }

    [Fact]
    public void Save_BareFilename_ShouldNotAttemptCreateDirectory()
    {
        // When the settings path has no directory component (just a filename),
        // GetDirectoryName returns string.Empty — CreateDirectory must not be called.
        var bareService = new SettingsService(_mockFileSystem.Object, "settings.json");

        bareService.Save(new AppSettings());

        _mockFileSystem.Verify(fs => fs.DirectoryExists(It.IsAny<string>()), Times.Never);
        _mockFileSystem.Verify(fs => fs.CreateDirectory(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Save_DirectoryExists_ShouldNotCreateIt()
    {
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"C:\app")).Returns(true);

        _service.Save(new AppSettings());

        _mockFileSystem.Verify(fs => fs.CreateDirectory(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void SaveAndLoad_MultiProfile_ShouldRoundTrip()
    {
        string? savedJson = null;
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"C:\app")).Returns(true);
        _mockFileSystem.Setup(fs => fs.WriteAllText(@"C:\app\settings.json", It.IsAny<string>()))
            .Callback<string, string>((_, json) => savedJson = json);

        var settings = new AppSettings
        {
            Profiles = new List<ProfileSettings>
            {
                new()
                {
                    Name = "Work",
                    TargetPaths = new List<string> { @"C:\projects" },
                    IncludeSubdirectories = false,
                    FilterRules = new List<FilterRule>
                    {
                        new() { Pattern = "*.jpg", Action = FilterAction.Include, Target = FilterTarget.Filename, IsRegex = false, IgnoreCase = true, IsEnabled = true },
                    },
                },
                new() { Name = "Photos", TargetPaths = new List<string> { @"D:\photos" } },
            },
            ActiveProfileName = "Photos",
            WindowLeft = 100.5,
            WindowTop = 200.0,
            WindowWidth = 1400.0,
            WindowHeight = 900.0,
            IsMaximized = true,
        };

        _service.Save(settings);

        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(savedJson!);

        var loaded = _service.Load();

        loaded.Profiles.Should().HaveCount(2);
        loaded.ActiveProfileName.Should().Be("Photos");
        loaded.Profiles[0].Name.Should().Be("Work");
        loaded.Profiles[0].IncludeSubdirectories.Should().BeFalse();
        loaded.Profiles[0].FilterRules.Should().ContainSingle().Which.Pattern.Should().Be("*.jpg");
        loaded.Profiles[1].Name.Should().Be("Photos");
        loaded.WindowLeft.Should().Be(100.5);
        loaded.WindowTop.Should().Be(200.0);
        loaded.WindowWidth.Should().Be(1400.0);
        loaded.WindowHeight.Should().Be(900.0);
        loaded.IsMaximized.Should().BeTrue();
    }

    /// <summary>serves-spec: SPEC-009 rule 1 — the whole AppSettings is serialized with WriteIndented = true, so the file stays human-readable.</summary>
    [Fact]
    public void Save_ValidSettings_ShouldSerializeWithWriteIndentedTrue()
    {
        string? savedJson = null;
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"C:\app")).Returns(true);
        _mockFileSystem.Setup(fs => fs.WriteAllText(@"C:\app\settings.json", It.IsAny<string>()))
            .Callback<string, string>((_, json) => savedJson = json);

        _service.Save(new AppSettings
        {
            Profiles = new List<ProfileSettings>
            {
                new() { Name = "Photos", TargetPaths = new List<string> { @"D:\Media\Photos" } },
            },
            ActiveProfileName = "Photos",
        });

        savedJson.Should().NotBeNull();
        savedJson.Should().Contain("\n  \"Profiles\": [");
        savedJson.Should().Contain("\n      \"Name\": \"Photos\"");
        savedJson.Should().NotContain("{\"Profiles\":");
    }

    /// <summary>serves-spec: SPEC-009 rule 1 — every save rewrites the entire file; there is no partial write and no append.</summary>
    [Fact]
    public void Save_CalledRepeatedly_ShouldRewriteTheWholeFileEachTime()
    {
        var payloads = new List<string>();
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"C:\app")).Returns(true);
        _mockFileSystem.Setup(fs => fs.WriteAllText(@"C:\app\settings.json", It.IsAny<string>()))
            .Callback<string, string>((_, json) => payloads.Add(json));

        var settings = new AppSettings
        {
            Profiles = new List<ProfileSettings>
            {
                new() { Name = "Downloads", TargetPaths = new List<string> { @"C:\Users\pkrit\Downloads" } },
            },
            ActiveProfileName = "Downloads",
        };

        _service.Save(settings);
        settings.Profiles[0].TargetPaths.Add(@"E:\Archive\Downloads");
        _service.Save(settings);

        _mockFileSystem.Verify(
            fs => fs.WriteAllText(@"C:\app\settings.json", It.IsAny<string>()),
            Times.Exactly(2));
        payloads.Should().HaveCount(2);
        payloads.Should().AllSatisfy(payload =>
        {
            payload.Should().StartWith("{");
            payload.Should().EndWith("}");
            payload.Should().Contain(@"C:\\Users\\pkrit\\Downloads");
        });
        payloads[0].Should().NotContain(@"E:\\Archive\\Downloads");
        payloads[1].Should().Contain(@"E:\\Archive\\Downloads");
    }

    /// <summary>serves-spec: SPEC-009 rule 3 — the try block wraps ReadAllText as well as Deserialize, so a JsonException raised while reading also falls back to defaults.</summary>
    [Fact]
    public void Load_ReadAllTextThrowsJsonException_ShouldReturnDefaults()
    {
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json"))
            .Throws(new JsonException("Unexpected end of stream while reading settings.json."));

        var settings = _service.Load();

        settings.Profiles.Should().ContainSingle();
        settings.Profiles[0].Name.Should().Be("Default");
        settings.ActiveProfileName.Should().Be("Default");
    }

    /// <summary>serves-spec: SPEC-009 rule 3 — an empty settings file makes Deserialize throw JsonException, which routes to CreateDefault().</summary>
    [Fact]
    public void Load_EmptyFileContent_ShouldReturnDefaults()
    {
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(string.Empty);

        var settings = _service.Load();

        settings.Profiles.Should().ContainSingle();
        settings.Profiles[0].Name.Should().Be("Default");
        settings.ActiveProfileName.Should().Be("Default");
    }

    /// <summary>serves-spec: SPEC-009 rule 4 — migration is triggered by Profiles.Count == 0, which an explicit empty "Profiles": [] array satisfies exactly as an absent key does.</summary>
    [Fact]
    public void Load_ExplicitEmptyProfilesArrayWithLegacyFields_ShouldTriggerMigration()
    {
        var json = """
            {
              "Profiles": [],
              "TargetPaths": ["D:\\Media\\Photos", "E:\\Backup\\Photos"],
              "ExcludeFolderNames": [".thumbnails"],
              "Volume": 0.8,
              "SelectedSortOption": "Wasted space (most)"
            }
            """;
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(json);

        var settings = _service.Load();

        var profile = settings.Profiles.Should().ContainSingle().Subject;
        profile.Name.Should().Be("Default");
        profile.TargetPaths.Should().BeEquivalentTo(new[] { @"D:\Media\Photos", @"E:\Backup\Photos" });
        profile.ExcludeFolderNames.Should().ContainSingle().Which.Should().Be(".thumbnails");
        profile.Volume.Should().Be(0.8);
        profile.SelectedSortOption.Should().Be("Wasted space (most)");
        settings.ActiveProfileName.Should().Be("Default");
    }

    /// <summary>serves-spec: SPEC-009 rule 4 — migration fires only when Profiles.Count == 0, so stray top-level legacy keys beside a populated Profiles array are ignored, not folded into a Default profile.</summary>
    [Fact]
    public void Load_ProfilesAlreadyPresent_ShouldNotRunLegacyMigration()
    {
        var json = """
            {
              "Profiles": [
                { "Name": "Photos", "TargetPaths": ["D:\\Media\\Photos"] }
              ],
              "ActiveProfileName": "Photos",
              "TargetPaths": ["C:\\Users\\pkrit\\Downloads"],
              "MoveTargetPath": "E:\\Quarantine",
              "Volume": 0.9
            }
            """;
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(json);

        var settings = _service.Load();

        var profile = settings.Profiles.Should().ContainSingle().Subject;
        profile.Name.Should().Be("Photos");
        profile.TargetPaths.Should().ContainSingle().Which.Should().Be(@"D:\Media\Photos");
        profile.MoveTargetPath.Should().BeEmpty();
        profile.Volume.Should().Be(0.5);
        settings.ActiveProfileName.Should().Be("Photos");
    }

    /// <summary>serves-spec: SPEC-009 rule 4 — every legacy reader falls back to the property's current default when the token is absent, so a legacy file with no recognised field yields an untouched Default profile.</summary>
    [Fact]
    public void Load_LegacyJsonWithNoRecognizedFields_ShouldKeepEveryProfileDefault()
    {
        var json = """
            {
              "SchemaVersion": 3,
              "LastOpenedTab": "Folder",
              "RecentSearches": ["invoice", "2019"]
            }
            """;
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(json);

        var settings = _service.Load();

        var profile = settings.Profiles.Should().ContainSingle().Subject;
        profile.Name.Should().Be("Default");
        profile.TargetPaths.Should().BeEmpty();
        profile.IncludeSubdirectories.Should().BeTrue();
        profile.MinimumFileSize.Should().Be(1);
        profile.IsMiniPreview.Should().BeTrue();
        profile.IsAutoPreview.Should().BeTrue();
        profile.IsAutoPlay.Should().BeFalse();
        profile.SelectedSortOption.Should().Be("Size (largest)");
        profile.Volume.Should().Be(0.5);
        profile.MoveTargetPath.Should().BeEmpty();
        profile.ExcludeFolderNames.Should().BeEmpty();
        profile.DisabledTargetPaths.Should().BeEmpty();
        profile.DisabledExcludeFolderNames.Should().BeEmpty();
        profile.FilterRules.Should().BeEmpty();
        profile.FolderSearchPatterns.Should().BeEmpty();
        profile.FolderSearchMaxDepth.Should().BeNull();
        profile.FolderSearchResultPaths.Should().BeEmpty();
        profile.SelectedFolderSearchResultPaths.Should().BeEmpty();
        profile.LinkSiblingsLayer.Should().Be(1);
        profile.LinkSiblingsPrefix.Should().BeEmpty();
        profile.DuplicateMatchByRegex.Should().BeFalse();
        profile.DuplicateMatchRegex.Should().BeEmpty();
    }

    /// <summary>serves-spec: SPEC-009 rule 4 — ReadString keeps the property's default when the legacy token is present but of the wrong ValueKind.</summary>
    [Fact]
    public void Load_LegacyStringFieldOfWrongKind_ShouldKeepDefault()
    {
        var json = """
            {
              "TargetPaths": ["D:\\Media\\Photos"],
              "SelectedSortOption": 42,
              "MoveTargetPath": ["E:\\Quarantine"]
            }
            """;
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(json);

        var settings = _service.Load();

        var profile = settings.Profiles.Should().ContainSingle().Subject;
        profile.SelectedSortOption.Should().Be("Size (largest)");
        profile.MoveTargetPath.Should().BeEmpty();
        profile.TargetPaths.Should().ContainSingle().Which.Should().Be(@"D:\Media\Photos");
    }

    /// <summary>serves-spec: SPEC-009 rule 4 — legacy string lists skip non-string elements as well as nulls.</summary>
    [Fact]
    public void Load_LegacyStringListWithNonStringElement_ShouldSkipIt()
    {
        var json = """
            {
              "ExcludeFolderNames": ["node_modules", 42, { "Name": "bin" }, [".git"], true, ".vs"]
            }
            """;
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(json);

        var settings = _service.Load();

        settings.Profiles[0].ExcludeFolderNames.Should().BeEquivalentTo(new[] { "node_modules", ".vs" });
    }

    /// <summary>serves-spec: SPEC-009 rule 4 — a JsonException raised inside migration is swallowed and whatever was mapped before the throw survives; ReadObjectList has no per-element tolerance, so every field read after the throw is abandoned.</summary>
    [Fact]
    public void Load_LegacyJsonExceptionMidMigration_ShouldKeepFieldsMappedBeforeTheThrow()
    {
        var json = """
            {
              "TargetPaths": ["D:\\Media\\Photos"],
              "SelectedSortOption": "Name (A-Z)",
              "FilterRules": [
                { "Pattern": "IMG_*.jpg", "Action": { "unexpected": "object" }, "Target": 0, "IsRegex": false, "IgnoreCase": true, "IsEnabled": true }
              ],
              "FolderSearchPatterns": [
                { "Pattern": "RAW", "MatchType": 1, "IsEnabled": true }
              ]
            }
            """;
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(json);

        var settings = _service.Load();

        var profile = settings.Profiles.Should().ContainSingle().Subject;
        profile.TargetPaths.Should().ContainSingle().Which.Should().Be(@"D:\Media\Photos");
        profile.SelectedSortOption.Should().Be("Name (A-Z)");
        profile.FilterRules.Should().BeEmpty();
        profile.FolderSearchPatterns.Should().BeEmpty();
    }

    /// <summary>serves-spec: SPEC-009 rule 5 — an absent ActiveProfileName leaves the C# initializer "Default", which names no profile here and is therefore repaired to Profiles[0].</summary>
    [Fact]
    public void Load_ActiveProfileNameAbsentFromJson_ShouldFallBackToFirst()
    {
        var json = """
            {
              "Profiles": [
                { "Name": "Photos" },
                { "Name": "Downloads" }
              ]
            }
            """;
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(json);

        var settings = _service.Load();

        settings.ActiveProfileName.Should().Be("Photos");
    }

    /// <summary>serves-spec: SPEC-009 rule 5 — the existence check is OrdinalIgnoreCase, so a name differing only by case resolves and is left exactly as written.</summary>
    [Fact]
    public void Load_ActiveProfileNameDiffersOnlyByCase_ShouldBeKeptAsIs()
    {
        var json = """
            {
              "Profiles": [
                { "Name": "Photos" },
                { "Name": "Downloads" }
              ],
              "ActiveProfileName": "photos"
            }
            """;
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(json);

        var settings = _service.Load();

        settings.ActiveProfileName.Should().Be("photos");
    }

    /// <summary>serves-spec: SPEC-009 rule 16 — the action history is global state that survives a save/load round trip with its kind and paths intact.</summary>
    [Fact]
    public void SaveAndLoad_WithActionHistory_ShouldRoundTripEntriesKindAndPaths()
    {
        string? savedJson = null;
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"C:\app")).Returns(true);
        _mockFileSystem.Setup(fs => fs.WriteAllText(@"C:\app\settings.json", It.IsAny<string>()))
            .Callback<string, string>((_, json) => savedJson = json);

        var settings = new AppSettings
        {
            Profiles = new List<ProfileSettings> { new() { Name = "Photos" } },
            ActiveProfileName = "Photos",
            ActionHistory = new List<ActionHistoryEntry>
            {
                new()
                {
                    Kind = ActionHistoryKind.RecycleFiles,
                    RecycledPaths = new List<string> { @"D:\Media\Photos\IMG_4821 (1).jpg", @"D:\Media\Photos\IMG_4822 (1).jpg" },
                    Summary = "Recycled 2 .jpg files",
                    Timestamp = new DateTime(2026, 3, 14, 9, 30, 0),
                },
                new()
                {
                    Kind = ActionHistoryKind.MoveFiles,
                    Moves = new List<ActionHistoryMove>
                    {
                        new() { Source = @"D:\Media\Photos\DSC_0031.nef", Destination = @"E:\Quarantine\DSC_0031.nef" },
                    },
                    Summary = "Moved 1 file",
                    Timestamp = new DateTime(2026, 3, 14, 9, 28, 0),
                },
            },
        };

        _service.Save(settings);

        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(savedJson!);

        var loaded = _service.Load();

        loaded.ActionHistory.Should().HaveCount(2);
        loaded.ActionHistory[0].Kind.Should().Be(ActionHistoryKind.RecycleFiles);
        loaded.ActionHistory[0].RecycledPaths.Should().BeEquivalentTo(new[] { @"D:\Media\Photos\IMG_4821 (1).jpg", @"D:\Media\Photos\IMG_4822 (1).jpg" });
        loaded.ActionHistory[0].Summary.Should().Be("Recycled 2 .jpg files");
        loaded.ActionHistory[0].Timestamp.Should().Be(new DateTime(2026, 3, 14, 9, 30, 0));
        loaded.ActionHistory[0].ItemCount.Should().Be(2);
        loaded.ActionHistory[1].Kind.Should().Be(ActionHistoryKind.MoveFiles);
        loaded.ActionHistory[1].Moves.Should().ContainSingle();
        loaded.ActionHistory[1].Moves[0].Source.Should().Be(@"D:\Media\Photos\DSC_0031.nef");
        loaded.ActionHistory[1].Moves[0].Destination.Should().Be(@"E:\Quarantine\DSC_0031.nef");
    }

    /// <summary>serves-spec: SPEC-009 invariant 1 — whatever the file holds, Load returns at least one profile and an ActiveProfileName that names one of them.</summary>
    /// <param name="fileContent">The settings-file content to load, or null to model a settings file that does not exist yet.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json{{{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"Profiles\": []}")]
    [InlineData("{\"TargetPaths\": [\"D:\\\\Media\\\\Photos\"], \"Volume\": 0.8}")]
    [InlineData("{\"Profiles\": [{\"Name\": \"Photos\"}], \"ActiveProfileName\": \"NoSuchProfile\"}")]
    [InlineData("{\"Profiles\": [{\"Name\": \"Photos\"}, {\"Name\": \"Downloads\"}], \"ActiveProfileName\": \"Downloads\"}")]
    public void Load_AnyDocumentedInput_ShouldAlwaysYieldAtLeastOneProfileAndResolvableActiveName(string? fileContent)
    {
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(fileContent != null);
        if (fileContent != null)
        {
            _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(fileContent);
        }

        var settings = _service.Load();

        settings.Profiles.Should().NotBeEmpty();
        settings.ActiveProfileName.Should().NotBeNullOrEmpty();
        settings.Profiles.Should().Contain(p => string.Equals(p.Name, settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>serves-spec: SPEC-009 invariant 6 — System.Text.Json ignores unknown properties, so a file written by a newer build still loads.</summary>
    [Fact]
    public void Load_JsonWithUnknownProperty_ShouldIgnoreItAndStillLoad()
    {
        var json = """
            {
              "SchemaVersion": 7,
              "Profiles": [
                { "Name": "Photos", "TargetPaths": ["D:\\Media\\Photos"], "ThemeAccent": "#2D7FF9" }
              ],
              "ActiveProfileName": "Photos",
              "LastBackupUtc": "2026-03-14T09:30:00Z"
            }
            """;
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(json);

        var settings = _service.Load();

        var profile = settings.Profiles.Should().ContainSingle().Subject;
        profile.Name.Should().Be("Photos");
        profile.TargetPaths.Should().ContainSingle().Which.Should().Be(@"D:\Media\Photos");
        settings.ActiveProfileName.Should().Be("Photos");
    }

    /// <summary>serves-spec: SPEC-009 invariant 6 — absent properties fall back to the C# initializers, so a profile carrying only Name deserializes with the other 21 defaults intact.</summary>
    [Fact]
    public void Load_ProfileJsonMissingMostProperties_ShouldFallBackToCSharpInitializers()
    {
        var json = """
            {
              "Profiles": [
                { "Name": "Archive" }
              ],
              "ActiveProfileName": "Archive"
            }
            """;
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(json);

        var settings = _service.Load();

        var profile = settings.Profiles.Should().ContainSingle().Subject;
        profile.Name.Should().Be("Archive");
        profile.TargetPaths.Should().BeEmpty();
        profile.IncludeSubdirectories.Should().BeTrue();
        profile.MinimumFileSize.Should().Be(1);
        profile.IsMiniPreview.Should().BeTrue();
        profile.IsAutoPreview.Should().BeTrue();
        profile.IsAutoPlay.Should().BeFalse();
        profile.SelectedSortOption.Should().Be("Size (largest)");
        profile.Volume.Should().Be(0.5);
        profile.MoveTargetPath.Should().BeEmpty();
        profile.ExcludeFolderNames.Should().BeEmpty();
        profile.DisabledTargetPaths.Should().BeEmpty();
        profile.DisabledExcludeFolderNames.Should().BeEmpty();
        profile.FilterRules.Should().BeEmpty();
        profile.FolderSearchPatterns.Should().BeEmpty();
        profile.FolderSearchMaxDepth.Should().BeNull();
        profile.FolderSearchResultPaths.Should().BeEmpty();
        profile.SelectedFolderSearchResultPaths.Should().BeEmpty();
        profile.LinkSiblingsLayer.Should().Be(1);
        profile.LinkSiblingsPrefix.Should().BeEmpty();
        profile.DuplicateMatchByRegex.Should().BeFalse();
        profile.DuplicateMatchRegex.Should().BeEmpty();
    }

    /// <summary>serves-spec: SPEC-009 invariant 7 — all settings I/O goes through IFileSystemService (ADR-004); a strict mock fails the test on any call outside that abstraction.</summary>
    [Fact]
    public void SettingsService_LoadAndSave_ShouldUseOnlyIFileSystemService()
    {
        var json = """
            {
              "Profiles": [
                { "Name": "Photos", "TargetPaths": ["D:\\Media\\Photos"] }
              ],
              "ActiveProfileName": "Photos"
            }
            """;
        var strictFileSystem = new Mock<IFileSystemService>(MockBehavior.Strict);
        strictFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        strictFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(json);
        strictFileSystem.Setup(fs => fs.DirectoryExists(@"C:\app")).Returns(true);
        strictFileSystem.Setup(fs => fs.WriteAllText(@"C:\app\settings.json", It.IsAny<string>()));
        var service = new SettingsService(strictFileSystem.Object, @"C:\app\settings.json");

        var loaded = service.Load();
        service.Save(loaded);

        loaded.Profiles.Should().ContainSingle().Which.Name.Should().Be("Photos");
        strictFileSystem.Verify(fs => fs.FileExists(@"C:\app\settings.json"), Times.Once);
        strictFileSystem.Verify(fs => fs.ReadAllText(@"C:\app\settings.json"), Times.Once);
        strictFileSystem.Verify(fs => fs.DirectoryExists(@"C:\app"), Times.Once);
        strictFileSystem.Verify(fs => fs.WriteAllText(@"C:\app\settings.json", It.IsAny<string>()), Times.Once);
        strictFileSystem.VerifyNoOtherCalls();
    }

    /// <summary>serves-spec: SPEC-009 invariant 8 — a full save/load round trip preserves all 22 ProfileSettings fields, not only the handful the multi-profile round trip covers.</summary>
    [Fact]
    public void SaveAndLoad_AllTwentyTwoProfileFields_ShouldRoundTrip()
    {
        string? savedJson = null;
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"C:\app")).Returns(true);
        _mockFileSystem.Setup(fs => fs.WriteAllText(@"C:\app\settings.json", It.IsAny<string>()))
            .Callback<string, string>((_, json) => savedJson = json);

        var settings = new AppSettings
        {
            Profiles = new List<ProfileSettings>
            {
                new()
                {
                    Name = "Photo library",
                    TargetPaths = new List<string> { @"D:\Media\Photos", @"E:\Backup\Photos" },
                    DisabledTargetPaths = new List<string> { @"E:\Backup\Photos" },
                    IncludeSubdirectories = false,
                    MinimumFileSize = 262_144,
                    IsMiniPreview = false,
                    IsAutoPreview = false,
                    IsAutoPlay = true,
                    SelectedSortOption = "Wasted space (most)",
                    Volume = 0.35,
                    MoveTargetPath = @"E:\Quarantine",
                    ExcludeFolderNames = new List<string> { ".thumbnails", "Lightroom Previews.lrdata" },
                    DisabledExcludeFolderNames = new List<string> { "Lightroom Previews.lrdata" },
                    FilterRules = new List<FilterRule>
                    {
                        new() { Pattern = @"IMG_\d{4} \(\d\)\.jpg", Action = FilterAction.Exclude, Target = FilterTarget.Filepath, IsRegex = true, IgnoreCase = false, IsEnabled = false },
                    },
                    FolderSearchPatterns = new List<FolderSearchPattern>
                    {
                        new() { Pattern = "RAW", MatchType = FolderMatchType.Contains, IsEnabled = false },
                    },
                    FolderSearchMaxDepth = 4,
                    FolderSearchResultPaths = new List<string> { @"D:\Media\Photos\2019", @"D:\Media\Photos\2020" },
                    SelectedFolderSearchResultPaths = new List<string> { @"D:\Media\Photos\2020" },
                    LinkSiblingsLayer = 3,
                    LinkSiblingsPrefix = "dup-",
                    DuplicateMatchByRegex = true,
                    DuplicateMatchRegex = @"^(IMG_\d{4})",
                },
            },
            ActiveProfileName = "Photo library",
        };

        _service.Save(settings);

        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(savedJson!);

        var profile = _service.Load().Profiles.Should().ContainSingle().Subject;

        profile.Name.Should().Be("Photo library");
        profile.TargetPaths.Should().BeEquivalentTo(new[] { @"D:\Media\Photos", @"E:\Backup\Photos" });
        profile.DisabledTargetPaths.Should().ContainSingle().Which.Should().Be(@"E:\Backup\Photos");
        profile.IncludeSubdirectories.Should().BeFalse();
        profile.MinimumFileSize.Should().Be(262_144);
        profile.IsMiniPreview.Should().BeFalse();
        profile.IsAutoPreview.Should().BeFalse();
        profile.IsAutoPlay.Should().BeTrue();
        profile.SelectedSortOption.Should().Be("Wasted space (most)");
        profile.Volume.Should().Be(0.35);
        profile.MoveTargetPath.Should().Be(@"E:\Quarantine");
        profile.ExcludeFolderNames.Should().BeEquivalentTo(new[] { ".thumbnails", "Lightroom Previews.lrdata" });
        profile.DisabledExcludeFolderNames.Should().ContainSingle().Which.Should().Be("Lightroom Previews.lrdata");
        var rule = profile.FilterRules.Should().ContainSingle().Subject;
        rule.Pattern.Should().Be(@"IMG_\d{4} \(\d\)\.jpg");
        rule.Action.Should().Be(FilterAction.Exclude);
        rule.Target.Should().Be(FilterTarget.Filepath);
        rule.IsRegex.Should().BeTrue();
        rule.IgnoreCase.Should().BeFalse();
        rule.IsEnabled.Should().BeFalse();
        var pattern = profile.FolderSearchPatterns.Should().ContainSingle().Subject;
        pattern.Pattern.Should().Be("RAW");
        pattern.MatchType.Should().Be(FolderMatchType.Contains);
        pattern.IsEnabled.Should().BeFalse();
        profile.FolderSearchMaxDepth.Should().Be(4);
        profile.FolderSearchResultPaths.Should().BeEquivalentTo(new[] { @"D:\Media\Photos\2019", @"D:\Media\Photos\2020" });
        profile.SelectedFolderSearchResultPaths.Should().ContainSingle().Which.Should().Be(@"D:\Media\Photos\2020");
        profile.LinkSiblingsLayer.Should().Be(3);
        profile.LinkSiblingsPrefix.Should().Be("dup-");
        profile.DuplicateMatchByRegex.Should().BeTrue();
        profile.DuplicateMatchRegex.Should().Be(@"^(IMG_\d{4})");
    }

    /// <summary>serves-spec: SPEC-003 rule 2 — FilterRule.Priority is [JsonIgnore], so rule precedence survives on disk only as the order of the JSON array.</summary>
    [Fact]
    public void SaveAndLoad_TwoFilterRules_PreservesArrayOrder()
    {
        string? savedJson = null;
        _mockFileSystem.Setup(fs => fs.DirectoryExists(@"C:\app")).Returns(true);
        _mockFileSystem.Setup(fs => fs.WriteAllText(@"C:\app\settings.json", It.IsAny<string>()))
            .Callback<string, string>((_, json) => savedJson = json);

        var settings = new AppSettings
        {
            Profiles = new List<ProfileSettings>
            {
                new()
                {
                    Name = "Photos",
                    FilterRules = new List<FilterRule>
                    {
                        new() { Priority = 1, Pattern = ".nef", Action = FilterAction.Exclude, Target = FilterTarget.Filename },
                        new() { Priority = 2, Pattern = "IMG_", Action = FilterAction.Include, Target = FilterTarget.Filename },
                    },
                },
            },
            ActiveProfileName = "Photos",
        };

        _service.Save(settings);

        savedJson.Should().NotContain("\"Priority\"");
        _mockFileSystem.Setup(fs => fs.FileExists(@"C:\app\settings.json")).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllText(@"C:\app\settings.json")).Returns(savedJson!);

        var rules = _service.Load().Profiles[0].FilterRules;

        rules.Should().HaveCount(2);
        rules[0].Pattern.Should().Be(".nef");
        rules[0].Action.Should().Be(FilterAction.Exclude);
        rules[1].Pattern.Should().Be("IMG_");
        rules[1].Action.Should().Be(FilterAction.Include);
        rules.Should().AllSatisfy(rule => rule.Priority.Should().Be(0));
    }
}
