using RussiaBasketBot.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RussiaBasketBot;

public class ParseUtils
{
    public static int? ExtractTeamId(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        var match = Regex.Match(url, @"id=(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    public static (string url, int id) ExtractGameUrlAndId(string text)
    {
        // Define the regex to match the URL and the ID
        var regex = new Regex(@"(?:location\.href=')?(/[^']+\?id=(\d+))'?");

        // Find the match in the input text
        var match = regex.Match(text);

        if (!match.Success)
            return ("", 0);

        // Extract the URL and ID from the match groups
        var url = match.Groups[1].Value;
        var id = int.Parse(match.Groups[2].Value);

        return (url, id);
    }

    public static DateTime GameDateToUtc(string dateString, TimeSpan defaultTime)
    {
        var culture = new CultureInfo("ru-RU");
        var formats = new[]
        {
            "dd MMMM yyyy, HH:mm (мск)", // Format with time and "(мск)"
            "dd MMMM yyyy"               // Format without time
        };

        if (!DateTime.TryParseExact(dateString, formats, culture, DateTimeStyles.None, out var moscowTime))
            return DateTime.MinValue;

        // If the format includes "(мск)", adjust for Moscow time zone (UTC+3)
        if (!dateString.Contains("(мск)"))
        {
            moscowTime = moscowTime.Add(defaultTime);
        }

        var moscowTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        var utcTime = TimeZoneInfo.ConvertTimeToUtc(moscowTime, moscowTimeZone);

        return utcTime;
    }

    public static (int homeTeamId, int guestTeamId) ExtractTeamIds(string text)
    {
        // Define the regex to match "_team<id>" patterns
        var regex = new Regex(@"_team(\d+)");

        // Find all matches in the input text
        var matches = regex.Matches(text);

        if (matches.Count != 2) 
            return (0, 0);

        // Extract the first and second IDs
        var homeTeamId = int.Parse(matches[0].Groups[1].Value);
        var guestTeamId = int.Parse(matches[1].Groups[1].Value);

        return (homeTeamId, guestTeamId);
    }

    public static MatchStatus? ExtractGameStatus(string text)
    {
        // Define the regex to match the "_status<number>" pattern
        var regex = new Regex(@"_status(\d)");

        // Find the match in the input text
        var match = regex.Match(text);

        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var status)) return null;

        // Map the extracted status number to the MatchStatus enum
        if (Enum.IsDefined(typeof(MatchStatus), status))
            return (MatchStatus)status;

        return null;
    }
}