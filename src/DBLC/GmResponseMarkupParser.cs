using System.Text.RegularExpressions;

namespace DBLC;

/// <summary>
/// Recognizes only the known GM response/speaker wrapper emitted by some local models.
/// It is deliberately not a general HTML stripper; all other markup remains untouched.
/// </summary>
public static class GmResponseMarkupParser
{
    private static readonly Regex ResponseOpen = new("""^\s*<div\s+class="gm-response"\s*>""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SpeakerOpen = new("""<div\s+class="speaker-segment"\s+data-speaker="(?<speaker>[^"]*)"\s*>""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ClosingDivAtEnd = new("""(?:\r?\n)?[ \t]*</div>[ \t]*$""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private const string ResponsePrefix = """<div class="gm-response">""";
    private const string SpeakerPrefix = """<div class="speaker-segment" data-speaker=""";

    public static string ForDisplay(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        if (TryUnwrap(text, out var presentation)) return Flatten(presentation.Segments);

        var leading = text.TrimStart();
        if (IsPartialPrefix(leading, ResponsePrefix)) return "";
        if (ResponseOpen.Match(text) is { Success: true } outer)
        {
            var remainder = text[outer.Length..].TrimStart(' ', '\t', '\r', '\n');
            if (IsPartialPrefix(remainder, SpeakerPrefix)) return "";
            var firstSpeaker = SpeakerOpen.Match(remainder);
            if (firstSpeaker.Success)
            {
                var beforeSpeaker = remainder[..firstSpeaker.Index];
                var afterSpeaker = remainder[(firstSpeaker.Index + firstSpeaker.Length)..];
                if (string.IsNullOrWhiteSpace(RemoveTrailingWrapperClosers(afterSpeaker)) || IsPartialPrefix(afterSpeaker.TrimStart(), "</div>"))
                    return beforeSpeaker;
            }
        }
        return text;
    }

    public static bool TryUnwrap(string? source, out GmPresentation presentation)
    {
        presentation = new GmPresentation();
        if (string.IsNullOrEmpty(source)) return false;
        var outer = ResponseOpen.Match(source);
        if (!outer.Success) return false;

        var body = source[outer.Length..];
        var speakers = SpeakerOpen.Matches(body);
        if (speakers.Count == 0) return false;
        var firstContent = body[..speakers[0].Index];
        if (!string.IsNullOrWhiteSpace(firstContent))
            presentation.Segments.Add(NewSegment("GM", TrimBoundaryLines(firstContent)));

        for (var i = 0; i < speakers.Count; i++)
        {
            var match = speakers[i];
            var start = match.Index + match.Length;
            var end = i + 1 < speakers.Count ? speakers[i + 1].Index : body.Length;
            var content = RemoveTrailingWrapperClosers(body[start..end]);
            content = TrimBoundaryLines(content);
            var label = CleanSpeaker(match.Groups["speaker"].Value);
            if (content.Length > 0) presentation.Segments.Add(NewSegment(label, content));
        }

        return presentation.Segments.Count > 0;
    }

    public static GmPresentation Normalize(GmPresentation presentation)
    {
        var segments = new List<GmPresentationSegment>();
        foreach (var segment in presentation.Segments)
        {
            if (TryUnwrap(segment.Text, out var nested))
                segments.AddRange(nested.Segments.Where(item => !string.IsNullOrWhiteSpace(item.Text)));
            else
            {
                segment.Text = ForDisplay(segment.Text);
                if (!string.IsNullOrWhiteSpace(segment.Text)) segments.Add(segment);
            }
        }
        presentation.Segments = segments;
        return presentation;
    }

    private static string RemoveTrailingWrapperClosers(string text)
    {
        var result = text;
        for (var i = 0; i < 4; i++)
        {
            var match = ClosingDivAtEnd.Match(result);
            if (!match.Success) break;
            result = result[..match.Index];
        }
        return result;
    }

    private static string TrimBoundaryLines(string value) => value.Trim('\r', '\n');
    private static string Flatten(IEnumerable<GmPresentationSegment> segments) => string.Join("\n\n", segments.Select(segment => segment.Text));
    private static bool IsPartialPrefix(string text, string prefix) => text.Length > 0 && text.Length < prefix.Length && prefix.StartsWith(text, StringComparison.OrdinalIgnoreCase);
    private static string CleanSpeaker(string value)
    {
        var label = new string(value.Trim().Where(character => !char.IsControl(character)).Take(72).ToArray());
        return string.IsNullOrWhiteSpace(label) ? "GM" : label;
    }
    private static GmPresentationSegment NewSegment(string speaker, string text) => new() { Speaker = speaker, Text = text, Flavor = "DEFAULT" };
}
