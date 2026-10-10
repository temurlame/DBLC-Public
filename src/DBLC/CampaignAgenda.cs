using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DBLC;

public sealed class CampaignClock
{
    // Absolute fictional minutes since Day 1 at 00:00. Starts at Day 1, 08:00.
    public long TotalMinutes { get; set; } = 8 * 60;
}

public sealed class AgendaEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public long DueMinute { get; set; }
    public string Visibility { get; set; } = "gm";
    public string Status { get; set; } = "upcoming";
    public long? ReachedAtMinute { get; set; }
    public long? ResolvedAtMinute { get; set; }
}

public sealed class CampaignAgendaTools
{
    public required Func<JsonObject, string> Read { get; init; }
    public required Func<JsonObject, string> Advance { get; init; }
    public required Func<JsonObject, string> SetTime { get; init; }
    public required Func<JsonObject, string> AddEvent { get; init; }
    public required Func<JsonObject, string> EditEvent { get; init; }
    public required Func<JsonObject, string> ResolveEvent { get; init; }
    public required Func<JsonObject, string> RemoveEvent { get; init; }
}

public static class CampaignAgendaFormat
{
    public static int Day(long minute) => checked((int)(Math.Max(0, minute) / 1440) + 1);
    public static int Hour(long minute) => (int)(Math.Max(0, minute) % 1440 / 60);
    public static int Minute(long minute) => (int)(Math.Max(0, minute) % 60);

    public static string Daypart(long minute) => Hour(minute) switch
    {
        >= 0 and < 5 => "Late Night",
        >= 5 and < 7 => "Dawn",
        >= 7 and < 12 => "Morning",
        >= 12 and < 17 => "Afternoon",
        >= 17 and < 20 => "Evening",
        >= 20 and < 23 => "Late Evening",
        _ => "Night"
    };

    public static string ClockTime(long minute)
    {
        var hour = Hour(minute);
        var suffix = hour < 12 ? "AM" : "PM";
        var displayHour = hour % 12;
        if (displayHour == 0) displayHour = 12;
        return $"{displayHour}:{Minute(minute):00} {suffix}";
    }

    public static string Now(long minute) => $"Day {Day(minute)}\n{Daypart(minute)} · {ClockTime(minute)}";

    public static string EventDate(long minute, long now) => EventDate(minute, now, true);

    public static string EventDate(long minute, long now, bool universalTimeEnabled)
    {
        var offset = Day(minute) - Day(now);
        var day = offset switch { 0 => "Today", 1 => "Tomorrow", _ => $"Day {Day(minute)}" };
        return universalTimeEnabled ? $"{day} · {Daypart(minute)}" : day;
    }

    public static string Moment(long minute) => $"Day {Day(minute)} · {Daypart(minute)} ({ClockTime(minute)})";

    public static bool TryDaypart(string? value, out int hour)
    {
        hour = (value ?? "").Trim().ToLowerInvariant() switch
        {
            "late night" => 2,
            "dawn" => 6,
            "morning" => 9,
            "afternoon" => 14,
            "evening" => 18,
            "late evening" => 22,
            "night" => 23,
            _ => -1
        };
        return hour >= 0;
    }
}

public sealed class CampaignAgendaAuditStore
{
    private readonly string _root = AppDataPaths.File("campaigns");

    public void Append(Guid campaignId, string action)
    {
        var folder = Path.Combine(_root, campaignId.ToString("N"));
        Directory.CreateDirectory(folder);
        var record = JsonSerializer.Serialize(new { recorded_at = DateTimeOffset.Now, campaign_id = campaignId, action });
        File.AppendAllText(Path.Combine(folder, "temporal-audit.jsonl"), record + Environment.NewLine);
    }
}
