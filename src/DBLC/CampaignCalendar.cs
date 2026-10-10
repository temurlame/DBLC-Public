using System.Globalization;

namespace DBLC;

public sealed class CampaignDateTimeSettings
{
    public bool UniversalTimeEnabled { get; set; } = true;
    public string CalendarMode { get; set; } = "DayCount";
    public DateTime GregorianAnchorDate { get; set; } = new(2013, 1, 1);
    public long GregorianAnchorCampaignDay { get; set; } = 1;
    public string CustomWeekdays { get; set; } = "Firstday\nSecondday\nThirdday\nFourthday\nFifthday\nSixthday\nSeventhday";
    public string CustomPeriods { get; set; } = "Firstmonth, 30\nSecondmonth, 27\nThirdmonth, 31";
    public string CustomAnchorWeekday { get; set; } = "Thirdday";
    public int CustomAnchorDay { get; set; } = 1;
    public string CustomAnchorPeriod { get; set; } = "Firstmonth";
    public int CustomAnchorYear { get; set; } = 321;
    public string CustomEra { get; set; } = "Fourth Era";
    public long CustomAnchorCampaignDay { get; set; } = 1;

    public static string NormalizeMode(string? mode) => mode?.Trim() switch
    {
        "Gregorian" => "Gregorian",
        "Custom" => "Custom",
        _ => "DayCount"
    };
}

public sealed record CustomCalendarPeriod(string Name, int Days);
public sealed record CustomCalendarDate(string Weekday, string Period, int Day, long Year, string Era);
public sealed record CalendarMonthPage(string Title, IReadOnlyList<string> Weekdays, int Days, int FirstWeekdayIndex, long FirstCampaignDay)
{
    public bool ContainsCampaignDay(long day) => day >= FirstCampaignDay && day < FirstCampaignDay + Days;
}

public static class CampaignCalendar
{
    public static string GmMoment(CampaignDateTimeSettings? settings, long minute)
    {
        settings ??= new CampaignDateTimeSettings();
        var moment = Moment(settings, minute);
        if (CampaignDateTimeSettings.NormalizeMode(settings.CalendarMode) != "Gregorian") return moment;
        var campaignDay = CampaignAgendaFormat.Day(minute);
        if (!TryGetMonthPage(settings, campaignDay, 0, out var page) || page is null) return moment;
        var weekdayIndex = (int)PositiveModulo(page.FirstWeekdayIndex + campaignDay - page.FirstCampaignDay, page.Weekdays.Count);
        return $"{page.Weekdays[weekdayIndex]} · {moment}";
    }

