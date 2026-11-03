using System.Text;

namespace TimetableChecker;

/// <summary>
/// Builds iCalendar (.ics) files, which Apple Calendar, Outlook and Google Calendar can import.
/// Each period becomes a weekly repeating event.
/// </summary>
public static class CalendarExport
{
    public static string Build(School school, Teacher teacher, DateTime firstWeek, int weeks,
                               bool includeFree, bool includeLessons, bool includeNonContact)
    {
        var sb = new StringBuilder();
        void Line(string text) => sb.Append(Fold(text)).Append("\r\n");

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");

        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//Timetable Checker//EN");
        Line("CALSCALE:GREGORIAN");
        Line("METHOD:PUBLISH");
        Line($"X-WR-CALNAME:{Escape(teacher.Name + " timetable")}");

        foreach (var day in school.Days)
        {
            if (School.ToDayOfWeek(day) is not DayOfWeek dow) continue;
            var date = FirstOnOrAfter(firstWeek.Date, dow);

            foreach (var period in school.Periods.Where(p => p.HasTimes))
            {
                var lesson = teacher.LessonAt(day, period.Name);
                var kind = teacher.KindAt(day, period.Name);
                bool free = kind == SlotKind.Free;
                if (free && !includeFree) continue;
                if (kind == SlotKind.Lesson && !includeLessons) continue;
                if (kind == SlotKind.NonContact && !includeNonContact) continue;

                var start = date + period.Start!.Value;
                var end = date + period.End!.Value;
                var summary = free ? $"Free period ({period.Name})" : $"{lesson} ({period.Name})";
                var uid = $"{Slug(teacher.Name)}-{Slug(day)}-{Slug(period.Name)}-{date:yyyyMMdd}@timetable-checker";

                Line("BEGIN:VEVENT");
                Line($"UID:{uid}");
                Line($"DTSTAMP:{stamp}");
                // "Floating" local time: shows at the same clock time in whatever time zone the calendar is in
                Line($"DTSTART:{start:yyyyMMdd'T'HHmmss}");
                Line($"DTEND:{end:yyyyMMdd'T'HHmmss}");
                Line($"RRULE:FREQ=WEEKLY;COUNT={weeks}");
                Line($"SUMMARY:{Escape(summary)}");
                if (free) Line("TRANSP:TRANSPARENT"); // free periods don't mark you as busy
                Line("END:VEVENT");
            }
        }

        Line("END:VCALENDAR");
        return sb.ToString();
    }

    private static DateTime FirstOnOrAfter(DateTime date, DayOfWeek day) =>
        date.AddDays(((int)day - (int)date.DayOfWeek + 7) % 7);

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");

    private static string Slug(string text) =>
        new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());

    /// <summary>iCalendar lines must be at most 75 bytes; longer ones continue on the next line after a space.</summary>
    private static string Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= 75) return line;
        var sb = new StringBuilder();
        int bytes = 0;
        foreach (var ch in line)
        {
            int size = Encoding.UTF8.GetByteCount(ch.ToString());
            if (bytes + size > 75) { sb.Append("\r\n "); bytes = 1; }
            sb.Append(ch);
            bytes += size;
        }
        return sb.ToString();
    }
}
