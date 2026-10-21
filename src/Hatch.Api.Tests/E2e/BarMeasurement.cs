using System.Globalization;

namespace Hatch.Api.Tests.E2e;

/// <summary>A rectangle in CSS pixels, as <c>getBoundingClientRect()</c> reports it.</summary>
internal sealed record Box(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"x={X:0.##} y={Y:0.##} w={Width:0.##} h={Height:0.##} right={Right:0.##} bottom={Bottom:0.##}");
}

/// <summary>
/// What the top bar looks like at the moment it was read: the viewport, the
/// document's width against it, two media queries, whether the primary nav is
/// drawn, and each bar control's box by name. A null <see cref="Box"/> means
/// the control is absent from the document, which is not the same as a zero box.
/// </summary>
internal sealed record BarMeasurement(
    int InnerWidth,
    int ScrollWidth,
    int ClientWidth,
    bool PhoneQueryMatches,
    bool CoarsePointerMatches,
    bool PrimaryNavDrawn,
    IReadOnlyDictionary<string, Box?> Controls)
{
    /// <summary>One line per fact, controls in bar order, so the case's output, a ticket comment and an assertion's message read the same.</summary>
    public override string ToString()
    {
        var lines = new List<string>
        {
            $"innerWidth: {InnerWidth}",
            $"(max-width: 40rem) matches: {PhoneQueryMatches}",
            $"(pointer: coarse) matches: {CoarsePointerMatches}",
            $".hatch-primary-nav drawn: {PrimaryNavDrawn}",
            $"document scrollWidth: {ScrollWidth}",
            $"document clientWidth: {ClientWidth}",
        };

        foreach (var (name, box) in Controls)
            lines.Add($"{name}: {(box is null ? "absent" : box.ToString())}");

        return string.Join(Environment.NewLine, lines);
    }
}
