namespace DBLC;

// Capture on the desktop dispatcher, which owns Chronicle mutation. Browser
// renders enumerate these copies rather than lists a GM turn can still change.
public sealed record MobileChronicleSnapshot(Guid? SessionId, IReadOnlyList<ChatLine> Lines)
{
    public static MobileChronicleSnapshot Capture(Conversation? conversation) => new(conversation?.Id,
        conversation?.Lines.Select(line => new ChatLine
        {
            Id = line.Id, Sequence = line.Sequence, Role = line.Role, EntryType = line.EntryType,
            Content = line.Content, ToolName = line.ToolName, RollSides = line.RollSides,
            GmNoteReferences = line.GmNoteReferences?.ToList(),
            SourceReferences = line.SourceReferences?.ToList(),
            ImageAttachmentKey = line.ImageAttachmentKey, GmMark = line.GmMark, CreatedAt = line.CreatedAt, CompletedAt = line.CompletedAt,
            NarrationEffectSpans = line.NarrationEffectSpans?.ToList(),
            PresentationSegments = line.PresentationSegments?.Select(segment => new GmPresentationSegment
            {
                Id = segment.Id, Speaker = segment.Speaker, SpeakerNpcId = segment.SpeakerNpcId,
                Text = segment.Text, Flavor = segment.Flavor, GlitchSeed = segment.GlitchSeed,
                NarrationEffectSpans = segment.NarrationEffectSpans?.ToList()
            }).ToList()
        }).ToArray() ?? []);
}
