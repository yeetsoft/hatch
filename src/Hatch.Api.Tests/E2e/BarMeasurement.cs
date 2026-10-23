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

/// <summary>
/// One <c>&lt;button&gt;</c> or <c>&lt;input&gt;</c> in a Runners row's control
/// group: what it is called, its box, the box of the table cell it sits in, and
/// its own <c>scrollWidth</c> against its <c>clientWidth</c> (a clipped label
/// overflows its button without moving the button's box).
/// </summary>
/// <param name="Name"><c>button "Pause"</c> or <c>input "Ticket to work"</c>, from the control's text or its <c>aria-label</c>.</param>
internal sealed record ControlMeasurement(string Name, Box Box, Box Cell, int ScrollWidth, int ClientWidth)
{
    public override string ToString() =>
        $"{Name}: {Box} in cell [{Cell}] scrollWidth={ScrollWidth} clientWidth={ClientWidth}";
}

/// <summary>
/// What the Runners page looks like at the moment it was read: the viewport,
/// the document's width against it, whether a coarse pointer matches, the card
/// and the table, and every control in a row's <c>.hatch-reorder--wrap</c>.
/// </summary>
/// <param name="Card">
/// The flush card's <em>content</em> box: its <c>getBoundingClientRect</c> less
/// the 1px border on each side. A flush card has no padding, so nothing else
/// separates the border from the table.
/// </param>
internal sealed record RunnersMeasurement(
    int InnerWidth,
    int ScrollWidth,
    int ClientWidth,
    bool CoarsePointerMatches,
    Box Card,
    Box Table,
    IReadOnlyList<ControlMeasurement> Controls)
{
    /// <summary>One line per fact, controls in document order, so the case's output, a ticket comment and an assertion's message read the same.</summary>
    public override string ToString()
    {
        var lines = new List<string>
        {
            $"innerWidth: {InnerWidth}",
            $"(pointer: coarse) matches: {CoarsePointerMatches}",
            $"document scrollWidth: {ScrollWidth}",
            $"document clientWidth: {ClientWidth}",
            $"card content box: {Card}",
            $"table: {Table}",
        };

        lines.AddRange(Controls.Select(c => c.ToString()));

        return string.Join(Environment.NewLine, lines);
    }
}
