using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace TimetableChecker;

public class MainWindow : Window
{
    private School _school = new();

    private readonly TextBlock _folderLabel = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ComboBox _dayBox = new() { MinWidth = 140 };
    private readonly ComboBox _periodBox = new() { MinWidth = 200 };
    private readonly TextBlock _freeHeading = new() { FontWeight = FontWeight.SemiBold, FontSize = 16 };
    private readonly ListBox _freeList = new() { MinHeight = 120 };
    private readonly ListBox _busyList = new() { MinHeight = 80 };
    private readonly ComboBox _teacherBox = new() { MinWidth = 200 };
    private readonly Grid _timetableGrid = new();
    private readonly CalendarDatePicker _startDate = new() { Width = 170, SelectedDate = NextMonday(DateTime.Today) };
    private readonly NumericUpDown _weeks = new() { Value = 13, Minimum = 1, Maximum = 52, Increment = 1, FormatString = "0", Width = 130 };
    private readonly CheckBox _includeFree = new() { Content = "Free periods", IsChecked = true };
    private readonly CheckBox _includeLessons = new() { Content = "Lessons", IsChecked = true };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };

    // Editing a timetable
    private bool _editing;
    private readonly Dictionary<(string Day, string Period), TextBox> _lessonEditors = new();
    private readonly List<(TextBox Start, TextBox End)> _timeEditors = new();
    private readonly Button _editButton = new() { Content = "Edit timetable" };
    private readonly Button _cancelEditButton = new() { Content = "Cancel", IsVisible = false };
    private readonly Button _renameButton = new() { Content = "Rename…" };
    private readonly Button _removeButton = new() { Content = "Remove…" };
    private readonly TextBlock _gridHint = new() { Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap };

    private static readonly IBrush FreeBrush = new SolidColorBrush(Color.Parse("#4CAF50"), 0.30);
    private static readonly IBrush LessonBrush = new SolidColorBrush(Color.Parse("#808080"), 0.12);
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.Parse("#2196F3"));

    public MainWindow()
    {
        Title = "Timetable Checker";
        Width = 1100;
        Height = 720;
        MinWidth = 760;
        MinHeight = 520;

        Content = BuildLayout();

        _dayBox.SelectionChanged += (_, _) => { RefreshFreeList(); if (!_editing) RefreshTimetable(); };
        _periodBox.SelectionChanged += (_, _) => { RefreshFreeList(); if (!_editing) RefreshTimetable(); };
        _teacherBox.SelectionChanged += (_, _) => RefreshTimetable();
        _freeList.SelectionChanged += (_, _) => ShowTeacher(_freeList.SelectedItem as Teacher);
        _busyList.SelectionChanged += (_, _) => ShowTeacher((_busyList.SelectedItem as BusyEntry)?.Teacher);

        LoadFolder(FindDefaultFolder());
    }

    // ---------- Layout ----------

    private Control BuildLayout()
    {
        var openButton = new Button { Content = "Open timetables folder…" };
        openButton.Click += async (_, _) => await ChooseFolder();
        var reloadButton = new Button { Content = "Reload" };
        reloadButton.Click += (_, _) => LoadFolder(_folderLabel.Tag as string);

        var topBar = new DockPanel { Margin = new Thickness(16, 12), LastChildFill = true };
        var topButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        topButtons.Children.Add(openButton);
        topButtons.Children.Add(reloadButton);
        DockPanel.SetDock(topButtons, Dock.Left);
        topBar.Children.Add(topButtons);
        _folderLabel.Margin = new Thickness(12, 0, 0, 0);
        topBar.Children.Add(_folderLabel);

        // Left: who's free?
        var nowButton = new Button { Content = "Free right now", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        nowButton.Click += (_, _) => SelectCurrentPeriod();

        var left = new StackPanel { Spacing = 8, Width = 300 };
        left.Children.Add(Heading("Who's free?"));
        left.Children.Add(Labelled("Day", _dayBox));
        left.Children.Add(Labelled("Period", _periodBox));
        left.Children.Add(nowButton);
        left.Children.Add(new Separator { Margin = new Thickness(0, 4) });
        left.Children.Add(_freeHeading);
        left.Children.Add(_freeList);
        left.Children.Add(new TextBlock { Text = "Teaching", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        left.Children.Add(_busyList);

        // Right: one teacher's week + calendar export
        var exportButton = new Button { Content = "Export to calendar (.ics)…" };
        exportButton.Click += async (_, _) => await ExportSelectedTeacher();
        var exportAllButton = new Button { Content = "Export every teacher…" };
        exportAllButton.Click += async (_, _) => await ExportAllTeachers();

        var exportOptions = new WrapPanel { Orientation = Orientation.Horizontal };
        exportOptions.Children.Add(Labelled("First week", _startDate, 12));
        exportOptions.Children.Add(Labelled("Repeat for (weeks)", _weeks, 12));
        var includes = new StackPanel { Spacing = 0, Margin = new Thickness(0, 0, 12, 0) };
        includes.Children.Add(new TextBlock { Text = "Include", Opacity = 0.8 });
        includes.Children.Add(_includeFree);
        includes.Children.Add(_includeLessons);
        exportOptions.Children.Add(includes);

        var exportButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        exportButtons.Children.Add(exportButton);
        exportButtons.Children.Add(exportAllButton);

        var right = new StackPanel { Spacing = 10 };
        right.Children.Add(Heading("Teacher timetable"));
        var addButton = new Button { Content = "Add teacher…" };
        addButton.Click += async (_, _) => await AddTeacher();
        _renameButton.Click += async (_, _) => await RenameTeacher();
        _removeButton.Click += async (_, _) => await RemoveTeacher();
        _editButton.Click += (_, _) => ToggleEditing();
        _cancelEditButton.Click += (_, _) => { _editing = false; RefreshTimetable(); };

        var teacherRow = new WrapPanel { Orientation = Orientation.Horizontal };
        teacherRow.Children.Add(Labelled("Teacher", _teacherBox, 12));
        var manageButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8) };
        manageButtons.Children.Add(addButton);
        manageButtons.Children.Add(_renameButton);
        manageButtons.Children.Add(_removeButton);
        manageButtons.Children.Add(_editButton);
        manageButtons.Children.Add(_cancelEditButton);
        teacherRow.Children.Add(manageButtons);
        right.Children.Add(teacherRow);
        right.Children.Add(new ScrollViewer
        {
            Content = _timetableGrid,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        });
        right.Children.Add(_gridHint);
        right.Children.Add(new Separator { Margin = new Thickness(0, 4) });
        right.Children.Add(Heading("Add to Apple Calendar / Outlook / Google Calendar"));
        right.Children.Add(exportOptions);
        right.Children.Add(exportButtons);
        right.Children.Add(_status);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,24,*"), Margin = new Thickness(16, 0, 16, 16) };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 2);
        body.Children.Add(left);
        body.Children.Add(right);

        var root = new DockPanel();
        DockPanel.SetDock(topBar, Dock.Top);
        root.Children.Add(topBar);
        root.Children.Add(new ScrollViewer { Content = body });
        return root;
    }

    private static TextBlock Heading(string text) =>
        new() { Text = text, FontSize = 20, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 0, 0, 4) };

    private static Control Labelled(string label, Control control, double rightMargin = 0)
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, rightMargin, 8) };
        panel.Children.Add(new TextBlock { Text = label, Opacity = 0.8 });
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        panel.Children.Add(control);
        return panel;
    }

    // ---------- Loading ----------

    /// <summary>Where new timetables go if no folder has been opened: Documents/Timetable Checker/timetables.</summary>
    private static string DocumentsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Timetable Checker", "timetables");

    /// <summary>Remembers the last folder opened, so it opens again next time.</summary>
    private static string SettingsFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TimetableChecker", "last-folder.txt");

    private static string? FindDefaultFolder()
    {
        string? remembered = null;
        try { if (File.Exists(SettingsFile)) remembered = File.ReadAllText(SettingsFile).Trim(); } catch { }

        var candidates = new[]
        {
            remembered,
            Path.Combine(AppContext.BaseDirectory, "timetables"),
            Path.Combine(Directory.GetCurrentDirectory(), "timetables"),
            DocumentsFolder,
        };
        return candidates.FirstOrDefault(f => !string.IsNullOrEmpty(f) && Directory.Exists(f));
    }

    private static void RememberFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, folder);
        }
        catch { /* not important if this fails */ }
    }

    private async Task ChooseFolder()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the folder containing the teachers' timetable CSV files",
            AllowMultiple = false,
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null) LoadFolder(path);
    }

    private void LoadFolder(string? folder, string? selectTeacher = null)
    {
        _editing = false;
        if (folder is null || !Directory.Exists(folder))
        {
            _school = new School();
            _folderLabel.Text = "No timetables loaded – click “Open timetables folder…”";
            _folderLabel.Tag = null;
        }
        else
        {
            _school = School.Load(folder);
            _folderLabel.Text = $"{folder}   ·   {_school.Teachers.Count} teachers";
            _folderLabel.Tag = folder;
            RememberFolder(folder);
        }

        _dayBox.ItemsSource = _school.Days.ToList();
        _periodBox.ItemsSource = _school.Periods.ToList();
        _teacherBox.ItemsSource = _school.Teachers.ToList();
        _dayBox.SelectedIndex = _school.Days.Count > 0 ? 0 : -1;
        _periodBox.SelectedIndex = _school.Periods.Count > 0 ? 0 : -1;
        _teacherBox.SelectedItem = _school.Teachers.FirstOrDefault(t => t.Name == selectTeacher) ?? _school.Teachers.FirstOrDefault();

        _status.Text = _school.Warnings.Count > 0
            ? "Some files could not be read:\n" + string.Join("\n", _school.Warnings)
            : "";

        RefreshFreeList();
        RefreshTimetable();
    }

    // ---------- Who's free ----------

    private record BusyEntry(Teacher Teacher, string Lesson)
    {
        public override string ToString() => $"{Teacher.Name} – {Lesson}";
    }

    private void SelectCurrentPeriod()
    {
        var slot = _school.SlotAt(DateTime.Now);
        if (slot is null)
        {
            _freeHeading.Text = "No lesson is happening right now";
            _freeList.ItemsSource = null;
            _busyList.ItemsSource = null;
            return;
        }
        _dayBox.SelectedItem = slot.Value.Day;
        _periodBox.SelectedItem = slot.Value.Period;
    }

    private void RefreshFreeList()
    {
        if (_dayBox.SelectedItem is not string day || _periodBox.SelectedItem is not Period period)
        {
            _freeHeading.Text = "Free teachers";
            _freeList.ItemsSource = null;
            _busyList.ItemsSource = null;
            return;
        }

        var free = _school.FreeTeachers(day, period.Name).ToList();
        _freeHeading.Text = free.Count switch
        {
            0 => $"Nobody is free {day} {period.Name}",
            1 => $"1 teacher free {day} {period.Name}",
            _ => $"{free.Count} teachers free {day} {period.Name}",
        };
        _freeList.ItemsSource = free;
        _busyList.ItemsSource = _school.BusyTeachers(day, period.Name)
            .Select(t => new BusyEntry(t, t.LessonAt(day, period.Name)!))
            .ToList();
    }

    private void ShowTeacher(Teacher? teacher)
    {
        if (teacher is not null) _teacherBox.SelectedItem = teacher;
    }

    // ---------- Timetable grid ----------

    private void RefreshTimetable()
    {
        _timetableGrid.Children.Clear();
        _timetableGrid.RowDefinitions.Clear();
        _timetableGrid.ColumnDefinitions.Clear();
        _lessonEditors.Clear();
        _timeEditors.Clear();

        var teacher = _teacherBox.SelectedItem as Teacher;
        if (teacher is null) _editing = false;
        _editButton.Content = _editing ? "Save changes" : "Edit timetable";
        _cancelEditButton.IsVisible = _editing;
        _editButton.IsEnabled = teacher is not null;
        _renameButton.IsEnabled = _removeButton.IsEnabled = teacher is not null && !_editing;
        _teacherBox.IsEnabled = !_editing;
        _gridHint.Text = teacher is null
            ? "No teachers yet – click “Add teacher…” to create one."
            : _editing
                ? "Type the class in each box, or leave it empty for a free period. Period times are shared by every teacher. Click “Save changes” when done."
                : "Green = free period. The blue outline is the period picked on the left.";
        if (teacher is null) return;

        _timetableGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        foreach (var _ in _school.Days) _timetableGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(_editing ? 105 : 120)));
        _timetableGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        foreach (var _ in _school.Periods) _timetableGrid.RowDefinitions.Add(new RowDefinition(new GridLength(52)));

        for (int d = 0; d < _school.Days.Count; d++)
            AddCell(0, d + 1, _school.Days[d], null, bold: true);

        var selectedDay = _dayBox.SelectedItem as string;
        var selectedPeriod = (_periodBox.SelectedItem as Period)?.Name;

        for (int p = 0; p < _school.Periods.Count; p++)
        {
            var period = _school.Periods[p];
            if (_editing)
            {
                AddTimeEditor(p + 1, period);
            }
            else
            {
                var label = period.HasTimes ? $"{period.Name}\n{period.Start:hh\\:mm}–{period.End:hh\\:mm}" : period.Name;
                AddCell(p + 1, 0, label, null, bold: true);
            }

            for (int d = 0; d < _school.Days.Count; d++)
            {
                var day = _school.Days[d];
                var lesson = teacher.LessonAt(day, period.Name);
                if (_editing)
                {
                    var box = new TextBox { Text = lesson ?? "", Watermark = "Free", Margin = new Thickness(2), VerticalContentAlignment = VerticalAlignment.Center };
                    Grid.SetRow(box, p + 1);
                    Grid.SetColumn(box, d + 1);
                    _timetableGrid.Children.Add(box);
                    _lessonEditors[(day, period.Name)] = box;
                    continue;
                }
                var cell = AddCell(p + 1, d + 1, lesson ?? "Free", lesson is null ? FreeBrush : LessonBrush, bold: lesson is null);
                if (day == selectedDay && period.Name == selectedPeriod)
                {
                    cell.BorderBrush = HighlightBrush;
                    cell.BorderThickness = new Thickness(2);
                }
            }
        }
    }

    private void AddTimeEditor(int row, Period period)
    {
        var start = new TextBox { Text = School.FormatTime(period.Start), Watermark = "09:00", Width = 64 };
        var end = new TextBox { Text = School.FormatTime(period.End), Watermark = "10:00", Width = 64 };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = period.Name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 28 });
        panel.Children.Add(start);
        panel.Children.Add(new TextBlock { Text = "–", VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(end);
        Grid.SetRow(panel, row);
        Grid.SetColumn(panel, 0);
        _timetableGrid.Children.Add(panel);
        _timeEditors.Add((start, end));
    }

    private Border AddCell(int row, int col, string text, IBrush? background, bool bold)
    {
        var cell = new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(2),
            Padding = new Thickness(8, 4),
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            },
        };
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, col);
        _timetableGrid.Children.Add(cell);
        return cell;
    }

    // ---------- Adding, editing and removing teachers ----------

    private async Task AddTeacher()
    {
        if (_editing) { _status.Text = "Save or cancel your changes first."; return; }
        var name = await Dialogs.AskText(this, "Add teacher", "Teacher's name (this is also the CSV file name):", "",
            text => _school.CheckName(text));
        if (name is null) return;

        var folder = _school.Folder ?? DocumentsFolder;
        try
        {
            var teacher = _school.AddTeacher(folder, name);
            LoadFolder(_school.Folder, teacher.Name);
            _editing = true; // jump straight into filling in their timetable
            RefreshTimetable();
            _status.Text = $"Added {teacher.Name}. Fill in their lessons, then click “Save changes”.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Couldn't add {name}: {ex.Message}";
        }
    }

    private async Task RenameTeacher()
    {
        if (_teacherBox.SelectedItem is not Teacher teacher) return;
        var name = await Dialogs.AskText(this, "Rename teacher", $"New name for {teacher.Name}:", teacher.Name,
            text => _school.CheckName(text, except: teacher));
        if (name is null || name == teacher.Name) return;

        try
        {
            var oldName = teacher.Name;
            _school.RenameTeacher(teacher, name);
            LoadFolder(_school.Folder, name);
            _status.Text = $"Renamed {oldName} to {name}.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Couldn't rename: {ex.Message}";
        }
    }

    private async Task RemoveTeacher()
    {
        if (_teacherBox.SelectedItem is not Teacher teacher) return;
        bool sure = await Dialogs.Confirm(this, "Remove teacher",
            $"Remove {teacher.Name}? This deletes their timetable file:\n{teacher.FilePath}", "Remove");
        if (!sure) return;

        try
        {
            _school.RemoveTeacher(teacher);
            LoadFolder(_school.Folder);
            _status.Text = $"Removed {teacher.Name}.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Couldn't remove {teacher.Name}: {ex.Message}";
        }
    }

    private void ToggleEditing()
    {
        if (_teacherBox.SelectedItem is not Teacher teacher) return;
        if (!_editing)
        {
            _editing = true;
            RefreshTimetable();
            return;
        }

        // Check the times first so nothing is half-saved
        var times = new List<(TimeSpan? Start, TimeSpan? End)>();
        for (int i = 0; i < _timeEditors.Count; i++)
        {
            var (startBox, endBox) = _timeEditors[i];
            var name = _school.Periods[i].Name;
            var startText = (startBox.Text ?? "").Trim();
            var endText = (endBox.Text ?? "").Trim();
            var start = School.ParseTime(startText);
            var end = School.ParseTime(endText);
            if ((startText.Length > 0 && start is null) || (endText.Length > 0 && end is null))
            {
                _status.Text = $"{name}: times must look like 09:00.";
                return;
            }
            if (start is not null && end is not null && end <= start)
            {
                _status.Text = $"{name}: the end time must be after the start time.";
                return;
            }
            times.Add((start, end));
        }

        try
        {
            for (int i = 0; i < times.Count; i++) _school.SetPeriodTimes(i, times[i].Start, times[i].End);
            foreach (var ((day, period), box) in _lessonEditors) teacher.SetLesson(day, period, box.Text ?? "");
            _school.SaveAll(); // times are stored in every teacher's file
            _editing = false;
            _status.Text = $"Saved {teacher.Name}'s timetable.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Couldn't save: {ex.Message}";
        }

        var periodIndex = _periodBox.SelectedIndex;
        _periodBox.ItemsSource = _school.Periods.ToList();
        _periodBox.SelectedIndex = periodIndex;
        RefreshFreeList();
        RefreshTimetable();
    }

    // ---------- Calendar export ----------

    private bool TryGetExportSettings(out DateTime firstWeek, out int weeks, out bool free, out bool lessons)
    {
        firstWeek = _startDate.SelectedDate ?? DateTime.Today;
        weeks = (int)(_weeks.Value ?? 13);
        free = _includeFree.IsChecked == true;
        lessons = _includeLessons.IsChecked == true;

        if (!free && !lessons)
        {
            _status.Text = "Tick “Free periods”, “Lessons” or both before exporting.";
            return false;
        }
        if (!_school.Periods.Any(p => p.HasTimes))
        {
            _status.Text = "The timetables need Start and End times to be exported to a calendar.";
            return false;
        }
        return true;
    }

    private async Task ExportSelectedTeacher()
    {
        if (_teacherBox.SelectedItem is not Teacher teacher) { _status.Text = "Pick a teacher first."; return; }
        if (!TryGetExportSettings(out var firstWeek, out var weeks, out var free, out var lessons)) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save calendar file",
            SuggestedFileName = $"{teacher.Name} timetable.ics",
            DefaultExtension = "ics",
            ShowOverwritePrompt = true,
            FileTypeChoices = new[] { new FilePickerFileType("Calendar file") { Patterns = new[] { "*.ics" } } },
        });
        if (file is null) return;

        var ics = CalendarExport.Build(_school, teacher, firstWeek, weeks, free, lessons);
        await using (var stream = await file.OpenWriteAsync())
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            await writer.WriteAsync(ics);

        _status.Text = $"Saved {file.Name}. Double-click it (or drag it into Apple Calendar / Outlook) to add the events.";
    }

    private async Task ExportAllTeachers()
    {
        if (_school.Teachers.Count == 0) { _status.Text = "No timetables loaded."; return; }
        if (!TryGetExportSettings(out var firstWeek, out var weeks, out var free, out var lessons)) return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose where to save one calendar file per teacher",
            AllowMultiple = false,
        });
        var folder = folders.FirstOrDefault()?.TryGetLocalPath();
        if (folder is null) return;

        foreach (var teacher in _school.Teachers)
        {
            var ics = CalendarExport.Build(_school, teacher, firstWeek, weeks, free, lessons);
            await File.WriteAllTextAsync(Path.Combine(folder, $"{teacher.Name} timetable.ics"), ics, new UTF8Encoding(false));
        }

        _status.Text = $"Saved {_school.Teachers.Count} calendar files to {folder}.";
    }

    private static DateTime NextMonday(DateTime from) =>
        from.DayOfWeek == DayOfWeek.Monday ? from : from.AddDays(((int)DayOfWeek.Monday - (int)from.DayOfWeek + 7) % 7);
}
