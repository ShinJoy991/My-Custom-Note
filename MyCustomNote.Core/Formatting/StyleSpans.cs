namespace MyCustomNote.Core.Formatting;

/// <summary>
/// Pure functions over style spans. No UI, no regex, no text parsing: the text is only used
/// for its length, to keep span edges from splitting a surrogate pair (an emoji), and to keep
/// spans from covering line breaks. Every function returns a new list and never changes its input.
/// </summary>
public static class StyleSpans
{
    /// <summary>
    /// Cleans a span list against the current text: clamps to [0, text length], drops empty or
    /// reversed spans, moves edges that fall inside a surrogate pair outward, cuts spans so none
    /// covers a '\n' (a span across two lines becomes one span per line), merges overlapping and
    /// touching spans of the same kind, and sorts by start, end, kind.
    /// </summary>
    public static IReadOnlyList<StyleSpan> Normalize(IEnumerable<StyleSpan> spans, string text)
    {
        ArgumentNullException.ThrowIfNull(spans);
        ArgumentNullException.ThrowIfNull(text);

        var cleaned = new List<StyleSpan>();
        foreach (var span in spans)
        {
            int start = Math.Clamp(span.Start, 0, text.Length);
            int end = Math.Clamp(span.End, 0, text.Length);
            if (end <= start)
            {
                continue; // checked before snapping so an empty span can never grow into an emoji
            }

            start = SnapDown(text, start);
            end = SnapUp(text, end);

            int segmentStart = start;
            for (int i = start; i < end; i++)
            {
                if (text[i] == '\n')
                {
                    if (i > segmentStart)
                    {
                        cleaned.Add(new StyleSpan(segmentStart, i, span.Kind));
                    }

                    segmentStart = i + 1;
                }
            }

            if (end > segmentStart)
            {
                cleaned.Add(new StyleSpan(segmentStart, end, span.Kind));
            }
        }

        var merged = new List<StyleSpan>();
        foreach (var group in cleaned.GroupBy(s => s.Kind))
        {
            StyleSpan? current = null;
            foreach (var span in group.OrderBy(s => s.Start).ThenBy(s => s.End))
            {
                if (current is null)
                {
                    current = span;
                }
                else if (span.Start <= current.Value.End)
                {
                    current = current.Value with { End = Math.Max(current.Value.End, span.End) };
                }
                else
                {
                    merged.Add(current.Value);
                    current = span;
                }
            }

            if (current is not null)
            {
                merged.Add(current.Value);
            }
        }

        return merged.OrderBy(s => s.Start).ThenBy(s => s.End).ThenBy(s => s.Kind).ToList();
    }

    /// <summary>
    /// True when every character of [start, end) carries <paramref name="kind"/>, even if the
    /// coverage comes from several touching spans. An empty range is never covered.
    /// Strict: line breaks count as characters. See <see cref="IsStyled"/> for the toolbar version.
    /// </summary>
    public static bool IsFullyCovered(IEnumerable<StyleSpan> spans, int start, int end, TextStyleKind kind)
    {
        ArgumentNullException.ThrowIfNull(spans);
        if (end <= start)
        {
            return false;
        }

        int covered = start;
        foreach (var span in spans.Where(s => s.Kind == kind && s.End > s.Start).OrderBy(s => s.Start))
        {
            if (span.End <= covered)
            {
                continue;
            }

            if (span.Start > covered)
            {
                return false; // gap
            }

            covered = span.End;
            if (covered >= end)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Toolbar "active" state: true when the selection has at least one character that is not a
    /// line break and every such character carries <paramref name="kind"/>. The selection may be
    /// reversed or out of range; it is ordered, clamped and widened to whole emoji.
    /// </summary>
    public static bool IsStyled(IEnumerable<StyleSpan> spans, string text, int selectionStart, int selectionEnd, TextStyleKind kind)
    {
        ArgumentNullException.ThrowIfNull(spans);
        ArgumentNullException.ThrowIfNull(text);

        int start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, text.Length);
        int end = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, text.Length);
        if (end <= start)
        {
            return false;
        }

        start = SnapDown(text, start);
        end = SnapUp(text, end);

        bool any = false;
        int segmentStart = start;
        for (int i = start; i <= end; i++)
        {
            if (i < end && text[i] != '\n')
            {
                continue;
            }

            if (i > segmentStart)
            {
                any = true;
                if (!IsFullyCovered(spans, segmentStart, i, kind))
                {
                    return false;
                }
            }

            segmentStart = i + 1;
        }

        return any;
    }

