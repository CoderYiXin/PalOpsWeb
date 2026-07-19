using System.Globalization;

namespace PalOps.Web.Automation;

public static class AutomationSchedule
{
    public static DateTimeOffset? GetNextRun(string scheduleType, string expression, DateTimeOffset from)
    {
        var type = (scheduleType ?? string.Empty).Trim().ToLowerInvariant();
        var value = (expression ?? string.Empty).Trim();
        return type switch
        {
            "interval" => from.AddMinutes(ParseInterval(value)),
            "daily" => NextDaily(value, from),
            "once" => NextOnce(value, from),
            "manual" => null,
            _ => throw new ArgumentException("ScheduleType 只能是 interval、daily、once 或 manual。")
        };
    }

    public static void Validate(string scheduleType, string expression)
        => _ = GetNextRun(scheduleType, expression, DateTimeOffset.UtcNow);

    private static int ParseInterval(string expression)
    {
        if (!int.TryParse(expression, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) || minutes is < 1 or > 10080)
            throw new ArgumentException("interval 计划必须填写 1 到 10080 分钟。");
        return minutes;
    }

    private static DateTimeOffset NextDaily(string expression, DateTimeOffset from)
    {
        if (!TimeOnly.TryParseExact(expression, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw new ArgumentException("daily 计划必须使用 HH:mm 格式。");
        var local = from.ToLocalTime();
        var candidateLocal = new DateTimeOffset(local.Year, local.Month, local.Day, time.Hour, time.Minute, 0, local.Offset);
        if (candidateLocal <= local) candidateLocal = candidateLocal.AddDays(1);
        return candidateLocal.ToUniversalTime();
    }

    private static DateTimeOffset? NextOnce(string expression, DateTimeOffset from)
    {
        if (!DateTimeOffset.TryParse(expression, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var candidate))
            throw new ArgumentException("once 计划必须填写 ISO-8601 时间。");
        candidate = candidate.ToUniversalTime();
        return candidate > from ? candidate : null;
    }
}
