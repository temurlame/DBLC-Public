namespace DBLC;

public sealed class CampaignTrigger
{
    public string Name { get; set; } = "";
    public string Prompt { get; set; } = "";
    public double Weight { get; set; } = 1;
}

public sealed class SurpriseMachineData
{
    public bool Enabled { get; set; }
    public int EligibilityMinimum { get; set; } = 5;
    public int EligibilityMaximum { get; set; } = 10;
    public double EventProbabilityMinimumPercent { get; set; } = 12;
    public double EventProbabilityMaximumPercent { get; set; } = 25;
    public int InterruptionDurationMinimum { get; set; } = 3;
    public int InterruptionDurationMaximum { get; set; } = 6;
    public int CooldownMinimum { get; set; } = 2;
    public int CooldownMaximum { get; set; } = 4;
    public double CampaignTriggerChancePercent { get; set; } = 10;
    public double PreviousSourceMultiplier { get; set; } = 0.35;
    public double RecentPairMultiplier { get; set; } = 0.10;
    public int PairHistoryLength { get; set; } = 3;

    public string Phase { get; set; } = "QUIET";
    public int BeatCount { get; set; }
    public int EligibleAfter { get; set; }
    public double EventProbability { get; set; }
    public int InterruptionLeaseRemaining { get; set; }
    public int CooldownRemaining { get; set; }
    public string? PreviousSource { get; set; }
    public List<RecentInterruptionPair> RecentPairs { get; set; } = [];
    public Dictionary<string, double> MagnitudeWeights { get; set; } = SurpriseDefaults.MagnitudeWeights();
    public Dictionary<string, double> SourceWeights { get; set; } = SurpriseDefaults.SourceWeights();
    public Dictionary<string, double> ChangeWeights { get; set; } = SurpriseDefaults.ChangeWeights();
    public Dictionary<string, double> BehaviorWeights { get; set; } = SurpriseDefaults.BehaviorWeights();
    public Dictionary<string, double> RelationWeights { get; set; } = SurpriseDefaults.RelationWeights();
    public List<CampaignTrigger> CampaignTriggers { get; set; } = [];

    public void ResetMechanicalDefaults()
    {
        EligibilityMinimum = 5;
        EligibilityMaximum = 10;
        EventProbabilityMinimumPercent = 12;
        EventProbabilityMaximumPercent = 25;
        InterruptionDurationMinimum = 3;
        InterruptionDurationMaximum = 6;
        CooldownMinimum = 2;
        CooldownMaximum = 4;
        CampaignTriggerChancePercent = 10;
        PreviousSourceMultiplier = 0.35;
        RecentPairMultiplier = 0.10;
        PairHistoryLength = 3;
        MagnitudeWeights = SurpriseDefaults.MagnitudeWeights();
        SourceWeights = SurpriseDefaults.SourceWeights();
        ChangeWeights = SurpriseDefaults.ChangeWeights();
        BehaviorWeights = SurpriseDefaults.BehaviorWeights();
        RelationWeights = SurpriseDefaults.RelationWeights();
    }
}

public sealed class RecentInterruptionPair
{
    public string Source { get; set; } = "";
    public string Change { get; set; } = "";
}

public static class SurpriseDefaults
{
    public static readonly string[] Magnitudes = ["AMBIENT", "MINOR", "MAJOR", "WILD"];
    public static readonly string[] Sources = ["PERSON", "GROUP", "AUTHORITY", "CRIMINAL", "ANIMAL", "CREATURE", "TRANSPORT", "OBJECT", "STRUCTURE", "ENVIRONMENT", "WEATHER", "HAZARD", "SUPERNATURAL", "PHYSICAL_CONDITION", "SOCIAL_ACTIVITY"];
    public static readonly string[] Changes = ["ARRIVES", "DEPARTS", "ENTERS", "ESCAPES", "PASSES_THROUGH", "PURSUES", "FLEES", "ATTACKS", "CONFRONTS", "DEMANDS", "COLLIDES", "FALLS", "BREAKS", "COLLAPSES", "SPILLS", "IGNITES", "EXTINGUISHES", "BLOCKS", "OPENS", "REVEALS", "BECOMES_LOST", "MISIDENTIFIES", "FAILS", "INTERRUPTS", "TRANSFORMS", "WORSENS"];
    public static readonly string[] Behaviors = ["HOSTILE", "PANICKED", "CONFUSED", "DESPERATE", "AGITATED", "HELPFUL", "INDIFFERENT", "OBLIVIOUS", "MISTAKEN", "RECKLESS", "PURPOSEFUL", "OUT_OF_CONTROL"];
    public static readonly string[] Relations = ["UNRELATED", "INCIDENTAL", "WORLD_CONSEQUENCE"];
    public static readonly HashSet<string> BehavioralSources = ["PERSON", "GROUP", "AUTHORITY", "CRIMINAL", "ANIMAL", "CREATURE"];

