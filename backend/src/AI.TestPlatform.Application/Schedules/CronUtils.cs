using NCrontab;

namespace AI.TestPlatform.Application.Schedules;

/// <summary>
/// Cron 表达式工具：解析校验、下次执行时间推算、可读描述。
/// 统一使用标准 5 段 Cron（分 时 日 月 周），与 Linux crontab 写法一致，
/// 例如「0 2 * * *」= 每天 02:00，「*/30 * * * *」= 每 30 分钟。
/// </summary>
public static class CronUtils
{
    private static readonly CrontabSchedule.ParseOptions Options = new() { IncludingSeconds = false };

    /// <summary>解析表达式；失败返回 null 并给出可读原因</summary>
    public static CrontabSchedule? TryParse(string? expression, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "Cron 表达式不能为空";
            return null;
        }
        if (expression.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length != 5)
        {
            error = "Cron 表达式必须是 5 段：分 时 日 月 周（如 0 2 * * *）";
            return null;
        }
        try
        {
            return CrontabSchedule.Parse(expression, Options);
        }
        catch (CrontabException ex)
        {
            error = $"Cron 表达式非法：{ex.Message}";
            return null;
        }
    }

    public static bool IsValid(string? expression) => TryParse(expression, out _) is not null;

    /// <summary>
    /// 从指定时间（默认当前本地时间）开始推算后续 N 次执行时间。
    /// 语义与 Linux crontab 一致：按「服务器本地时间」解释 Cron（0 2 * * * = 本地时间 02:00），
    /// 返回值的 Kind 固定为 Local，落库前由调用方转 UTC。
    /// </summary>
    public static List<DateTime> NextOccurrences(string expression, int count, DateTime? from = null)
    {
        var schedule = TryParse(expression, out _);
        if (schedule is null) return new List<DateTime>();
        var start = from ?? DateTime.Now;
        var result = new List<DateTime>();
        var cursor = start;
        for (var i = 0; i < count; i++)
        {
            var next = schedule.GetNextOccurrence(cursor);
            if (next == default) break;
            result.Add(DateTime.SpecifyKind(next, DateTimeKind.Local));
            // +1 分钟避免同一分钟内原地踏步
            cursor = next.AddMinutes(1);
        }
        return result;
    }

    /// <summary>下次执行时间（本地时间，用于落库 NextRunAt 前转 UTC）</summary>
    public static DateTime? GetNextOccurrence(string? expression, DateTime? from = null)
    {
        var schedule = TryParse(expression, out _);
        if (schedule is null) return null;
        var next = schedule.GetNextOccurrence(from ?? DateTime.Now);
        return next == default ? null : DateTime.SpecifyKind(next, DateTimeKind.Local);
    }

    /// <summary>可读描述：覆盖常见写法，其余回退为原始表达式</summary>
    public static string Describe(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return "未设置";
        var parts = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5) return expression;
        var (minute, hour, day, month, week) = (parts[0], parts[1], parts[2], parts[3], parts[4]);

        // 每分钟 / 每 N 分钟
        if (hour == "*" && day == "*" && month == "*" && week == "*")
        {
            if (minute == "*") return "每分钟";
            if (minute.StartsWith("*/") && int.TryParse(minute[2..], out var stepM))
                return $"每 {stepM} 分钟";
        }

        // 每周某几天
        if (week != "*" && day == "*")
        {
            var dayName = WeekDayName(week);
            if (dayName is not null) return $"{dayName} {TimeText(hour, minute)}";
        }

        // 每月某日
        if (day != "*" && week == "*" && int.TryParse(day, out var dom))
            return $"每月 {dom} 日 {TimeText(hour, minute)}";

        // 每天
        if (day == "*" && week == "*" && month == "*")
        {
            if (hour == "*")
            {
                if (minute == "0") return "每小时整点";
                return int.TryParse(minute, out var m) ? $"每小时第 {m} 分钟" : $"每小时 {minute} 分";
            }
            return $"每天 {TimeText(hour, minute)}";
        }

        return expression;
    }

    private static string TimeText(string hour, string minute)
    {
        if (!int.TryParse(hour, out var h)) return $"{hour}:{minute}";
        var m = int.TryParse(minute, out var mm) ? mm : 0;
        return $"{h:D2}:{m:D2}";
    }

    private static string? WeekDayName(string week) => week switch
    {
        "1" => "每周一",
        "2" => "每周二",
        "3" => "每周三",
        "4" => "每周四",
        "5" => "每周五",
        "6" => "每周六",
        "0" or "7" => "每周日",
        "1-5" => "工作日",
        "0,6" or "6,0" => "周末",
        _ => null,
    };
}
