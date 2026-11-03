using System.Text;

namespace TimetableChecker;

/// <summary>One lesson slot in the school day, e.g. "P1" from 08:50 to 09:50.</summary>
public record Period(string Name, TimeSpan? Start, TimeSpan? End)
{
    public bool HasTimes => Start is not null && End is not null;

    public override string ToString() =>
        HasTimes ? $"{Name}  ({Start:hh\\:mm}–{End:hh\\:mm})" : Name;
}

/// <summary>What a teacher is doing in one slot.</summary>
public enum SlotKind
{
    Free,        // available – e.g. to cover a lesson
    NonContact,  // not teaching, but the time is set aside (planning/PPA, management time)
    Lesson,
}

/// <summary>A teacher and what they teach in each (day, period) slot.</summary>
public class Teacher
{
    public string Name { get; internal set; }
    public string FilePath { get; internal set; }
    public ContactInfo Contact { get; internal set; } = new();
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

    public SlotKind KindAt(string day, string period) => LessonAt(day, period) switch
    {
        null => SlotKind.Free,
        var text when IsNonContactText(text) => SlotKind.NonContact,
        _ => SlotKind.Lesson,
    };

    /// <summary>
    /// Non-contact time is written in the timetable as "Non-contact" (optionally with a note,
    /// e.g. "Non-contact: HoD time"), "NC" or "PPA".
    /// </summary>
    public static bool IsNonContactText(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        return t.StartsWith("non-contact") || t.StartsWith("non contact") || t.StartsWith("noncontact")
               || t == "nc" || t == "ppa" || t.StartsWith("ppa ") || t.StartsWith("ppa:");
    }

    public void ClearLessons() => _lessons.Clear();

    public override string ToString() => Name;
}

/// <summary>Every teacher's timetable, loaded from a folder of CSV files (one per teacher).</summary>
public class School
{
    public List<string> Days { get; } = new();
    public List<Period> Periods { get; } = new();
    public List<BreakSlot> Breaks { get; } = new();

    /// <summary>The shape of the school day (period lengths, breaks). Edited in "School day settings".</summary>
    public DaySchedule Schedule { get; private set; } = DaySchedule.Default();
    public List<Teacher> Teachers { get; } = new();
    public List<string> Warnings { get; } = new();

    /// <summary>Everyone's contact details from contacts.json, keyed by teacher name.</summary>
    private Dictionary<string, ContactInfo> _contacts = new(StringComparer.OrdinalIgnoreCase);
    private bool _contactsUnreadable; // if contacts.json is damaged, don't overwrite it and lose everyone's details

    /// <summary>The folder the CSV files live in (null if nothing is loaded).</summary>
    public string? Folder { get; private set; }

    /// <summary>Used when a new teacher is added to an empty folder.</summary>
    public static readonly string[] DefaultDays = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" };

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

