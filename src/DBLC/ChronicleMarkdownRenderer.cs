using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;
using Markdig;

namespace DBLC;

/// <summary>
/// Renders Chronicle source as safe presentation HTML. Stored Chronicle text remains unchanged.
/// </summary>
public static class ChronicleMarkdownRenderer
{
    private static readonly (string Source, string Symbol)[] ProseArrows =
    [
        ("$\\leftrightarrow$", "↔"), ("$\\rightarrow$", "→"), ("$\\leftarrow$", "←"),
        ("$\\Leftrightarrow$", "⇔"), ("$\\Rightarrow$", "⇒"), ("$\\Leftarrow$", "⇐"),
        ("$\\to$", "→")
    ];
    private static readonly Regex EscapedFenceBlock = new(@"(?<before>\\n|^)(?<opening> {0,3}(?:`{3,}|~{3,})[^\x0A\x0D]*?)\\n(?<body>.*?)(?<closingBreak>\\n)(?<closing> {0,3}(?:`{3,}|~{3,})[ \x09]*)(?<after>\\n|$)", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();
    private static readonly char[] GlitchAboveMarks = ['\u0300', '\u0301', '\u0302', '\u0303', '\u0304', '\u0306', '\u0307', '\u0308', '\u030A', '\u030B', '\u030C'];
    private static readonly char[] GlitchBelowMarks = ['\u0323', '\u0327', '\u0328', '\u0331', '\u0332', '\u0333'];
    private static readonly char[] GlitchOverlayMarks = ['\u0335', '\u0336'];
    private static readonly char[] GlitchLowMarks = ['\u0307', '\u0336', '\u0323'];

    public static string Render(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return "";
        return Sanitizer.Sanitize(Markdown.ToHtml(NormalizeProseSymbols(NormalizeEscapedProseLineBreaks(markdown)), Pipeline));
    }

    public static string RenderGlitched(string? markdown, string preset, int? savedSeed, string stableKey)
    {
        if (string.IsNullOrEmpty(markdown)) return "";
        if (preset is not ("low" or "medium" or "cursed")) return Render(markdown);

        var safeHtml = Sanitizer.Sanitize(Markdown.ToHtml(NormalizeProseSymbols(NormalizeEscapedProseLineBreaks(markdown)), Pipeline));
        var document = new HtmlParser().ParseDocument(safeHtml);
        if (document.Body is not { } body) return safeHtml;
        var seed = savedSeed is { } stored ? unchecked((uint)stored) : StableSeed(stableKey);
        var eligibleIndex = 0;
        GlitchTextNodes(body, preset, seed, ref eligibleIndex);
        return body.InnerHtml;
    }

    // Presentation only. An incomplete arrow token is held during streaming so
    // the browser can append the finished symbol without replacing earlier text.
    public static string NormalizeProseSymbols(string? source, bool holdIncomplete = false)
    {
        if (string.IsNullOrEmpty(source)) return source ?? "";
        var output = new StringBuilder(source.Length);
        var fenced = false;
        var fenceCharacter = '\0';
        var fenceLength = 0;
        var inlineTicks = 0;
        for (var start = 0; start < source.Length;)
        {
            var end = source.IndexOf('\n', start);
            if (end < 0) end = source.Length;
            var contentEnd = end > start && source[end - 1] == '\r' ? end - 1 : end;
            var line = source.AsSpan(start, contentEnd - start);
            if (TryGetFence(line, out var marker, out var length))
            {
                if (!fenced) { fenced = true; fenceCharacter = marker; fenceLength = length; }
                else if (marker == fenceCharacter && length >= fenceLength) fenced = false;
                output.Append(source, start, contentEnd - start);
            }
            else if (fenced || IsIndentedCode(line)) output.Append(source, start, contentEnd - start);
            else
            {
                for (var index = start; index < contentEnd;)
                {
                    if (source[index] == '`')
                    {
                        var run = CountRun(source, index, contentEnd, '`');
                        if (inlineTicks == 0) inlineTicks = run;
                        else if (run == inlineTicks) inlineTicks = 0;
                        output.Append(source, index, run); index += run; continue;
                    }
                    if (inlineTicks == 0 && source[index] == '$' && (index == start || source[index - 1] != '\\'))
                    {
                        var remaining = source.AsSpan(index, contentEnd - index);
                        var matched = false;
                        var incomplete = false;
                        foreach (var arrow in ProseArrows)
                        {
                            if (remaining.StartsWith(arrow.Source, StringComparison.Ordinal))
                            {
                                output.Append(arrow.Symbol); index += arrow.Source.Length; matched = true; break;
                            }
                            if (arrow.Source.AsSpan().StartsWith(remaining, StringComparison.Ordinal)) incomplete = true;
                        }
                        if (matched) continue;
                        if (holdIncomplete && end == source.Length && incomplete) break;
                    }
                    output.Append(source[index++]);
                }
            }
            output.Append(source, contentEnd, end - contentEnd);
            if (end < source.Length) output.Append('\n');
            start = end + 1;
        }
        return output.ToString();
    }

    // Some model responses arrive as Markdown whose paragraph separators are literal
    // backslash-n text. Normalize only prose; code and valid JSON remain verbatim.
    // Stored Chronicle source is not changed, so context and persistence stay faithful.
    private static string NormalizeEscapedProseLineBreaks(string markdown)
    {
        markdown = NormalizeEscapedFenceBoundaries(markdown);
        var output = new StringBuilder(markdown.Length);
        var fenced = false;
        var fenceCharacter = '\0';
        var fenceLength = 0;
        var inlineCodeTicks = 0;
        var lineStart = 0;
        while (lineStart < markdown.Length)
        {
            var lineEnd = markdown.IndexOf('\n', lineStart);
            if (lineEnd < 0) lineEnd = markdown.Length;
            var contentEnd = lineEnd > lineStart && markdown[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;
            var line = markdown.AsSpan(lineStart, contentEnd - lineStart);
            if (TryGetFence(line, out var marker, out var markerLength))
            {
                if (!fenced) { fenced = true; fenceCharacter = marker; fenceLength = markerLength; }
                else if (marker == fenceCharacter && markerLength >= fenceLength) fenced = false;
                output.Append(markdown, lineStart, lineEnd - lineStart);
            }
            else if (fenced || IsIndentedCode(line)) output.Append(markdown, lineStart, lineEnd - lineStart);
            else
            {
                AppendProseLine(output, markdown, lineStart, contentEnd, ref inlineCodeTicks);
                output.Append(markdown, contentEnd, lineEnd - contentEnd);
            }
            if (lineEnd < markdown.Length) output.Append('\n');
            lineStart = lineEnd + 1;
        }
        return output.ToString();
    }

    private static string NormalizeEscapedFenceBoundaries(string markdown) => EscapedFenceBlock.Replace(markdown, match =>
    {
        var opening = match.Groups["opening"].Value;
        var closing = match.Groups["closing"].Value;
        var openMarker = opening.TrimStart(' ');
        var closeMarker = closing.TrimStart(' ');
        if (openMarker.Length < 3 || closeMarker.Length < 3 || openMarker[0] != closeMarker[0] || closeMarker.TakeWhile(character => character == closeMarker[0]).Count() < openMarker.TakeWhile(character => character == openMarker[0]).Count())
            return match.Value;
        static string Boundary(string value) => value == "\\n" ? "\n" : value;
        return Boundary(match.Groups["before"].Value) + opening + "\n" + match.Groups["body"].Value + "\n" + closing + Boundary(match.Groups["after"].Value);
    });
    private static bool TryGetFence(ReadOnlySpan<char> line, out char marker, out int length)
    {
        marker = '\0'; length = 0;
        var index = 0;
        while (index < line.Length && index < 3 && line[index] == ' ') index++;
        if (index >= line.Length || line[index] is not ('`' or '~')) return false;
        marker = line[index];
        while (index + length < line.Length && line[index + length] == marker) length++;
        return length >= 3;
    }

    private static bool IsIndentedCode(ReadOnlySpan<char> line)
    {
        var spaces = 0;
        while (spaces < line.Length && spaces < 4 && line[spaces] == ' ') spaces++;
        return spaces == 4 || (line.Length > 0 && line[0] == '\t');
    }
    private static void AppendProseLine(StringBuilder output, string source, int start, int end, ref int inlineCodeTicks)
    {
        for (var index = start; index < end;)
        {
            if (source[index] == '`')
            {
                var run = CountRun(source, index, end, '`');
                if (inlineCodeTicks == 0)
                {
                    var close = FindRun(source, index + run, end, '`', run);
                    if (close >= 0) { output.Append(source, index, close + run - index); index = close + run; continue; }
                    inlineCodeTicks = run;
                }
                else if (run == inlineCodeTicks)
                {
                    inlineCodeTicks = 0;
                    output.Append(source, index, run); index += run; continue;
                }
                output.Append(source, index, run); index += run; continue;
            }
            if (inlineCodeTicks == 0 && (source[index] is '{' or '[' or '"') && TryGetJsonLiteralEnd(source, index, end, out var jsonEnd))
            {
                output.Append(source, index, jsonEnd - index); index = jsonEnd; continue;
            }
            if (inlineCodeTicks == 0 && source[index] == '\\' && index + 1 < end && source[index + 1] == 'n')
            {
                var precedingSlashes = 0;
                for (var previous = index - 1; previous >= start && source[previous] == '\\'; previous--) precedingSlashes++;
                if (precedingSlashes % 2 == 0) { output.Append('\n'); index += 2; continue; }
            }
            output.Append(source[index++]);
        }
    }

    private static int CountRun(string source, int start, int end, char value)
    {
        var index = start;
        while (index < end && source[index] == value) index++;
        return index - start;
    }

    private static int FindRun(string source, int start, int end, char value, int length)
    {
        for (var index = start; index < end;)
        {
            if (source[index] == value)
            {
                var run = CountRun(source, index, end, value);
                if (run == length) return index;
                index += run;
            }
            else index++;
        }
        return -1;
    }

    private static bool TryGetJsonLiteralEnd(string source, int start, int end, out int jsonEnd)
    {
        jsonEnd = start;
        if (source[start] == '"')
        {
            var escaped = false;
            for (var index = start + 1; index < end; index++)
            {
                var character = source[index];
                if (escaped) escaped = false;
                else if (character == '\\') escaped = true;
                else if (character == '"')
                {
                    if (!IsValidJson(source.Substring(start, index - start + 1))) return false;
                    jsonEnd = index + 1; return true;
                }
            }
            return false;
        }
        var stack = new Stack<char>();
        var inString = false;
        var escapedString = false;
        for (var index = start; index < end; index++)
        {
            var character = source[index];
            if (inString)
            {
                if (escapedString) escapedString = false;
                else if (character == '\\') escapedString = true;
                else if (character == '"') inString = false;
                continue;
            }
            if (character == '"') inString = true;
            else if (character is '{' or '[') stack.Push(character == '{' ? '}' : ']');
            else if (character is '}' or ']')
            {
                if (stack.Count == 0 || stack.Pop() != character) return false;
                if (stack.Count == 0)
                {
                    if (!IsValidJson(source.Substring(start, index - start + 1))) return false;
                    jsonEnd = index + 1; return true;
                }
            }
        }
        return false;
    }

    private static bool IsValidJson(string candidate)
    {
        try { using var _ = System.Text.Json.JsonDocument.Parse(candidate); return true; }
        catch (System.Text.Json.JsonException) { return false; }
    }
    private static void GlitchTextNodes(INode parent, string preset, uint segmentSeed, ref int eligibleIndex)
    {
        foreach (var child in parent.ChildNodes.ToArray())
        {
            if (child is IText textNode)
            {
                var fragment = parent.Owner!.CreateDocumentFragment();
                foreach (var rune in textNode.Data.EnumerateRunes())
                {
                    if (!Rune.IsLetterOrDigit(rune))
                    {
                        fragment.AppendChild(parent.Owner.CreateTextNode(rune.ToString()));
                        continue;
                    }

                    var affected = preset switch
                    {
                        "low" => eligibleIndex % 5 == 2,
                        "medium" => eligibleIndex % 5 != 0,
                        _ => true
                    };
                    if (affected)
                    {
                        var glyph = new StringBuilder(rune.ToString());
                        AppendGlitchMarks(glyph, rune, eligibleIndex, preset, segmentSeed);
                        var span = parent.Owner.CreateElement("span");
                        span.SetAttribute("class", "glitch-glyph glitch-glyph-" + preset);
                        span.SetAttribute("data-clean-glyph", rune.ToString());
                        span.TextContent = glyph.ToString();
                        fragment.AppendChild(span);
                    }
                    else fragment.AppendChild(parent.Owner.CreateTextNode(rune.ToString()));
                    eligibleIndex++;
                }
                parent.ReplaceChild(fragment, child);
            }
            else
            {
                GlitchTextNodes(child, preset, segmentSeed, ref eligibleIndex);
            }
        }
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[] { "p", "br", "hr", "h1", "h2", "h3", "h4", "h5", "h6", "strong", "em", "del", "ul", "ol", "li", "blockquote", "pre", "code", "a" })
            sanitizer.AllowedTags.Add(tag);

        sanitizer.AllowedAttributes.Clear();
        foreach (var attribute in new[] { "href", "title", "class", "start" })
            sanitizer.AllowedAttributes.Add(attribute);

        sanitizer.AllowedSchemes.Clear();
        foreach (var scheme in new[] { "http", "https", "mailto" })
            sanitizer.AllowedSchemes.Add(scheme);
        sanitizer.AllowedCssProperties.Clear();
        return sanitizer;
    }

    private static uint NextGlitchValue(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    private static uint StableSeed(string value)
    {
        var hash = 2166136261u;
        foreach (var character in value) hash = unchecked((hash ^ character) * 16777619u);
        return hash == 0 ? 0x9E3779B9u : hash;
    }

    private static void AppendGlitchMarks(StringBuilder target, Rune rune, int index, string preset, uint segmentSeed)
    {
        var seed = unchecked(segmentSeed ^ (uint)rune.Value * 2654435761u ^ (uint)(index + 1) * 2246822519u ^ 0x9E3779B9u);
        if (seed == 0) seed = 0xA341316Cu;
        if (preset == "low")
        {
            target.Append(GlitchLowMarks[NextGlitchValue(ref seed) % (uint)GlitchLowMarks.Length]);
            return;
        }

        var above = 0;
        var below = 0;
        var overlay = 1;
        if (preset == "medium") { above = 3; below = 3; }
        else
        {
            var count = 3 + (int)(NextGlitchValue(ref seed) % 22);
            var vertical = count - overlay;
            above = vertical / 2 + (vertical % 2);
            below = vertical / 2;
        }

        var total = above + below + overlay;
        for (var remaining = total; remaining > 0; remaining--)
        {
            var choice = (int)(NextGlitchValue(ref seed) % (uint)remaining);
            if (choice < above)
            {
                target.Append(GlitchAboveMarks[NextGlitchValue(ref seed) % (uint)GlitchAboveMarks.Length]);
                above--;
            }
            else if ((choice -= above) < below)
            {
                target.Append(GlitchBelowMarks[NextGlitchValue(ref seed) % (uint)GlitchBelowMarks.Length]);
                below--;
            }
            else
            {
                target.Append(GlitchOverlayMarks[NextGlitchValue(ref seed) % (uint)GlitchOverlayMarks.Length]);
                overlay--;
            }
        }
    }
}