    /// <summary>
    /// Toolbar action. If the whole selection already has the style (line breaks ignored) it is
    /// removed from the selection (a span in the middle is split in two); otherwise it is added
    /// over the whole selection. The selection may be reversed or out of range; it is ordered,
    /// clamped to the text and widened to whole emoji. A collapsed selection changes nothing
    /// (typing style at a caret is not handled here). The result never covers a line break.
    /// </summary>
    public static IReadOnlyList<StyleSpan> Toggle(
        IEnumerable<StyleSpan> spans, string text, int selectionStart, int selectionEnd, TextStyleKind kind)
    {
        ArgumentNullException.ThrowIfNull(spans);
        ArgumentNullException.ThrowIfNull(text);

        var current = Normalize(spans, text);
        int start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, text.Length);
        int end = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, text.Length);
        if (end <= start)
        {
            return current;
        }

        start = SnapDown(text, start);
        end = SnapUp(text, end);

        return IsStyled(current, text, start, end, kind)
            ? Normalize(Remove(current, start, end, kind), text)
            : Normalize(current.Append(new StyleSpan(start, end, kind)), text);
    }

    /// <summary>
    /// Moves spans to follow a text edit that replaced <paramref name="removedLength"/> characters
    /// at <paramref name="editStart"/> with <paramref name="insertedLength"/> new ones (delete first,
    /// then insert). Rules, chosen like a word processor:
    /// text inserted inside a span, or right at its end, takes the span's style;
    /// text inserted right at its start does not (the span moves right);
    /// a span whose characters are all deleted disappears;
    /// replacing part of a span keeps the style on the replacement.
    /// The result is not normalized: call <see cref="Normalize"/> with the new text afterwards.
    /// </summary>
    public static IReadOnlyList<StyleSpan> ApplyEdit(
        IEnumerable<StyleSpan> spans, int editStart, int removedLength, int insertedLength)
    {
        ArgumentNullException.ThrowIfNull(spans);
        ArgumentOutOfRangeException.ThrowIfNegative(editStart);
        ArgumentOutOfRangeException.ThrowIfNegative(removedLength);
        ArgumentOutOfRangeException.ThrowIfNegative(insertedLength);

        var result = new List<StyleSpan>();
        foreach (var span in spans)
        {
            int start = span.Start;
            int end = span.End;
            if (end <= start)
            {
                continue;
            }

            if (removedLength > 0)
            {
                start = MapAfterDelete(start, editStart, removedLength);
                end = MapAfterDelete(end, editStart, removedLength);
                if (end <= start)
                {
                    continue;
                }
            }

            if (insertedLength > 0)
            {
                if (start >= editStart)
                {
                    start += insertedLength;
                    end += insertedLength;
                }
                else if (end >= editStart)
                {
                    end += insertedLength;
                }
            }

            result.Add(new StyleSpan(start, end, span.Kind));
        }

        return result;
    }

    private static IEnumerable<StyleSpan> Remove(IEnumerable<StyleSpan> spans, int start, int end, TextStyleKind kind)
    {
        foreach (var span in spans)
        {
            if (span.Kind != kind || span.End <= start || span.Start >= end)
            {
                yield return span;
                continue;
            }

            if (span.Start < start)
            {
                yield return span with { End = start };
            }

            if (span.End > end)
            {
                yield return span with { Start = end };
            }
        }
    }

    private static int MapAfterDelete(int position, int editStart, int removedLength) =>
        position <= editStart ? position
        : position >= editStart + removedLength ? position - removedLength
        : editStart;

    private static bool IsInsidePair(string text, int index) =>
        index > 0 && index < text.Length && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1]);

    private static int SnapDown(string text, int index) => IsInsidePair(text, index) ? index - 1 : index;

    private static int SnapUp(string text, int index) => IsInsidePair(text, index) ? index + 1 : index;
}