    public static bool TryResolveCampaignDay(CampaignDateTimeSettings? settings, int currentCampaignDay, string month, int dayOfMonth, long? year, out int campaignDay, out string error)
    {
        campaignDay = 0;
        error = "The configured campaign calendar is invalid or the requested date does not exist.";
        if (settings is null || currentCampaignDay < 1 || dayOfMonth < 1) return false;
        try
        {
            var mode = CampaignDateTimeSettings.NormalizeMode(settings.CalendarMode);
            if (mode == "Gregorian")
            {
                var anchor = DateOnly.FromDateTime(settings.GregorianAnchorDate);
                var currentDate = anchor.AddDays(checked((int)(currentCampaignDay - settings.GregorianAnchorCampaignDay)));
                if (!DateTime.TryParseExact(month.Trim(), ["MMMM", "MMM"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedMonth))
                {
                    error = "Give a Gregorian month name such as March or Mar.";
                    return false;
                }
                var targetDate = new DateOnly(checked((int)(year ?? currentDate.Year)), parsedMonth.Month, dayOfMonth);
                campaignDay = checked((int)(settings.GregorianAnchorCampaignDay + targetDate.DayNumber - anchor.DayNumber));
                if (campaignDay < 1) { error = "That date is before Campaign Day 1."; return false; }
                return true;
            }
            if (mode != "Custom"
                || !TryParseNames(settings.CustomWeekdays, 32, out var weekdays, out _)
                || !TryParsePeriods(settings.CustomPeriods, out var periods, out _)
                || !TryGetCustomDate(settings, currentCampaignDay, out var currentDateCustom)) return false;
            var periodIndex = periods.FindIndex(period => string.Equals(period.Name, month.Trim(), StringComparison.OrdinalIgnoreCase));
            if (periodIndex < 0)
            {
                error = "That month is not in the configured campaign calendar.";
                return false;
            }
            var period = periods[periodIndex];
            if (dayOfMonth > period.Days)
            {
                error = $"{period.Name} has {period.Days} days in the configured campaign calendar.";
                return false;
            }
            var targetYear = year ?? currentDateCustom.Year;
            if (targetYear is < 0 or > 2_000_000) { error = "The requested calendar year is outside the supported range."; return false; }
            var anchorPeriodIndex = periods.FindIndex(item => string.Equals(item.Name, settings.CustomAnchorPeriod.Trim(), StringComparison.OrdinalIgnoreCase));
            var anchorWeekdayIndex = weekdays.FindIndex(item => string.Equals(item, settings.CustomAnchorWeekday.Trim(), StringComparison.OrdinalIgnoreCase));
            if (anchorPeriodIndex < 0 || anchorWeekdayIndex < 0 || settings.CustomAnchorDay < 1 || settings.CustomAnchorDay > periods[anchorPeriodIndex].Days) return false;
            var yearLength = periods.Sum(item => (long)item.Days);
            var anchorOrdinal = checked((settings.CustomAnchorYear - 1L) * yearLength + periods.Take(anchorPeriodIndex).Sum(item => (long)item.Days) + settings.CustomAnchorDay - 1L);
            var targetOrdinal = checked((targetYear - 1L) * yearLength + periods.Take(periodIndex).Sum(item => (long)item.Days) + dayOfMonth - 1L);
            var targetCampaignDay = checked(settings.CustomAnchorCampaignDay + targetOrdinal - anchorOrdinal);
            if (targetCampaignDay is < 1 or > int.MaxValue) { error = "That date is outside the supported campaign day range."; return false; }
            campaignDay = (int)targetCampaignDay;
            return true;
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
        {
            error = "That date is outside the supported campaign calendar.";
            campaignDay = 0;
            return false;
        }
    }

    public static bool TryGetMonthPage(CampaignDateTimeSettings? settings, int currentCampaignDay, int monthOffset, out CalendarMonthPage? page)
    {
        page = null;
        if (settings is null || currentCampaignDay < 1) return false;
        try
        {
            if (CampaignDateTimeSettings.NormalizeMode(settings.CalendarMode) == "Gregorian")
            {
                var anchor = DateOnly.FromDateTime(settings.GregorianAnchorDate);
                var today = anchor.AddDays(checked(currentCampaignDay - (int)settings.GregorianAnchorCampaignDay));
                var first = new DateOnly(today.Year, today.Month, 1).AddMonths(monthOffset);
                var weekdays = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
                page = new CalendarMonthPage(first.ToString("MMMM yyyy", CultureInfo.InvariantCulture), weekdays,
                    DateTime.DaysInMonth(first.Year, first.Month), ((int)first.DayOfWeek + 6) % 7,
                    checked(settings.GregorianAnchorCampaignDay + first.DayNumber - anchor.DayNumber));
                return true;
            }
            if (CampaignDateTimeSettings.NormalizeMode(settings.CalendarMode) != "Custom"
                || !TryGetCustomDate(settings, currentCampaignDay, out var todayDate)
                || !TryParseNames(settings.CustomWeekdays, 32, out var weekdaysCustom, out _)
                || !TryParsePeriods(settings.CustomPeriods, out var periods, out _)) return false;
            var currentIndex = periods.FindIndex(period => period.Name == todayDate.Period);
            var targetOrdinal = checked((todayDate.Year - 1) * periods.Count + currentIndex + monthOffset);
            var targetYearIndex = FloorDivide(targetOrdinal, periods.Count);
            var targetIndex = (int)PositiveModulo(targetOrdinal, periods.Count);
            var yearLength = periods.Sum(period => (long)period.Days);
            var currentPrefix = periods.Take(currentIndex).Sum(period => (long)period.Days);
            var targetPrefix = periods.Take(targetIndex).Sum(period => (long)period.Days);
            var firstCampaignDay = checked(currentCampaignDay - todayDate.Day + 1L
                + checked((targetYearIndex - (todayDate.Year - 1)) * yearLength) + targetPrefix - currentPrefix);
            var weekdayAnchor = weekdaysCustom.FindIndex(name => string.Equals(name, settings.CustomAnchorWeekday.Trim(), StringComparison.OrdinalIgnoreCase));
            var firstWeekday = (int)PositiveModulo(weekdayAnchor + firstCampaignDay - settings.CustomAnchorCampaignDay, weekdaysCustom.Count);
            var year = targetYearIndex + 1;
            var title = string.IsNullOrWhiteSpace(settings.CustomEra) ? $"{periods[targetIndex].Name} · Year {year}" : $"{periods[targetIndex].Name} · {settings.CustomEra.Trim()} {year}";
            page = new CalendarMonthPage(title, weekdaysCustom, periods[targetIndex].Days, firstWeekday, firstCampaignDay);
            return true;
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException) { return false; }
    }

    public static bool TryValidate(CampaignDateTimeSettings settings, out string error)
    {
        error = "";
        var mode = CampaignDateTimeSettings.NormalizeMode(settings.CalendarMode);
        if (mode == "Gregorian")
        {
            if (settings.GregorianAnchorDate.Year is < 1 or > 9999)
            {
                error = "Choose a valid Gregorian date.";
                return false;
            }
            return true;
        }
        if (mode != "Custom") return true;
        if (!TryParseNames(settings.CustomWeekdays, 32, out var weekdays, out error)) return false;
        if (!TryParsePeriods(settings.CustomPeriods, out var periods, out error)) return false;
        if (!weekdays.Contains(settings.CustomAnchorWeekday.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            error = "Choose a current weekday from the configured weekday list.";
            return false;
        }
        var anchorPeriod = periods.FirstOrDefault(period => string.Equals(period.Name, settings.CustomAnchorPeriod.Trim(), StringComparison.OrdinalIgnoreCase));
        if (anchorPeriod is null)
        {
            error = "Choose a current month or period from the configured list.";
            return false;
        }
        if (settings.CustomAnchorDay < 1 || settings.CustomAnchorDay > anchorPeriod.Days)
        {
            error = $"The current day must be between 1 and {anchorPeriod.Days} for {anchorPeriod.Name}.";
            return false;
        }
        if (settings.CustomAnchorYear is < 0 or > 2_000_000)
        {
            error = "The custom year must be between 0 and 2,000,000.";
            return false;
        }
        if (settings.CustomEra.Trim().Length > 60)
        {
            error = "Era text must be 60 characters or fewer.";
            return false;
        }
        return true;
    }

    public static string DateLabel(CampaignDateTimeSettings? settings, long minute)
    {
        settings ??= new CampaignDateTimeSettings();
        var day = CampaignAgendaFormat.Day(minute);
        var mode = CampaignDateTimeSettings.NormalizeMode(settings.CalendarMode);
        if (mode == "Gregorian")
        {
            try
            {
                var offset = (long)day - Math.Clamp(settings.GregorianAnchorCampaignDay, 1, int.MaxValue);
                return DateOnly.FromDateTime(settings.GregorianAnchorDate).AddDays(checked((int)offset)).ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
            }
            catch (ArgumentOutOfRangeException) { }
        }
        if (mode == "Custom" && TryGetCustomDate(settings, day, out var customDate))
        {
            var year = string.IsNullOrWhiteSpace(customDate.Era) ? $"Year {customDate.Year}" : $"{customDate.Era} {customDate.Year}";
            return $"{customDate.Weekday}, {customDate.Period} {customDate.Day}, {year}";
        }
        return $"Day {day}";
    }

    public static string Now(CampaignDateTimeSettings? settings, long minute) => settings?.UniversalTimeEnabled == false
        ? DateLabel(settings, minute)
        : $"{DateLabel(settings, minute)}\n{CampaignAgendaFormat.Daypart(minute)} · {CampaignAgendaFormat.ClockTime(minute)}";

    public static string Moment(CampaignDateTimeSettings? settings, long minute)
    {
        var display = settings?.UniversalTimeEnabled == false
            ? DateLabel(settings, minute)
            : $"{DateLabel(settings, minute)} · {CampaignAgendaFormat.Daypart(minute)} ({CampaignAgendaFormat.ClockTime(minute)})";
        return $"{display} · Campaign Day {CampaignAgendaFormat.Day(minute)}";
    }

    public static string EventDate(CampaignDateTimeSettings? settings, long minute, long now)
    {
        if (CampaignDateTimeSettings.NormalizeMode(settings?.CalendarMode) == "DayCount")
            return CampaignAgendaFormat.EventDate(minute, now, settings?.UniversalTimeEnabled != false);
        var offset = CampaignAgendaFormat.Day(minute) - CampaignAgendaFormat.Day(now);
        var date = DateLabel(settings, minute);
        var prefix = offset switch { 0 => "Today · ", 1 => "Tomorrow · ", _ => "" };
        return settings?.UniversalTimeEnabled == false
            ? prefix + date
            : $"{prefix}{date} · {CampaignAgendaFormat.Daypart(minute)}";
    }

    public static bool TryParseNames(string? text, int maximum, out List<string> names, out string error)
    {
        names = [];
        error = "";
        if ((text?.Length ?? 0) > 2000)
        {
            error = "Keep the weekday list under 2,000 characters.";
            return false;
        }
        foreach (var line in (text ?? "").Split('\n'))
        {
            var name = line.Trim().TrimEnd('\r');
            if (name.Length == 0 || name.StartsWith('#')) continue;
            if (name.Length > 40)
            {
                error = "Weekday names must be 40 characters or fewer.";
                return false;
            }
            if (names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                error = $"The weekday name “{name}” appears more than once.";
                return false;
            }
            names.Add(name);
        }
        if (names.Count is < 1 || names.Count > maximum)
        {
            error = $"Enter between 1 and {maximum} weekday names, one per line.";
            return false;
        }
        return true;
    }

    public static bool TryParsePeriods(string? text, out List<CustomCalendarPeriod> periods, out string error)
    {
        periods = [];
        error = "";
        if ((text?.Length ?? 0) > 3000)
        {
            error = "Keep the month and period list under 3,000 characters.";
            return false;
        }
        foreach (var line in (text ?? "").Split('\n'))
        {
            var value = line.Trim().TrimEnd('\r');
            if (value.Length == 0 || value.StartsWith('#')) continue;
            var separator = value.LastIndexOf(',');
            if (separator < 1) separator = value.LastIndexOf('|');
            if (separator < 1 || !int.TryParse(value[(separator + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var days))
            {
                error = "Enter each month or period as Name, number of days (for example, Firstmonth, 30).";
                return false;
            }
            var name = value[..separator].Trim();
            if (name.Length is < 1 or > 40 || days is < 1 or > 1000)
            {
                error = "Month names must be 1–40 characters and period lengths must be 1–1,000 days.";
                return false;
            }
            if (periods.Any(period => string.Equals(period.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                error = $"The month or period “{name}” appears more than once.";
                return false;
            }
            periods.Add(new CustomCalendarPeriod(name, days));
        }
        if (periods.Count is < 1 or > 36)
        {
            error = "Enter between 1 and 36 months or periods, one per line.";
            return false;
        }
        return true;
    }

    private static bool TryGetCustomDate(CampaignDateTimeSettings settings, int campaignDay, out CustomCalendarDate date)
    {
        date = new CustomCalendarDate("", "", 1, 0, "");
        if (!TryParseNames(settings.CustomWeekdays, 32, out var weekdays, out _) || !TryParsePeriods(settings.CustomPeriods, out var periods, out _)) return false;
        var weekdayAnchor = weekdays.FindIndex(name => string.Equals(name, settings.CustomAnchorWeekday.Trim(), StringComparison.OrdinalIgnoreCase));
        var periodAnchor = periods.FindIndex(period => string.Equals(period.Name, settings.CustomAnchorPeriod.Trim(), StringComparison.OrdinalIgnoreCase));
        if (weekdayAnchor < 0 || periodAnchor < 0 || settings.CustomAnchorDay < 1 || settings.CustomAnchorDay > periods[periodAnchor].Days) return false;
        var yearLength = periods.Sum(period => (long)period.Days);
        var beforeAnchorPeriod = periods.Take(periodAnchor).Sum(period => (long)period.Days);
        var anchorOrdinal = checked((settings.CustomAnchorYear - 1L) * yearLength + beforeAnchorPeriod + settings.CustomAnchorDay - 1L);
        var delta = (long)campaignDay - Math.Clamp(settings.CustomAnchorCampaignDay, 1, int.MaxValue);
        var ordinal = checked(anchorOrdinal + delta);
        var yearIndex = FloorDivide(ordinal, yearLength);
        var dayInYear = PositiveModulo(ordinal, yearLength);
        var periodIndex = 0;
        while (periodIndex < periods.Count - 1 && dayInYear >= periods[periodIndex].Days)
        {
            dayInYear -= periods[periodIndex].Days;
            periodIndex++;
        }
        var weekdayIndex = (int)PositiveModulo(weekdayAnchor + delta, weekdays.Count);
        var displayYear = yearIndex + 1;
        date = new CustomCalendarDate(weekdays[weekdayIndex], periods[periodIndex].Name, (int)dayInYear + 1, displayYear, settings.CustomEra.Trim());
        return true;
    }

    private static long FloorDivide(long value, long divisor)
    {
        var quotient = value / divisor;
        var remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    private static long PositiveModulo(long value, long divisor) => ((value % divisor) + divisor) % divisor;
}
