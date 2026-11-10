using System.Globalization;

namespace FutureNews.Core.Fetching;

/// <summary>Tolerant parsing of the many date shapes scholarly APIs return.</summary>
public static class DateUtil
{
    /// <summary>
    /// Parses a publication date. Supports full ISO timestamps, "yyyy-MM-dd", "yyyy-MM" and
    /// bare years. A bare year resolves to today when it is the current year (the record is
    /// certainly recent) and to 1 January otherwise.
    /// </summary>
    public static DateTimeOffset ParseFlexible(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DateTimeOffset.UtcNow;

        var text = value.Trim();

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var exact))
        {
            return exact;
        }

        if (text.Length == 7 && int.TryParse(text[..4], out var ymYear) && int.TryParse(text[5..7], out var ymMonth))
        {
            try { return new DateTimeOffset(ymYear, ymMonth, 1, 0, 0, 0, TimeSpan.Zero); }
            catch (ArgumentOutOfRangeException) { }
        }

        if (text.Length == 4 && int.TryParse(text, out var year))
        {
            if (year is >= 1900 and <= 2200)
            {
                var now = DateTimeOffset.UtcNow;
                if (year == now.Year) return now;
                try { return new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero); }
                catch (ArgumentOutOfRangeException) { }
            }
        }

        return DateTimeOffset.UtcNow;
    }
}
