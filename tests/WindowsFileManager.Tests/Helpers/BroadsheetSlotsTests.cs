using System.Diagnostics.CodeAnalysis;
using System.Windows;
using FluentAssertions;
using WindowsFileManager.Helpers;
using Xunit;

namespace WindowsFileManager.Tests.Helpers;

/// <summary>
/// Tests for <see cref="BroadsheetSlots"/>, the typed template slots of decision D1-B on T-003.
/// A bare <see cref="DependencyObject"/> carries attached properties without an STA thread, so these run
/// in the ordinary test pass.
/// </summary>
public class BroadsheetSlotsTests
{
    /// <summary>The helper sits under the coverage Include filter and takes ADR-011's reviewed escape hatch, as the two
    /// attached-property helpers beside it do. Dropping the attribute must be a visible, deliberate change.</summary>
    [Fact]
    public void BroadsheetSlots_ShouldCarryExcludeFromCodeCoverage()
    {
        typeof(BroadsheetSlots).IsDefined(typeof(ExcludeFromCodeCoverageAttribute), inherit: false).Should().BeTrue();
    }

    /// <summary>Unset slots are null — the templates bind that as nothing, so an unused slot lays out as if absent.</summary>
    [Fact]
    public void Slots_DefaultToNull()
    {
        var element = new DependencyObject();

        BroadsheetSlots.GetTrailingContent(element).Should().BeNull();
        BroadsheetSlots.GetPlaceholder(element).Should().BeNull();
    }

    /// <summary>Each slot returns what was set, and can be cleared again.</summary>
    [Fact]
    public void Slots_RoundTrip()
    {
        var element = new DependencyObject();
        var trailing = new object();

        BroadsheetSlots.SetTrailingContent(element, trailing);
        BroadsheetSlots.SetPlaceholder(element, "text or /regex/");

        BroadsheetSlots.GetTrailingContent(element).Should().BeSameAs(trailing);
        BroadsheetSlots.GetPlaceholder(element).Should().Be("text or /regex/");

        BroadsheetSlots.SetTrailingContent(element, null);
        BroadsheetSlots.SetPlaceholder(element, null);

        BroadsheetSlots.GetTrailingContent(element).Should().BeNull();
        BroadsheetSlots.GetPlaceholder(element).Should().BeNull();
    }
}
