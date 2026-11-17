namespace Guildhall.AgentHost.Scheduling;

public sealed class CronSchedule
{
    private readonly bool[] _minutes; // 0-59
    private readonly bool[] _hours;   // 0-23
    private readonly bool[] _days;    // 1-31
    private readonly bool[] _months;  // 1-12
    private readonly bool[] _daysOfWeek; // 0-6 (Sunday = 0)
    private readonly bool _domWildcard;
    private readonly bool _dowWildcard;

    internal CronSchedule(
        bool[] minutes,
        bool[] hours,
        bool[] days,
        bool[] months,
        bool[] daysOfWeek,
        bool domWildcard,
        bool dowWildcard)
    {
        _minutes = minutes;
        _hours = hours;
        _days = days;
        _months = months;
        _daysOfWeek = daysOfWeek;
        _domWildcard = domWildcard;
        _dowWildcard = dowWildcard;
    }

    public bool Matches(DateTime localTime)
    {
        if (!_minutes[localTime.Minute] || !_hours[localTime.Hour] || !_months[localTime.Month])
            return false;

        var domMatch = _days[localTime.Day];
        var dowMatch = _daysOfWeek[(int)localTime.DayOfWeek];

        if (_domWildcard && _dowWildcard) return true;
        if (_domWildcard) return dowMatch;
        if (_dowWildcard) return domMatch;
        return domMatch || dowMatch;
    }

    public DateTime? GetNextOccurrenceUtc(DateTime afterUtc, TimeZoneInfo timezone)
    {
        var list = EnumerateOccurrencesUtc(afterUtc, afterUtc.AddYears(2), timezone, 1);
        return list.Count > 0 ? list[0] : null;
    }

    public IReadOnlyList<DateTime> EnumerateOccurrencesUtc(
        DateTime fromExclusiveUtc,
        DateTime toInclusiveUtc,
        TimeZoneInfo timezone,
        int maxCount)
    {
        if (maxCount <= 0 || toInclusiveUtc <= fromExclusiveUtc)
            return [];

        var utcFrom = DateTime.SpecifyKind(fromExclusiveUtc, DateTimeKind.Utc);
        var utcTo = DateTime.SpecifyKind(toInclusiveUtc, DateTimeKind.Utc);

        // Start at the next whole minute after fromExclusiveUtc
        var cursorUtc = new DateTime(utcFrom.Year, utcFrom.Month, utcFrom.Day, utcFrom.Hour, utcFrom.Minute, 0, DateTimeKind.Utc).AddMinutes(1);
        var results = new List<DateTime>(Math.Min(maxCount, 16));

        // Guard against impossible dates like Feb 31 (max ~2 years = ~1.05M minutes, fast skip by day/hour)
        while (cursorUtc <= utcTo && results.Count < maxCount)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(cursorUtc, timezone);
            if (!_months[local.Month])
            {
                cursorUtc = cursorUtc.AddDays(1);
                cursorUtc = new DateTime(cursorUtc.Year, cursorUtc.Month, cursorUtc.Day, 0, 0, 0, DateTimeKind.Utc);
                continue;
            }

            if (Matches(local))
            {
                results.Add(cursorUtc);
            }

            cursorUtc = cursorUtc.AddMinutes(1);
        }

        return results;
    }
}

public static class CronScheduleParser
{
    public static bool TryParse(string? expression, out CronSchedule? schedule, out string? error)
    {
        schedule = null;
        error = null;

        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "Cron expression is required.";
            return false;
        }

        var normalized = expression.Trim().ToLowerInvariant() switch
        {
            "@hourly" => "0 * * * *",
            "@daily" => "0 0 * * *",
            "@weekly" => "0 0 * * 0",
            "@every_15m" => "*/15 * * * *",
            "@every_30m" => "*/30 * * * *",
            var raw => raw
        };

        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 5)
        {
            error = "Cron expression must have 5 fields (minute hour day-of-month month day-of-week) or a preset (@hourly, @daily, @weekly).";
            return false;
        }

        if (!TryParseField(parts[0], 0, 59, out var minutes, out error) ||
            !TryParseField(parts[1], 0, 23, out var hours, out error) ||
            !TryParseField(parts[2], 1, 31, out var days, out error) ||
            !TryParseField(parts[3], 1, 12, out var months, out error) ||
            !TryParseField(parts[4], 0, 7, out var rawDow, out error))
        {
            return false;
        }

        // Normalize Sunday=7 -> 0
        var daysOfWeek = new bool[7];
        for (var i = 0; i < 7; i++)
            daysOfWeek[i] = rawDow![i];
        if (rawDow![7])
            daysOfWeek[0] = true;

        schedule = new CronSchedule(
            minutes!,
            hours!,
            days!,
            months!,
            daysOfWeek,
            parts[2] == "*",
            parts[4] == "*");
        return true;
    }

    private static bool TryParseField(string field, int min, int max, out bool[]? values, out string? error)
    {
        values = new bool[max + 1];
        error = null;

        foreach (var segment in field.Split(',', StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrEmpty(segment))
            {
                error = $"Invalid empty segment in cron field '{field}'.";
                return false;
            }

            var slashIdx = segment.IndexOf('/');
            var rangePart = slashIdx >= 0 ? segment[..slashIdx] : segment;
            var step = 1;

            if (slashIdx >= 0)
            {
                if (!int.TryParse(segment[(slashIdx + 1)..], out step) || step <= 0)
                {
                    error = $"Invalid step value in cron field '{segment}'.";
                    return false;
                }
            }

            int start;
            int end;
            if (rangePart == "*")
            {
                start = min;
                end = max;
            }
            else if (rangePart.Contains('-'))
            {
                var bounds = rangePart.Split('-');
                if (bounds.Length != 2 || !int.TryParse(bounds[0], out start) || !int.TryParse(bounds[1], out end))
                {
                    error = $"Invalid range '{rangePart}'.";
                    return false;
                }
            }
            else
            {
                if (!int.TryParse(rangePart, out start))
                {
                    error = $"Invalid cron value '{rangePart}'.";
                    return false;
                }
                end = slashIdx >= 0 ? max : start;
            }

            if (start < min || end > max || start > end)
            {
                error = $"Cron value '{segment}' is out of bounds [{min}..{max}].";
                return false;
            }

            for (var i = start; i <= end; i += step)
                values[i] = true;
        }

        return true;
    }
}
