using System.Text;

namespace TimetableChecker;

/// <summary>One lesson slot in the school day, e.g. "P1" from 08:50 to 09:50.</summary>
public record Period(string Name, TimeSpan? Start, TimeSpan? End)
{
    public bool HasTimes => Start is not null && End is not null;

    public override string ToString() =>
        HasTimes ? $"{Name}  ({Start:hh\\:mm}–{End:hh\\:mm})" : Name;
}

/// <summary>A teacher and what they teach in each (day, period) slot.</summary>
public class Teacher
{
    public string Name { get; internal set; }
    public string FilePath { get; internal set; }
    private readonly Dictionary<(string Day, string Period), string> _lessons = new();

    public Teacher(string name, string filePath)
    {
        Name = name;
        FilePath = filePath;
    }

    public void SetLesson(string day, string period, string lesson) =>
        _lessons[(day, period)] = lesson.Trim();

    /// <summary>The lesson in this slot, or null if the teacher is free.</summary>
    public string? LessonAt(string day, string period)
    {
        if (!_lessons.TryGetValue((day, period), out var lesson)) return null;
        if (lesson.Length == 0 || lesson.Equals("free", StringComparison.OrdinalIgnoreCase)) return null;
        return lesson;
    }

    public bool IsFree(string day, string period) => LessonAt(day, period) is null;

    public override string ToString() => Name;
}

/// <summary>Every teacher's timetable, loaded from a folder of CSV files (one per teacher).</summary>
public class School
{
    public List<string> Days { get; } = new();
    public List<Period> Periods { get; } = new();
    public List<Teacher> Teachers { get; } = new();
    public List<string> Warnings { get; } = new();

    /// <summary>The folder the CSV files live in (null if nothing is loaded).</summary>
    public string? Folder { get; private set; }

    /// <summary>Used when a new teacher is added to an empty folder.</summary>
    public static readonly string[] DefaultDays = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" };
    public static readonly Period[] DefaultPeriods =
    {
        new("P1", new TimeSpan(8, 50, 0), new TimeSpan(9, 50, 0)),
        new("P2", new TimeSpan(9, 50, 0), new TimeSpan(10, 50, 0)),
        new("P3", new TimeSpan(11, 10, 0), new TimeSpan(12, 10, 0)),
        new("P4", new TimeSpan(12, 10, 0), new TimeSpan(13, 10, 0)),
        new("P5", new TimeSpan(14, 0, 0), new TimeSpan(15, 0, 0)),
    };

    private static readonly string[] FixedColumns = { "period", "start", "end" };

