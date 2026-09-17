using System.Globalization;

namespace FixPal.Models.ViewModels;

// Display conversion only; scheduling input validation remains in SchedulingTimePolicy.
public static class SchedulingDisplay
{
    public static string ZoneLabel(string? id)
    {
        var zone = Resolve(id);
        return zone.Id switch
        {
            "Asia/Hebron" or "Asia/Gaza" or "West Bank Standard Time" => "توقيت فلسطين",
            "UTC" => "التوقيت العالمي (UTC)",
            _ => zone.Id
        };
    }

    public static string Format(DateTimeOffset instant, string? id) =>
        TimeZoneInfo.ConvertTime(instant, Resolve(id)).ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);

    private static TimeZoneInfo Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
        catch (ArgumentException) { return TimeZoneInfo.Utc; }
    }
}
