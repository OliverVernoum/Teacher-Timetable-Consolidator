using System.Text.Json;
using System.Text.Json.Serialization;

namespace TimetableChecker;

/// <summary>A break or lunch in the school day, with its worked-out times.</summary>
public record BreakSlot(string Name, TimeSpan Start, TimeSpan End)
{
    public override string ToString() => $"{Name}  ({Start:hh\\:mm}–{End:hh\\:mm})";
}

/// <summary>
/// The shape of the school day: when it starts, each period's name and length,
/// and the breaks/lunches between periods. All times are worked out from these.
/// Saved as school-day.json in the timetables folder.
/// </summary>
public class DaySchedule
{
    public const string FileName = "school-day.json";
    public const int MaxPeriods = 15;

    public class PeriodSpec
    {
        public string Name { get; set; } = "";
        public int LengthMinutes { get; set; } = 60;
    }

    public class BreakSpec
    {
        public string Name { get; set; } = "Break";
        /// <summary>The break comes straight after this period (1 = after the first period).</summary>
        public int AfterPeriod { get; set; } = 1;
        public int LengthMinutes { get; set; } = 20;
    }

    [JsonIgnore] public TimeSpan DayStart { get; set; } = new(8, 50, 0);

    [JsonPropertyName("dayStart")]
    public string DayStartText
    {
        get => DayStart.ToString(@"hh\:mm");
        set => DayStart = School.ParseTime(value) ?? new TimeSpan(8, 50, 0);
    }

    public List<PeriodSpec> Periods { get; set; } = new();
    public List<BreakSpec> Breaks { get; set; } = new();

    /// <summary>The default day: 5 one-hour periods from 08:50, a break after P2 and lunch after P4.</summary>
    public static DaySchedule Default() => new()
    {
        DayStart = new TimeSpan(8, 50, 0),
        Periods = Enumerable.Range(1, 5).Select(i => new PeriodSpec { Name = $"P{i}", LengthMinutes = 60 }).ToList(),
        Breaks =
        {
            new BreakSpec { Name = "Break", AfterPeriod = 2, LengthMinutes = 20 },
            new BreakSpec { Name = "Lunch", AfterPeriod = 4, LengthMinutes = 50 },
        },
    };

    /// <summary>Works out every period's and break's start and end time.</summary>
    public (List<Period> Periods, List<BreakSlot> Breaks) Build()
    {
        var periods = new List<Period>();
        var breaks = new List<BreakSlot>();
        var time = DayStart;

        for (int i = 0; i < Periods.Count; i++)
        {
            var spec = Periods[i];
            var end = time + TimeSpan.FromMinutes(spec.LengthMinutes);
            periods.Add(new Period(spec.Name, time, end));
            time = end;

            foreach (var b in Breaks.Where(b => b.AfterPeriod == i + 1))
            {
                var breakEnd = time + TimeSpan.FromMinutes(b.LengthMinutes);
                breaks.Add(new BreakSlot(b.Name, time, breakEnd));
                time = breakEnd;
            }
        }

        return (periods, breaks);
    }

    /// <summary>Returns a problem with these settings, or null if they're fine.</summary>
    public string? Validate()
    {
        if (Periods.Count == 0) return "There must be at least one period.";
        if (Periods.Count > MaxPeriods) return $"There can be at most {MaxPeriods} periods.";

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Periods)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) return "Every period needs a name.";
            if (p.Name.Contains(',') || p.Name.Contains('"')) return $"Period names can't contain commas or quotes ({p.Name}).";
            if (!names.Add(p.Name.Trim())) return $"Two periods are called {p.Name.Trim()}.";
            if (p.LengthMinutes is < 5 or > 300) return $"{p.Name} must be between 5 and 300 minutes long.";
        }
        foreach (var b in Breaks)
        {
            if (string.IsNullOrWhiteSpace(b.Name)) return "Every break needs a name.";
            if (b.AfterPeriod < 1 || b.AfterPeriod >= Periods.Count) return $"{b.Name} must come between two periods.";
            if (b.LengthMinutes is < 1 or > 300) return $"{b.Name} must be between 1 and 300 minutes long.";
        }

        var (periods, breaks) = Build();
        var dayEnd = breaks.Select(b => b.End).Append(periods[^1].End!.Value).Max();
        if (dayEnd >= TimeSpan.FromDays(1)) return "The school day would go past midnight – make it shorter.";
        return null;
    }

    /// <summary>
    /// Builds settings from periods that already have times (e.g. loaded from older CSV files):
    /// gaps between periods become breaks, and gaps of 30+ minutes are called "Lunch".
    /// </summary>
    public static DaySchedule Infer(IReadOnlyList<Period> periods)
    {
        if (periods.Count == 0) return Default();

        var schedule = new DaySchedule { DayStart = periods[0].Start ?? new TimeSpan(8, 50, 0) };
        for (int i = 0; i < periods.Count; i++)
        {
            var p = periods[i];
            int length = p.HasTimes && p.End > p.Start ? (int)(p.End!.Value - p.Start!.Value).TotalMinutes : 60;
            schedule.Periods.Add(new PeriodSpec { Name = p.Name, LengthMinutes = length });

            if (i + 1 < periods.Count && p.End is { } end && periods[i + 1].Start is { } nextStart && nextStart > end)
            {
                int gap = (int)(nextStart - end).TotalMinutes;
                schedule.Breaks.Add(new BreakSpec { Name = gap >= 30 ? "Lunch" : "Break", AfterPeriod = i + 1, LengthMinutes = gap });
            }
        }
        return schedule;
    }

    public DaySchedule Clone() => new()
    {
        DayStart = DayStart,
        Periods = Periods.Select(p => new PeriodSpec { Name = p.Name, LengthMinutes = p.LengthMinutes }).ToList(),
        Breaks = Breaks.Select(b => new BreakSpec { Name = b.Name, AfterPeriod = b.AfterPeriod, LengthMinutes = b.LengthMinutes }).ToList(),
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static DaySchedule? Load(string folder)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<DaySchedule>(File.ReadAllText(path), JsonOptions);
    }

    public void Save(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, FileName), JsonSerializer.Serialize(this, JsonOptions));
    }
}
