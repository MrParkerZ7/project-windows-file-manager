using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using WindowsFileManager.Helpers;

namespace WindowsFileManager.Tests.Helpers;

/// <summary>
/// SPEC-010's help-markup grammar (rules 4 through 14) is deliberately NOT asserted here.
/// Every grammar case has to build a TextBlock and read its Inlines, which needs an STA
/// apartment the xUnit runner does not provide, and FormattedTextBehavior is
/// [ExcludeFromCodeCoverage] so none of it reaches the coverage gate either. The
/// coverage-boundary invariant itself is the one SPEC-010 fact assertable without both.
/// </summary>
public class FormattedTextBehaviorTests
{
    /// <summary>serves-spec: SPEC-010 invariant 6 — FormattedTextBehavior is [ExcludeFromCodeCoverage], so none of the help-markup grammar is exercised by the 100 percent coverage gate (ADR-011); dropping the attribute must be a visible, deliberate change rather than a silent one.</summary>
    [Fact]
    public void FormattedTextBehavior_ShouldCarryExcludeFromCodeCoverage()
    {
        var isExcluded = typeof(FormattedTextBehavior).IsDefined(typeof(ExcludeFromCodeCoverageAttribute), inherit: false);

        isExcluded.Should().BeTrue("SPEC-010 invariant 6 places the help-markup parser outside the 100 percent coverage gate");
    }
}