        school.LoadSchedule();
        school.LoadContacts();
        return school;
    }

    private void LoadContacts()
    {
        try
        {
            _contacts = Contacts.Load(Folder!);
        }
        catch (Exception ex)
        {
            _contactsUnreadable = true;
            Warnings.Add($"{Contacts.FileName} couldn't be read, so contact details can't be changed until it's fixed: {ex.Message}");
        }

        foreach (var teacher in Teachers)
        {
            if (!_contacts.TryGetValue(teacher.Name, out var contact))
                _contacts[teacher.Name] = contact = new ContactInfo();
            teacher.Contact = contact; // the teacher and the dictionary share the same object
        }
    }

    /// <summary>Changes a teacher's contact details and saves contacts.json.</summary>
    public void SetContact(Teacher teacher, string email, string phone)
    {
        teacher.Contact.Email = email.Trim();
        teacher.Contact.Phone = phone.Trim();
        _contacts[teacher.Name] = teacher.Contact;
        SaveContacts();
    }

    private void SaveContacts()
    {
        if (_contactsUnreadable) throw new InvalidOperationException($"{Contacts.FileName} is damaged. Fix or delete it, then click Reload.");
        if (Folder is not null) Contacts.Save(Folder, _contacts);
    }

    /// <summary>
    /// Uses school-day.json for the period times and breaks if it exists.
    /// Otherwise keeps the times from the CSV files and treats gaps between periods as breaks.
    /// </summary>
    private void LoadSchedule()
    {
        DaySchedule? saved = null;
        try
        {
            saved = DaySchedule.Load(Folder!);
            var problem = saved?.Validate();
            if (problem is not null)
            {
                Warnings.Add($"{DaySchedule.FileName}: {problem}");
                saved = null;
            }
        }
        catch (Exception ex)
        {
            Warnings.Add($"{DaySchedule.FileName}: {ex.Message}");
        }

        if (saved is not null)
        {
            var (periods, breaks) = saved.Build();
            var unknown = Periods.Where(p => !periods.Any(n => n.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase))).Select(p => p.Name).ToList();
            if (unknown.Count > 0)
                Warnings.Add($"These periods are in the CSV files but not in {DaySchedule.FileName}, so they are hidden: {string.Join(", ", unknown)}");
            Schedule = saved;
            Periods.Clear();
            Periods.AddRange(periods);
            Breaks.Clear();
            Breaks.AddRange(breaks);
            return;
        }

        if (Periods.Count == 0) return; // empty folder: the default day is used when the first teacher is added
        Schedule = DaySchedule.Infer(Periods);
        Breaks.Clear();
        for (int i = 0; i + 1 < Periods.Count; i++)
        {
            if (Periods[i].End is { } end && Periods[i + 1].Start is { } next && next > end)
                Breaks.Add(new BreakSlot(next - end >= TimeSpan.FromMinutes(30) ? "Lunch" : "Break", end, next));
        }
    }

    /// <summary>
    /// How many lessons would be deleted by switching to <paramref name="schedule"/>
    /// (lessons in periods that no longer exist when the number of periods goes down).
    /// </summary>
    public int LessonsLostBy(DaySchedule schedule) =>
        Teachers.Sum(t => Periods.Skip(schedule.Periods.Count)
            .Sum(p => Days.Count(d => t.LessonAt(d, p.Name) is not null)));

    /// <summary>
    /// Switches to a new school day. Lessons stay in the same position (the 3rd period stays the 3rd period),
    /// even if periods are renamed. Saves school-day.json and every teacher's CSV file.
    /// </summary>
    public void ApplySchedule(DaySchedule schedule)
    {
        var (newPeriods, newBreaks) = schedule.Build();
        var oldPeriods = Periods.ToList();
        if (Days.Count == 0) Days.AddRange(DefaultDays);

        foreach (var teacher in Teachers)
        {
            var lessons = Days.ToDictionary(d => d, d => oldPeriods.Select(p => teacher.LessonAt(d, p.Name) ?? "").ToList());
            teacher.ClearLessons();
            foreach (var day in Days)
                for (int i = 0; i < newPeriods.Count && i < oldPeriods.Count; i++)
                    teacher.SetLesson(day, newPeriods[i].Name, lessons[day][i]);
        }

        Schedule = schedule.Clone();
        Periods.Clear();
        Periods.AddRange(newPeriods);
        Breaks.Clear();
        Breaks.AddRange(newBreaks);

        if (Folder is not null)
        {
            Schedule.Save(Folder);
            SaveAll();
        }
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
        if (Periods.Count == 0) ApplySchedule(Schedule);

        var teacher = new Teacher(name, Path.Combine(Folder, name + ".csv"));
        if (_contacts.TryGetValue(name, out var contact)) teacher.Contact = contact; // details kept from before
        else _contacts[name] = teacher.Contact;
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
        _contacts.Remove(teacher.Name);
        teacher.Name = newName;
        teacher.FilePath = newPath;
        _contacts[newName] = teacher.Contact;
        Save(teacher);
        if (!teacher.Contact.IsEmpty) SaveContacts();
        Teachers.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Removes the teacher and deletes their CSV file.</summary>
    public void RemoveTeacher(Teacher teacher)
    {
        if (File.Exists(teacher.FilePath)) File.Delete(teacher.FilePath);
        Teachers.Remove(teacher);
        if (_contacts.Remove(teacher.Name) && !teacher.Contact.IsEmpty) SaveContacts();
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

    public IEnumerable<Teacher> NonContactTeachers(string day, string period) =>
        Teachers.Where(t => t.KindAt(day, period) == SlotKind.NonContact);

    /// <summary>Teachers who are teaching a lesson (not free, not non-contact).</summary>
    public IEnumerable<Teacher> BusyTeachers(string day, string period) =>
        Teachers.Where(t => t.KindAt(day, period) == SlotKind.Lesson);

    /// <summary>Finds the day column and period happening at <paramref name="now"/>, if any.</summary>
    public (string Day, Period Period)? SlotAt(DateTime now)
    {
        var day = Days.FirstOrDefault(d => ToDayOfWeek(d) == now.DayOfWeek);
        if (day is null) return null;
        var period = Periods.FirstOrDefault(p => p.HasTimes && now.TimeOfDay >= p.Start && now.TimeOfDay < p.End);
        return period is null ? null : (day, period);
    }

    /// <summary>The break or lunch happening at <paramref name="now"/>, if any (on a school day).</summary>
    public BreakSlot? BreakAt(DateTime now)
    {
        if (!Days.Any(d => ToDayOfWeek(d) == now.DayOfWeek)) return null;
        return Breaks.FirstOrDefault(b => now.TimeOfDay >= b.Start && now.TimeOfDay < b.End);
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
