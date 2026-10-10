using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DBLC;

public sealed record GmNarrationEffectSpan(int Start, int Length, string Effect);
public sealed record GmNarrationTextBatch(string Text, string Effect, int Start, string Speaker = "GM");

/// <summary>Incrementally removes optional GM effect markers while preserving code examples and source text.</summary>
public sealed class GmNarrationMarkerParser
{
    public static readonly string[] Effects = ["focus", "shaky", "3d", "vaporwave", "smoke", "hyperspace"];
    private static readonly string[] Markers = Effects.Select(effect => "[[" + effect + "]]").ToArray();
    private readonly StringBuilder _pending = new();
    private readonly List<GmNarrationEffectSpan> _spans = [];
    private char _fenceCharacter;
    private int _fenceLength;
    private int _inlineTicks;
    private bool _lineOnlyIndent = true;
    private int _cleanLength;
    private bool _collapseNextHorizontalSpace;
    private string _activeEffect;
    private readonly string _defaultEffect;
    private readonly bool _allowInlineEffects;
    private readonly bool _allowCharacterSpeakers;
    private string _activeSpeaker;
    private readonly Action<string>? _onCitation;
    private bool _literalCitation;
    private bool _literalCitationLastCloseBracket;

    public GmNarrationMarkerParser(string defaultEffect, bool allowInlineEffects, string? initialEffect = null, bool allowCharacterSpeakers = false, string initialSpeaker = "GM", Action<string>? onCitation = null)
    {
        _defaultEffect = NormalizeEffect(defaultEffect);
        _allowInlineEffects = allowInlineEffects;
        _activeEffect = NormalizeEffect(initialEffect ?? _defaultEffect);
        _allowCharacterSpeakers = allowCharacterSpeakers;
        _activeSpeaker = NormalizeSpeaker(initialSpeaker);
        _onCitation = onCitation;
    }

    public string ActiveEffect => _activeEffect;
    public string ActiveSpeaker => _activeSpeaker;
    public IReadOnlyList<GmNarrationEffectSpan> Spans => _spans;

    public IReadOnlyList<GmNarrationTextBatch> Append(string? chunk) => Process(chunk ?? "", flush: false);
    public IReadOnlyList<GmNarrationTextBatch> Flush() => Process("", flush: true);

    public static string RemoveMarkers(string? text, bool removeCitations = false)
    {
        var parser = new GmNarrationMarkerParser("focus", allowInlineEffects: true, onCitation: removeCitations ? _ => { } : null);
        var batches = parser.Append(text);
        var final = parser.Flush();
        return string.Concat(batches.Concat(final).Select(batch => batch.Text));
    }

