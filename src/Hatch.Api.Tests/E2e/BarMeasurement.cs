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
/// What the board's filter bar looks like at the moment it was read: the
/// viewport, the document's width against it, whether a coarse pointer matches,
/// and each child of <c>.hatch-filter-trailing</c> by name and box, in bar
/// order. The top bar's <see cref="BarMeasurement"/> does not reach these.
/// </summary>
internal sealed record BoardBarMeasurement(
    int InnerWidth,
    int ScrollWidth,
    int ClientWidth,
    bool CoarsePointerMatches,
    IReadOnlyList<(string Name, Box Box)> Trailing,
    Box? Search = null)
{
    public override string ToString()
    {
        var lines = new List<string>
        {
            $"innerWidth: {InnerWidth}",
            $"(pointer: coarse) matches: {CoarsePointerMatches}",
            $"document scrollWidth: {ScrollWidth}",
            $"document clientWidth: {ClientWidth}",
        };

        if (Search is not null) lines.Add($"search: {Search}");
        lines.AddRange(Trailing.Select(t => $"{t.Name}: {t.Box}"));

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// What the board's open Types panel looks like at the moment it was read: the
/// viewport, the document's width against it, whether a coarse pointer matches,
/// the menu panel's box (null if no Only button was found) and each
/// <c>.hatch-types-only</c> by name and box, in row order.
/// </summary>
internal sealed record TypesOnlyMeasurement(
    int InnerWidth,
    int ScrollWidth,
    int ClientWidth,
    bool CoarsePointerMatches,
    Box? Panel,
    IReadOnlyList<(string Name, Box Box)> Only)
{
    public override string ToString()
    {
        var lines = new List<string>
        {
            $"innerWidth: {InnerWidth}",
            $"(pointer: coarse) matches: {CoarsePointerMatches}",
            $"document scrollWidth: {ScrollWidth}",
            $"document clientWidth: {ClientWidth}",
            $"panel: {(Panel is null ? "absent" : Panel.ToString())}",
        };

        lines.AddRange(Only.Select(o => $"{o.Name}: {o.Box}"));

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// One element of the Omitted runners section: what it is called, its box, and
/// its own <c>scrollWidth</c> against its <c>clientWidth</c> (a clipped label
/// overflows its element without moving the element's box).
/// </summary>
/// <param name="Name"><c>li.hatch-omission "Own Runner"</c> or <c>input "Reason"</c>, from the tag, class and text or <c>aria-label</c>.</param>
/// <param name="Control">True for a <c>&lt;button&gt;</c> or <c>&lt;input&gt;</c>, the elements held to their own <c>scrollWidth</c>.</param>
internal sealed record OmissionElement(string Name, bool Control, Box Box, int ScrollWidth, int ClientWidth)
{
    public override string ToString() => $"{Name}: {Box} scrollWidth={ScrollWidth} clientWidth={ClientWidth}";
}

/// <summary>
/// The Details card's Omitted runners section as the page stands: the viewport,
/// the document's width against it, the card's border box, and every
/// <c>.hatch-omission</c> row with its descendants and every element of the
/// <c>.hatch-omission-add</c> form.
/// </summary>
internal sealed record OmittedRunnersMeasurement(
    int InnerWidth,
    int ScrollWidth,
    int ClientWidth,
    Box Card,
    IReadOnlyList<OmissionElement> Elements)
{
    public override string ToString() => string.Join(Environment.NewLine, new[]
    {
        $"innerWidth: {InnerWidth}",
        $"document scrollWidth: {ScrollWidth}",
        $"document clientWidth: {ClientWidth}",
        $"card: {Card}",
    }.Concat(Elements.Select(e => e.ToString())));
}
