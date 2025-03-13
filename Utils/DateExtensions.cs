namespace RussiaBasketBot;

public static class DateExtensions
{
    public const string MOSCOW_TIME_ZONE_ID = "Russian Standard Time";

    public static TimeZoneInfo GetMscTimeZoneInfo()
    {
        return TimeZoneInfo.FindSystemTimeZoneById(MOSCOW_TIME_ZONE_ID);
    }

    /// <summary>
    /// Конвертирует из ГМТ в МСК
    /// </summary>
    public static DateTime UtcToMsc(this DateTime dateTime)
    {
        return TimeZoneInfo.ConvertTimeBySystemTimeZoneId(dateTime, TimeZoneInfo.Utc.Id, MOSCOW_TIME_ZONE_ID);
    }

    public static DateTime? UtcToMsc(this DateTime? dateTime)
    {
        return dateTime?.UtcToMsc();
    }

    /// <summary>
    /// Конвертирует из МСК в ГМТ
    /// </summary>
    public static DateTime MscToUtc(this DateTime dateTime)
    {
        return TimeZoneInfo.ConvertTimeBySystemTimeZoneId(dateTime, MOSCOW_TIME_ZONE_ID, TimeZoneInfo.Utc.Id);
    }

    public static DateTime? MscToUtc(this DateTime? dateTime)
    {
        return dateTime?.MscToUtc();
    }

    public static DateTime SetKindUtc(this DateTime date)
    {
        return date.Kind == DateTimeKind.Utc ? date : DateTime.SpecifyKind(date, DateTimeKind.Utc);
    }

    public static DateTime? SetKindUtc(this DateTime? date)
    {
        if (date == null) return null;
        return date.Value.Kind == DateTimeKind.Utc ? date : DateTime.SpecifyKind(date.Value, DateTimeKind.Utc);
    }
}