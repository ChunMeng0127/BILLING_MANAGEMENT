namespace BillingControl.Services;

public sealed class BusinessClock(TimeProvider time, IConfiguration configuration)
{
    private readonly TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(configuration["BusinessTimeZone"] ?? "Asia/Kuala_Lumpur");
    public DateTime UtcNow => time.GetUtcNow().UtcDateTime;
    public DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
    public DateOnly Today => DateOnly.FromDateTime(Local(UtcNow));
    public DateOnly CurrentWeekStart => WeekStart(Today);
    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
    public bool IsLate(DateTime submittedAt, DateOnly weekEnd) => DateOnly.FromDateTime(Local(submittedAt)) > weekEnd;
}
