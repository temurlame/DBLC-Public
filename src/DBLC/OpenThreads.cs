namespace DBLC;

public sealed class OpenThread
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public string Visibility { get; set; } = "player";
    public long? LastRemindedAtBeat { get; set; }
}

public sealed class OpenThreadReminderSettings
{
    public bool Enabled { get; set; } = true;
    public int MinimumBeats { get; set; } = 8;
    public int MaximumBeats { get; set; } = 12;
}

public sealed class OpenThreadReminderState
{
    public long BeatCount { get; set; }
    public long NextReminderAtBeat { get; set; }
    public int? ScheduledInterval { get; set; }
}

public sealed record OpenThreadReminderPlan(OpenThreadReminderState State, Guid? ThreadId, int? Interval, string? Visibility)
{
    public string? Context { get; init; }
}

public sealed class CampaignOpenThreadTools
{
    public required Func<string> Read { get; init; }
    public required Func<string, string, string> Add { get; init; }
    public required Func<string, string, string?, string> Update { get; init; }
    public required Func<string, string> Resolve { get; init; }
}

public static class OpenThreadReminderScheduler
{
    public const int MaxThreadCharacters = 600;
    public const int MaximumConfigurableInterval = 1000;

    public static bool IsValid(OpenThreadReminderSettings settings, out string error)
    {
        if (settings.MinimumBeats < 1 || settings.MinimumBeats > MaximumConfigurableInterval)
            error = $"Open Threads minimum interval must be between 1 and {MaximumConfigurableInterval} beats.";
        else if (settings.MaximumBeats < settings.MinimumBeats || settings.MaximumBeats > MaximumConfigurableInterval)
            error = $"Open Threads maximum interval must be at least the minimum and no greater than {MaximumConfigurableInterval} beats.";
        else { error = ""; return true; }
        return false;
    }

    public static OpenThreadReminderPlan Prepare(OpenThreadReminderSettings settings, OpenThreadReminderState current, IReadOnlyList<OpenThread> openThreads, Func<int, int, int>? chooseInterval = null)
    {
        var staged = new OpenThreadReminderState
        {
            BeatCount = Math.Max(0, current.BeatCount),
            NextReminderAtBeat = Math.Max(0, current.NextReminderAtBeat),
            ScheduledInterval = current.ScheduledInterval
        };
        if (!settings.Enabled || !IsValid(settings, out _)) return new OpenThreadReminderPlan(staged, null, null, null);

        staged.BeatCount = checked(staged.BeatCount + 1);
        if (openThreads.Count == 0)
        {
            staged.NextReminderAtBeat = 0;
            staged.ScheduledInterval = null;
            return new OpenThreadReminderPlan(staged, null, null, null);
        }

        var choose = chooseInterval ?? RandomInterval;
        if (staged.ScheduledInterval is not { } scheduled || scheduled < settings.MinimumBeats || scheduled > settings.MaximumBeats)
        {
            staged.ScheduledInterval = choose(settings.MinimumBeats, settings.MaximumBeats);
            staged.NextReminderAtBeat = checked(current.BeatCount + staged.ScheduledInterval.Value);
        }

        if (staged.BeatCount < staged.NextReminderAtBeat) return new OpenThreadReminderPlan(staged, null, null, null);

        var selected = openThreads
            .Select((thread, index) => (thread, index))
            .OrderBy(pair => pair.thread.LastRemindedAtBeat ?? long.MinValue)
            .ThenBy(pair => pair.index)
            .First().thread;
        var interval = choose(settings.MinimumBeats, settings.MaximumBeats);
        staged.ScheduledInterval = interval;
        staged.NextReminderAtBeat = checked(staged.BeatCount + interval);
        return new OpenThreadReminderPlan(staged, selected.Id, interval, selected.Visibility)
        {
            Context = "OPEN THREAD REMINDER\nVisibility: " + selected.Visibility + "\n" + selected.Text +
                (selected.Visibility == "gm" ? "\nThis is GM-private knowledge; do not disclose it merely because it was reminded." : "") +
                "\nThis is an unresolved campaign reminder, not an instruction to act on it now. It does not make the matter more urgent, and it is not resolved merely by being mentioned. Continue normally; use it only if naturally relevant, otherwise ignore it for now."
        };
    }

    private static int RandomInterval(int minimum, int maximum) => Random.Shared.Next(minimum, maximum + 1);
}
