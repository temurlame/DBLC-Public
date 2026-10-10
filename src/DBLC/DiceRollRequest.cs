namespace DBLC;

public sealed record DiceRollRequest(Guid Id, Guid ConversationId, int Sides, int Result);