    public static Dictionary<string, double> MagnitudeWeights() => new() { ["AMBIENT"] = 25, ["MINOR"] = 40, ["MAJOR"] = 27, ["WILD"] = 8 };
    public static Dictionary<string, double> SourceWeights() => Sources.ToDictionary(value => value, _ => 1d);
    public static Dictionary<string, double> ChangeWeights() => Changes.ToDictionary(value => value, _ => 1d);
    public static Dictionary<string, double> BehaviorWeights() => Behaviors.ToDictionary(value => value, _ => 1d);
    public static Dictionary<string, double> RelationWeights() => new() { ["UNRELATED"] = 70, ["INCIDENTAL"] = 25, ["WORLD_CONSEQUENCE"] = 5 };

    public static bool IsValid(SurpriseMachineData data, out string error)
    {
        if (data.EligibilityMinimum < 1 || data.EligibilityMinimum > data.EligibilityMaximum) error = "Eligibility minimum must be at least 1 and no greater than its maximum.";
        else if (!ProbabilityRangeValid(data.EventProbabilityMinimumPercent, data.EventProbabilityMaximumPercent)) error = "Event probability must be between 0 and 100%, with minimum no greater than maximum.";
        else if (data.InterruptionDurationMinimum < 1 || data.InterruptionDurationMinimum > data.InterruptionDurationMaximum) error = "Post-surprise event suppression must be at least 1 beat, with minimum no greater than maximum.";
        else if (data.CooldownMinimum < 0 || data.CooldownMinimum > data.CooldownMaximum) error = "Cooldown must be 0 or more beats, with minimum no greater than maximum.";
        else if (!double.IsFinite(data.CampaignTriggerChancePercent) || data.CampaignTriggerChancePercent is < 0 or > 100) error = "Campaign Trigger chance must be between 0 and 100%.";
        else if (!double.IsFinite(data.PreviousSourceMultiplier) || data.PreviousSourceMultiplier <= 0 || !double.IsFinite(data.RecentPairMultiplier) || data.RecentPairMultiplier <= 0) error = "Repetition multipliers must be greater than zero so repeats remain possible.";
        else if (data.PairHistoryLength < 1 || data.PairHistoryLength > 20) error = "Pair history length must be between 1 and 20.";
        else if (!WeightsValid(data.MagnitudeWeights, Magnitudes) || !WeightsValid(data.SourceWeights, Sources) || !WeightsValid(data.ChangeWeights, Changes) || !WeightsValid(data.BehaviorWeights, Behaviors) || !WeightsValid(data.RelationWeights, Relations)) error = "Weights cannot be negative, and each aspect needs at least one positive weight.";
        else if (!new[] { "STRUCTURE", "OBJECT", "WEATHER" }.All(source => Changes.Any(change => !new[] { "DEMANDS", "MISIDENTIFIES" }.Contains(change) && data.ChangeWeights.TryGetValue(change, out var weight) && weight > 0))) error = "Give at least one non-excluded Change a positive weight; STRUCTURE, OBJECT, and WEATHER cannot demand or misidentify.";
        else if (data.CampaignTriggers is null) error = "Campaign Triggers could not be read. Remove and recreate the campaign trigger list.";
        else if (data.CampaignTriggers.Any(trigger => string.IsNullOrWhiteSpace(trigger.Name) || string.IsNullOrWhiteSpace(trigger.Prompt) || !double.IsFinite(trigger.Weight) || trigger.Weight <= 0)) error = "Each Campaign Trigger needs a name, prompt, and positive weight.";
        else { error = ""; return true; }
        return false;
    }

    private static bool ProbabilityRangeValid(double min, double max) => double.IsFinite(min) && double.IsFinite(max) && min is >= 0 and <= 100 && max is >= 0 and <= 100 && min <= max;
    private static bool WeightsValid(Dictionary<string, double>? weights, IEnumerable<string> keys) => weights is not null && keys.All(key => weights.TryGetValue(key, out var weight) && double.IsFinite(weight) && weight >= 0) && keys.Any(key => weights.TryGetValue(key, out var weight) && weight > 0);
}

