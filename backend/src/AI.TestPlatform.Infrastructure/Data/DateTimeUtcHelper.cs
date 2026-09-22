namespace AI.TestPlatform.Infrastructure.Data;

/// <summary>
/// Npgsql timestamptz 只接受 DateTime.Kind = Utc。
/// [FromQuery] 绑定 URL "/api/xxx?date=2026-09-22" 时 Kind=Unspecified → 必须在写 EF Where 前转 UTC。
/// </summary>
public static class DateTimeUtcHelper
{
    public static DateTime SpecifyUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime(),
    };

    public static DateTime? SpecifyUtc(DateTime? value) => value.HasValue ? SpecifyUtc(value.Value) : null;
}
