using System.Globalization;
using System.Windows;
using FluentAssertions;
using WindowsFileManager.Helpers;

namespace WindowsFileManager.Tests.Helpers;

public class PercentToWidthConverterTests
{
    private readonly PercentToWidthConverter _converter = PercentToWidthConverter.Instance;

    [Fact]
    public void Convert_ValidValues_ShouldReturnPixelWidth()
    {
        var result = _converter.Convert(new object[] { 50.0, 200.0 }, typeof(double), null!, CultureInfo.InvariantCulture);

        result.Should().Be(100.0);
    }

    [Fact]
    public void Convert_ZeroPercent_ShouldReturnZero()
    {
        var result = _converter.Convert(new object[] { 0.0, 200.0 }, typeof(double), null!, CultureInfo.InvariantCulture);

        result.Should().Be(0.0);
    }

    [Fact]
    public void Convert_FullPercent_ShouldReturnContainerWidth()
    {
        var result = _converter.Convert(new object[] { 100.0, 300.0 }, typeof(double), null!, CultureInfo.InvariantCulture);

        result.Should().Be(300.0);
    }

    [Fact]
    public void Convert_OverPercent_ShouldClampToContainerWidth()
    {
        var result = _converter.Convert(new object[] { 150.0, 200.0 }, typeof(double), null!, CultureInfo.InvariantCulture);

        result.Should().Be(200.0);
    }

    [Fact]
    public void Convert_ZeroContainerWidth_ShouldReturnZero()
    {
        var result = _converter.Convert(new object[] { 50.0, 0.0 }, typeof(double), null!, CultureInfo.InvariantCulture);

        result.Should().Be(0.0);
    }

    [Fact]
    public void Convert_InsufficientValues_ShouldReturnZero()
    {
        var result = _converter.Convert(new object[] { 50.0 }, typeof(double), null!, CultureInfo.InvariantCulture);

        result.Should().Be(0.0);
    }

    [Fact]
    public void Convert_NonDoubleValues_ShouldReturnZero()
    {
        var result = _converter.Convert(new object[] { "not a number", 200.0 }, typeof(double), null!, CultureInfo.InvariantCulture);

        result.Should().Be(0.0);
    }

    [Fact]
    public void ConvertBack_ShouldThrow()
    {
        var act = () => _converter.ConvertBack(100.0, new[] { typeof(double) }, null!, CultureInfo.InvariantCulture);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void ProvideValue_ShouldReturnSameInstance()
    {
        var result = _converter.ProvideValue(null!);

        result.Should().BeSameAs(PercentToWidthConverter.Instance);
    }

    /// <summary>serves-spec: SPEC-006 rule 14 — the bar-fill converter yields 0.0 when either value is not a double; here the ancestor Grid's ActualWidth arrives as DependencyProperty.UnsetValue before the first measure pass.</summary>
    [Fact]
    public void Convert_NonDoubleContainerWidth_ShouldReturnZero()
    {
        var result = _converter.Convert(new object[] { 37.5, DependencyProperty.UnsetValue }, typeof(double), null!, CultureInfo.InvariantCulture);

        result.Should().Be(0.0);
    }

    /// <summary>serves-spec: SPEC-006 rule 14 — the bar-fill converter yields 0.0 whenever containerWidth is zero or negative; this pins the negative half of that guard.</summary>
    [Fact]
    public void Convert_NegativeContainerWidth_ShouldReturnZero()
    {
        var result = _converter.Convert(new object[] { 37.5, -288.0 }, typeof(double), null!, CultureInfo.InvariantCulture);

        result.Should().Be(0.0);
    }

    /// <summary>serves-spec: SPEC-006 rule 14 — the pixel width is clamped to the range 0 through containerWidth; this pins the low end of that clamp, which no existing test exercises.</summary>
    [Fact]
    public void Convert_NegativePercent_ShouldClampToZero()
    {
        var result = _converter.Convert(new object[] { -12.5, 288.0 }, typeof(double), null!, CultureInfo.InvariantCulture);

        result.Should().Be(0.0);
    }

    /// <summary>serves-spec: SPEC-006 rule 14 — the bar-fill converter yields 0.0 when given fewer than two values; an empty MultiBinding payload is the true lower boundary of that guard.</summary>
    [Fact]
    public void Convert_EmptyValuesArray_ShouldReturnZero()
    {
        var result = _converter.Convert(Array.Empty<object>(), typeof(double), null!, CultureInfo.InvariantCulture);

        result.Should().Be(0.0);
    }
}