public static class SurpriseMachine
{
    public static string? Advance(SurpriseMachineData data)
    {
        if (!data.Enabled || !SurpriseDefaults.IsValid(data, out _)) return null;
        data.RecentPairs ??= [];
        EnsureCycleValues(data);
        if (data.Phase == "INTERRUPTED")
        {
            data.InterruptionLeaseRemaining = Math.Max(0, data.InterruptionLeaseRemaining - 1);
            if (data.InterruptionLeaseRemaining == 0)
            {
                data.Phase = "COOLDOWN";
                if (data.CooldownRemaining == 0) StartQuietCycle(data);
            }
            return null;
        }
        if (data.Phase == "COOLDOWN")
        {
            data.CooldownRemaining = Math.Max(0, data.CooldownRemaining - 1);
            if (data.CooldownRemaining == 0) StartQuietCycle(data);
            return null;
        }

        data.BeatCount++;
        if (data.BeatCount < data.EligibleAfter) { data.Phase = "QUIET"; return null; }
        data.Phase = "ELIGIBLE";
        if (NextDouble() >= data.EventProbability) return null;

        var magnitude = Choose(data.MagnitudeWeights);
        var sourceWeights = new Dictionary<string, double>(data.SourceWeights);
        if (data.PreviousSource is { } previous && sourceWeights.ContainsKey(previous)) sourceWeights[previous] *= data.PreviousSourceMultiplier;
        var source = Choose(sourceWeights);
        var changeWeights = new Dictionary<string, double>(data.ChangeWeights);
        foreach (var candidateChange in SurpriseDefaults.Changes)
        {
            if (data.RecentPairs.TakeLast(data.PairHistoryLength).Any(pair => pair.Source == source && pair.Change == candidateChange)) changeWeights[candidateChange] *= data.RecentPairMultiplier;
            if ((source is "STRUCTURE" or "OBJECT" or "WEATHER") && (candidateChange is "DEMANDS" or "MISIDENTIFIES")) changeWeights[candidateChange] = 0;
        }
        var change = Choose(changeWeights);
        var lines = new List<string> { "INTERRUPTION", $"MAGNITUDE: {magnitude}", $"SOURCE: {source}", $"CHANGE: {change}" };
        if (SurpriseDefaults.BehavioralSources.Contains(source)) lines.Add($"BEHAVIOR: {Choose(data.BehaviorWeights)}");
        lines.Add($"RELATION: {Choose(data.RelationWeights)}");
        data.PreviousSource = source;
        data.RecentPairs.Add(new RecentInterruptionPair { Source = source, Change = change });
        if (data.RecentPairs.Count > data.PairHistoryLength) data.RecentPairs.RemoveRange(0, data.RecentPairs.Count - data.PairHistoryLength);

        if (data.CampaignTriggers.Count > 0 && NextDouble() < data.CampaignTriggerChancePercent / 100d)
        {
            var trigger = ChooseTrigger(data.CampaignTriggers);
            lines.Add("");
            lines.Add("CAMPAIGN TRIGGER");
            lines.Add($"NAME: {trigger.Name.Trim()}");
            lines.Add($"PROMPT: {trigger.Prompt.Trim()}");
        }
        data.Phase = "INTERRUPTED";
        data.InterruptionLeaseRemaining = NextInt(data.InterruptionDurationMinimum, data.InterruptionDurationMaximum);
        data.CooldownRemaining = NextInt(data.CooldownMinimum, data.CooldownMaximum);
        return string.Join("\n", lines);
    }

    private static void EnsureCycleValues(SurpriseMachineData data)
    {
        if (data.EligibleAfter > 0 && double.IsFinite(data.EventProbability) && data.EventProbability is >= 0 and <= 1) return;
        data.EligibleAfter = NextInt(data.EligibilityMinimum, data.EligibilityMaximum);
        data.EventProbability = NextDouble(data.EventProbabilityMinimumPercent / 100d, data.EventProbabilityMaximumPercent / 100d);
    }

    private static void StartQuietCycle(SurpriseMachineData data)
    {
        data.Phase = "QUIET";
        data.BeatCount = 0;
        data.EligibleAfter = NextInt(data.EligibilityMinimum, data.EligibilityMaximum);
        data.EventProbability = NextDouble(data.EventProbabilityMinimumPercent / 100d, data.EventProbabilityMaximumPercent / 100d);
        data.InterruptionLeaseRemaining = 0;
        data.CooldownRemaining = 0;
    }

    private static string Choose(Dictionary<string, double> weights)
    {
        var candidates = weights.Where(pair => pair.Value > 0).ToArray();
        var scale = candidates.Max(pair => pair.Value);
        var total = candidates.Sum(pair => pair.Value / scale);
        var marker = NextDouble() * total;
        foreach (var (key, weight) in candidates) { marker -= weight / scale; if (marker < 0) return key; }
        return candidates[^1].Key;
    }

    private static CampaignTrigger ChooseTrigger(List<CampaignTrigger> triggers)
    {
        var scale = triggers.Max(trigger => trigger.Weight);
        var total = triggers.Sum(trigger => trigger.Weight / scale);
        var marker = NextDouble() * total;
        foreach (var trigger in triggers) { marker -= trigger.Weight / scale; if (marker < 0) return trigger; }
        return triggers[^1];
    }

    private static double NextDouble() => System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, int.MaxValue) / (double)int.MaxValue;
    private static double NextDouble(double min, double max) => min + NextDouble() * (max - min);
    private static int NextInt(int min, int max)
    {
        var span = (long)max - min + 1;
        return min + (int)Math.Floor(NextDouble() * span);
    }
}