    private IReadOnlyList<GmNarrationTextBatch> Process(string chunk, bool flush)
    {
        _pending.Append(chunk);
        var batches = new List<GmNarrationTextBatch>();
        var run = new StringBuilder();
        var runEffect = _activeEffect;
        var runSpeaker = _activeSpeaker;
        void EmitRun()
        {
            if (run.Length == 0) return;
            var value = run.ToString();
            batches.Add(new GmNarrationTextBatch(value, runEffect, _cleanLength, runSpeaker));
            var length = value.Length;
            if (_spans.Count > 0 && _spans[^1].Effect == runEffect && _spans[^1].Start + _spans[^1].Length == _cleanLength)
                _spans[^1] = _spans[^1] with { Length = _spans[^1].Length + length };
            else _spans.Add(new GmNarrationEffectSpan(_cleanLength, length, runEffect));
            _cleanLength += length;
            run.Clear();
        }
        void Put(char value)
        {
            if (_collapseNextHorizontalSpace)
            {
                _collapseNextHorizontalSpace = false;
                if (value is ' ' or '\t') return;
            }
            run.Append(value);
            if (value == '\n') _lineOnlyIndent = true;
            else if (!_lineOnlyIndent || value is not (' ' or '\t' or '\r')) _lineOnlyIndent = false;
        }

        var input = _pending.ToString();
        var index = 0;
        while (index < input.Length)
        {
            if (_literalCitation)
            {
                var character = input[index++];
                Put(character);
                if (character == ']' && _literalCitationLastCloseBracket)
                {
                    _literalCitation = false;
                    _literalCitationLastCloseBracket = false;
                }
                else _literalCitationLastCloseBracket = character == ']';
                continue;
            }
            if (_fenceCharacter == '\0' && _inlineTicks == 0 && input[index] == '[' && input.AsSpan(index).StartsWith("[[", StringComparison.Ordinal))
            {
                var end = input.IndexOf("]]", index + 2, StringComparison.Ordinal);
                if (end < 0 && !flush && IsMarkerPrefix(input.AsSpan(index))) break;
                if (end >= 0)
                {
                    var candidate = input.Substring(index, end + 2 - index);
                    var found = Array.FindIndex(Markers, marker => marker.Equals(candidate, StringComparison.OrdinalIgnoreCase));
                    if (found >= 0)
                    {
                        var collapseSpace = _collapseNextHorizontalSpace || (run.Length > 0 && run[^1] is ' ' or '\t');
                        EmitRun();
                        _collapseNextHorizontalSpace = collapseSpace;
                        _activeEffect = _allowInlineEffects ? Effects[found] : _defaultEffect;
                        runEffect = _activeEffect;
                        runSpeaker = _activeSpeaker;
                        index = end + 2;
                        continue;
                    }
                    if (TryParseSpeakerMarker(candidate, out var speaker))
                    {
                        EmitRun();
                        if (_allowCharacterSpeakers && !string.Equals(_activeSpeaker, speaker, StringComparison.OrdinalIgnoreCase))
                        {
                            _activeSpeaker = speaker;
                            _activeEffect = _defaultEffect;
                        }
                        else if (!_allowCharacterSpeakers) _activeSpeaker = "GM";
                        runSpeaker = _activeSpeaker;
                        runEffect = _activeEffect;
                        index = end + 2;
                        continue;
                    }
                    if (_onCitation is not null && candidate.StartsWith("[[cite:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryParseCitationMarker(candidate, out var handles))
                        {
                            EmitRun();
                            _onCitation(handles);
                        }
                        else
                        {
                            foreach (var character in candidate) Put(character);
                        }
                        index = end + 2;
                        continue;
                    }
                }
                if (_onCitation is not null && end < 0 && input.AsSpan(index).StartsWith("[[cite:", StringComparison.OrdinalIgnoreCase) && input.Length - index > 96)
                {
                    _literalCitation = true;
                    _literalCitationLastCloseBracket = false;
                    continue;
                }
                if (flush && _onCitation is not null && end < 0 && input.AsSpan(index).StartsWith("[[cite:", StringComparison.OrdinalIgnoreCase) && input.Length - index <= 96)
                {
                    for (var remaining = index; remaining < input.Length; remaining++) Put(input[remaining]);
                    index = input.Length;
                    continue;
                }
                if (!flush && IsMarkerPrefix(input.AsSpan(index))) break;
            }
            if ((input[index] is '`' or '~') && (_fenceCharacter == '\0' || input[index] == _fenceCharacter || _inlineTicks == 0))
            {
                var marker = input[index];
                var length = 1;
                while (index + length < input.Length && input[index + length] == marker) length++;
                if (index + length == input.Length && !flush) break;
                if (_fenceCharacter != '\0')
                {
                    if (marker == _fenceCharacter && length >= _fenceLength && _lineOnlyIndent)
                    {
                        for (var i = 0; i < length; i++) Put(marker);
                        index += length;
                        _fenceCharacter = '\0'; _fenceLength = 0;
                        continue;
                    }
                }
                else if (_inlineTicks != 0)
                {
                    if (marker == '`' && length == _inlineTicks)
                    {
                        for (var i = 0; i < length; i++) Put(marker);
                        index += length; _inlineTicks = 0;
                        continue;
                    }
                }
                else if (marker == '`' && length >= 3 && _lineOnlyIndent)
                {
                    for (var i = 0; i < length; i++) Put(marker);
                    index += length; _fenceCharacter = '`'; _fenceLength = length;
                    continue;
                }
                else if (marker == '~' && length >= 3 && _lineOnlyIndent)
                {
                    for (var i = 0; i < length; i++) Put(marker);
                    index += length; _fenceCharacter = '~'; _fenceLength = length;
                    continue;
                }
                else if (marker == '`')
                {
                    for (var i = 0; i < length; i++) Put(marker);
                    index += length; _inlineTicks = length;
                    continue;
                }
                for (var i = 0; i < length; i++) Put(marker);
                index += length;
                continue;
            }
            Put(input[index++]);
        }

        EmitRun();
        _pending.Clear();
        if (index < input.Length) _pending.Append(input.AsSpan(index));
        return batches;
    }

    private static bool IsMarkerPrefix(ReadOnlySpan<char> text)
    {
        foreach (var marker in Markers)
            if (text.Length < marker.Length && marker.AsSpan().StartsWith(text, StringComparison.OrdinalIgnoreCase)) return true;
        const string speakerPrefix = "[[speaker:";
        if (text.Length < speakerPrefix.Length && speakerPrefix.AsSpan().StartsWith(text, StringComparison.OrdinalIgnoreCase)) return true;
        if (text.StartsWith(speakerPrefix, StringComparison.OrdinalIgnoreCase) && text.IndexOf("]]".AsSpan(), StringComparison.Ordinal) < 0) return true;
        const string citationPrefix = "[[cite:";
        if (text.Length < citationPrefix.Length && citationPrefix.AsSpan().StartsWith(text, StringComparison.OrdinalIgnoreCase)) return true;
        if (text.StartsWith(citationPrefix, StringComparison.OrdinalIgnoreCase) && text.Length <= 96 && text.IndexOf("]]".AsSpan(), StringComparison.Ordinal) < 0) return true;
        return false;
    }

    private static bool TryParseCitationMarker(string marker, out string handles)
    {
        handles = "";
        if (!marker.StartsWith("[[cite:", StringComparison.OrdinalIgnoreCase) || !marker.EndsWith("]]", StringComparison.Ordinal) || marker.Length > 96) return false;
        var value = marker[7..^2];
        foreach (var part in value.Split(','))
        {
            var handle = part.Trim();
            if (handle.Length < 10 || handle[8] != '-' || handle[9] is < '1' or > '9') return false;
            for (var index = 0; index < 8; index++)
                if (!char.IsAsciiHexDigit(handle[index])) return false;
            for (var index = 10; index < handle.Length; index++)
                if (!char.IsAsciiDigit(handle[index])) return false;
        }
        handles = value;
        return true;
    }

    public static string NormalizeSpeaker(string? speaker)
    {
        var value = new string((speaker ?? "").Trim().Where(character => !char.IsControl(character) && character is not ('[' or ']' or '<' or '>')).Take(72).ToArray()).Trim();
        return value.Length == 0 || value.Equals("GM", StringComparison.OrdinalIgnoreCase) || value.Equals("THE GM", StringComparison.OrdinalIgnoreCase) || value.Equals("YOUR GM", StringComparison.OrdinalIgnoreCase) || value.Equals("GAME MASTER", StringComparison.OrdinalIgnoreCase) ? "GM" : value;
    }

    private static bool TryParseSpeakerMarker(string marker, out string speaker)
    {
        speaker = "GM";
        if (!marker.StartsWith("[[speaker:", StringComparison.OrdinalIgnoreCase) || !marker.EndsWith("]]", StringComparison.Ordinal)) return false;
        var value = marker[10..^2];
        if (value.Length == 0 || value.Length > 72 || value.Any(char.IsControl) || value.IndexOfAny(['[', ']', '<', '>']) >= 0 || string.IsNullOrWhiteSpace(value)) return false;
        speaker = NormalizeSpeaker(value);
        return true;
    }

    public static string NormalizeEffect(string? effect) => Effects.FirstOrDefault(value => string.Equals(value, effect, StringComparison.OrdinalIgnoreCase)) ?? "focus";
}