    /// <summary>
    /// Loads every *.csv in <paramref name="folder"/>. The file name is the teacher's name.
    /// Format: Period,Start,End,Monday,Tuesday,... then one row per period.
    /// A blank cell (or "Free") means a free period.
    /// </summary>
    public static School Load(string folder)
    {
        var school = new School { Folder = folder };
        var files = Directory.GetFiles(folder, "*.csv").OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            try
            {
                school.LoadTeacher(file);
            }
            catch (Exception ex)
            {
                school.Warnings.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return school;
    }

    private void LoadTeacher(string file)
    {
        var rows = File.ReadAllLines(file)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(ParseCsvLine)
            .ToList();
        if (rows.Count < 2) throw new FormatException("needs a header row and at least one period row");

        var header = rows[0];
        int periodCol = IndexOf(header, "period");
        if (periodCol < 0) throw new FormatException("header row has no 'Period' column");
        int startCol = IndexOf(header, "start");
        int endCol = IndexOf(header, "end");

        // Every other column is a day of the week
        var dayCols = new List<(int Col, string Day)>();
        for (int c = 0; c < header.Count; c++)
        {
            var name = header[c].Trim();
            if (name.Length == 0 || FixedColumns.Contains(name.ToLowerInvariant())) continue;
            dayCols.Add((c, name));
            if (!Days.Contains(name, StringComparer.OrdinalIgnoreCase)) Days.Add(name);
        }

        var teacher = new Teacher(Path.GetFileNameWithoutExtension(file), file);

        foreach (var row in rows.Skip(1))
        {
            var periodName = Cell(row, periodCol).Trim();
            if (periodName.Length == 0) continue;

            if (!Periods.Any(p => p.Name.Equals(periodName, StringComparison.OrdinalIgnoreCase)))
                Periods.Add(new Period(periodName, ParseTime(Cell(row, startCol)), ParseTime(Cell(row, endCol))));
            var period = Periods.First(p => p.Name.Equals(periodName, StringComparison.OrdinalIgnoreCase)).Name;

            foreach (var (col, dayName) in dayCols)
            {
                var day = Days.First(d => d.Equals(dayName, StringComparison.OrdinalIgnoreCase));
                teacher.SetLesson(day, period, Cell(row, col));
            }
        }

        Teachers.Add(teacher);
    }

    // ---------- Adding, renaming, removing and saving ----------

    /// <summary>Returns an error message if <paramref name="name"/> can't be used as a teacher name, otherwise null.</summary>
    public string? CheckName(string name, Teacher? except = null)
    {
        name = name.Trim();
        if (name.Length == 0) return "Please type a name.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
            return "Names can't contain / \\ : or other special characters.";
        if (Teachers.Any(t => t != except && t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return $"There is already a teacher called {name}.";
        return null;
    }

    /// <summary>Creates a new teacher with every period free and saves their CSV file.</summary>
    public Teacher AddTeacher(string folder, string name)
    {
        name = name.Trim();
        Folder ??= folder;
        Directory.CreateDirectory(Folder);
        if (Days.Count == 0) Days.AddRange(DefaultDays);
        if (Periods.Count == 0) Periods.AddRange(DefaultPeriods);

        var teacher = new Teacher(name, Path.Combine(Folder, name + ".csv"));
        Teachers.Add(teacher);
        Teachers.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        Save(teacher);
        return teacher;
    }

    public void RenameTeacher(Teacher teacher, string newName)
    {
        newName = newName.Trim();
        var newPath = Path.Combine(Path.GetDirectoryName(teacher.FilePath)!, newName + ".csv");
        if (File.Exists(teacher.FilePath) && !string.Equals(teacher.FilePath, newPath, StringComparison.Ordinal))
            File.Move(teacher.FilePath, newPath);
        teacher.Name = newName;
        teacher.FilePath = newPath;
        Save(teacher);
        Teachers.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Removes the teacher and deletes their CSV file.</summary>
    public void RemoveTeacher(Teacher teacher)
    {
        if (File.Exists(teacher.FilePath)) File.Delete(teacher.FilePath);
        Teachers.Remove(teacher);
    }

    /// <summary>Changes a period's start/end time (shared by every teacher) and saves all files.</summary>
    public void SetPeriodTimes(int index, TimeSpan? start, TimeSpan? end)
    {
        Periods[index] = Periods[index] with { Start = start, End = end };
    }

    public void SaveAll()
    {
        foreach (var teacher in Teachers) Save(teacher);
    }

    /// <summary>Writes one teacher's timetable back to their CSV file.</summary>
    public void Save(Teacher teacher)
    {
        var lines = new List<string>
        {
            string.Join(",", new[] { "Period", "Start", "End" }.Concat(Days).Select(CsvEscape)),
        };
        foreach (var p in Periods)
        {
            var cells = new List<string> { p.Name, FormatTime(p.Start), FormatTime(p.End) };
            cells.AddRange(Days.Select(d => teacher.LessonAt(d, p.Name) ?? ""));
            lines.Add(string.Join(",", cells.Select(CsvEscape)));
        }
        File.WriteAllLines(teacher.FilePath, lines);
    }

    public static string FormatTime(TimeSpan? t) => t is null ? "" : t.Value.ToString(@"hh\:mm");

    public static TimeSpan? ParseTime(string text) =>
        TimeSpan.TryParse(text.Trim(), out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1) ? t : null;

    private static string CsvEscape(string cell) =>
        cell.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell;

    // ---------- Questions ----------

    public IEnumerable<Teacher> FreeTeachers(string day, string period) =>
        Teachers.Where(t => t.IsFree(day, period));

    public IEnumerable<Teacher> BusyTeachers(string day, string period) =>
        Teachers.Where(t => !t.IsFree(day, period));

    /// <summary>Finds the day column and period happening at <paramref name="now"/>, if any.</summary>
    public (string Day, Period Period)? SlotAt(DateTime now)
    {
        var day = Days.FirstOrDefault(d => ToDayOfWeek(d) == now.DayOfWeek);
        if (day is null) return null;
        var period = Periods.FirstOrDefault(p => p.HasTimes && now.TimeOfDay >= p.Start && now.TimeOfDay < p.End);
        return period is null ? null : (day, period);
    }

    /// <summary>Turns "Monday", "Mon" or "monday" into a DayOfWeek.</summary>
    public static DayOfWeek? ToDayOfWeek(string day)
    {
        var d = day.Trim().ToLowerInvariant();
        if (d.Length < 3) return null;
        foreach (DayOfWeek dow in Enum.GetValues<DayOfWeek>())
            if (dow.ToString().ToLowerInvariant().StartsWith(d[..3])) return dow;
        return null;
    }

    private static int IndexOf(List<string> header, string name) =>
        header.FindIndex(h => h.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string Cell(List<string> row, int col) =>
        col >= 0 && col < row.Count ? row[col] : "";

    /// <summary>Splits one CSV line, allowing "quoted, values".</summary>
    public static List<string> ParseCsvLine(string line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (ch == '"') inQuotes = false;
                else current.Append(ch);
            }
            else if (ch == '"') inQuotes = true;
            else if (ch == ',') { cells.Add(current.ToString()); current.Clear(); }
            else current.Append(ch);
        }

        cells.Add(current.ToString());
        return cells;
    }
}
