namespace LifeOS.Api.Services;

/// <summary>Financial business date uses WIB (UTC+7) consistently across scopes.</summary>
public static class BusinessDate
{
    public static DateOnly TodayWib => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    public static DateTime StartOfDayUtc(DateOnly wibDate)
        => wibDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-7);
}
