using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace TimetableChecker;

/// <summary>
/// The main window: a sidebar on the left and one of three pages on the right.
///
///   Who's free      – pick a day and a period on the "day ribbon" to see who is free
///   Timetables      – one teacher's week; add, rename, edit and remove teachers
///   Calendar export – save .ics files for Apple Calendar / Outlook / Google Calendar
///
/// How it fits together: the window keeps a few pieces of STATE (which day, period and teacher
/// are selected). When something changes, the Refresh...() methods rebuild the controls on screen
/// from that state. The look of everything (colours, sizes) is in App.axaml – here we only
/// give controls style "classes", like class="..." in HTML.
/// </summary>
public class MainWindow : Window
{
    // ======================= State =======================

    private School _school = new();     // every teacher's timetable (see School.cs)
    private string? _day;               // the day picked on the Who's free page
    private Period? _period;            // the period picked on the Who's free page
    private BreakSlot? _breakNow;       // set when "Right now" lands in a break or lunch
    private Teacher? _teacher;          // the teacher shown on the Timetables page
    private bool _editing;              // is that teacher's timetable being edited?
    private readonly Dictionary<(string Day, string Period), TextBox> _lessonEditors = new();

    // ======================= Controls =======================

    // The sidebar and the area the current page is shown in
    private readonly ContentControl _pageHost = new();
    private readonly Dictionary<string, Button> _navButtons = new();
    private readonly Dictionary<string, Control> _pages = new();
    private readonly TextBlock _folderName = new() { Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap };
    private readonly Border _statusBar = new() { Classes = { "statusbar" }, IsVisible = false };
    private readonly TextBlock _status = new() { Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap };

    // Who's free page
    private readonly StackPanel _dayButtons = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly Grid _ribbon = new() { Height = 58 };
    private readonly TextBlock _answer = new() { Classes = { "h2" }, TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel _freeBadges = new();
    private readonly TextBlock _nonContactHeading = new() { Classes = { "h3" }, Margin = new Thickness(0, 12, 0, 0) };
    private readonly WrapPanel _nonContactBadges = new();
    private readonly TextBlock _teachingHeading = new() { Classes = { "h3" }, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Grid _teachingTable = new() { ColumnDefinitions = new ColumnDefinitions("Auto,24,*") };

    // Timetables page
    private readonly TextBox _search = new() { Watermark = "Find a teacher" };
    private readonly ListBox _teacherList = new();
    private readonly TextBlock _teacherName = new() { Classes = { "h1" }, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _teacherSummary = new() { Classes = { "muted" } };
    private readonly TextBlock _teacherContact = new() { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap };
    private readonly Button _contactButton = new() { Content = "Contact details…" };
    private readonly Grid _grid = new();
    private readonly TextBlock _gridHint = new() { Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Button _editButton = new() { Content = "Edit timetable" };
    private readonly Button _cancelEditButton = new() { Content = "Cancel", IsVisible = false };
    private readonly Button _renameButton = new() { Content = "Rename…" };
    private readonly Button _removeButton = new() { Content = "Remove…", Classes = { "danger" } };
    private readonly StackPanel _teacherToolbar = new() { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

    // Calendar export page
    private readonly ComboBox _exportTeacher = new() { MinWidth = 260 };
    private readonly CalendarDatePicker _startDate = new() { Width = 180, SelectedDate = NextMonday(DateTime.Today) };
    private readonly NumericUpDown _weeks = new() { Value = 13, Minimum = 1, Maximum = 52, Increment = 1, FormatString = "0", Width = 140 };
    private readonly CheckBox _includeFree = new() { Content = "Free periods", IsChecked = true };
    private readonly CheckBox _includeLessons = new() { Content = "Lessons", IsChecked = true };
    private readonly CheckBox _includeNonContact = new() { Content = "Non-contact time", IsChecked = true };

    public MainWindow()
    {
        Title = "Timetable Checker";
        Width = 1180;
        Height = 760;
        MinWidth = 900;
        MinHeight = 560;

        _pages["free"] = BuildFreePage();
        _pages["timetables"] = BuildTimetablesPage();
        _pages["export"] = BuildExportPage();
        Content = BuildShell();

        // Events: "when this happens, run this code". (_, _) => ... is a lambda – a small unnamed method.
        _search.TextChanged += (_, _) => RefreshTeacherList();
        _teacherList.SelectionChanged += (_, _) =>
        {
            if (_teacherList.SelectedItem is Teacher t && t != _teacher)
            {
                _teacher = t;
                RefreshTimetablePage();
            }
        };

        LoadFolder(FindDefaultFolder());
        PickDefaultSlot();
        RefreshFreePage();
        ShowPage("free");
    }

    // ======================= Layout: the shell =======================

    private Control BuildShell()
    {
        var title = new TextBlock { Text = "Timetable Checker", Classes = { "h2" } };

        var nav = new StackPanel { Spacing = 2, Margin = new Thickness(0, 24, 0, 0) };
        nav.Children.Add(NavButton("free", "Who's free"));
        nav.Children.Add(NavButton("timetables", "Timetables"));
        nav.Children.Add(NavButton("export", "Calendar export"));

        var settings = QuietButton("School day settings…", OpenSchoolDaySettings);
        ToolTip.SetTip(settings, "Number of periods, period lengths, breaks and lunches");
        var open = QuietButton("Open timetables folder…", ChooseFolder);
        var reload = QuietButton("Reload", () => { LoadFolder(_school.Folder); return Task.CompletedTask; });

        _folderName.Margin = new Thickness(12, 8, 12, 0);
        var bottom = new StackPanel { Spacing = 2, Children = { settings, open, reload, _folderName } };

        var sidebarContent = new DockPanel { Margin = new Thickness(14, 22, 14, 16) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        sidebarContent.Children.Add(bottom);
        sidebarContent.Children.Add(new StackPanel { Children = { new Border { Padding = new Thickness(12, 0), Child = title }, nav } });

        var sidebar = new Border { Classes = { "sidebar" }, Child = sidebarContent };

        _statusBar.Child = _status;
        var main = new DockPanel();
        DockPanel.SetDock(_statusBar, Dock.Bottom);
        main.Children.Add(_statusBar);
        main.Children.Add(_pageHost);

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*") };
        Grid.SetColumn(main, 1);
        root.Children.Add(sidebar);
        root.Children.Add(main);
        return root;
    }

    private Button NavButton(string page, string text)
    {
        var button = new Button { Content = text, Classes = { "nav" } };
        button.Click += (_, _) => ShowPage(page);
        _navButtons[page] = button;
        return button;
    }

    /// <summary>A plain sidebar button. Func&lt;Task&gt; means "a method that can be awaited".</summary>
    private static Button QuietButton(string text, Func<Task> onClick)
    {
        var button = new Button { Content = text, Classes = { "quiet" } };
        button.Click += async (_, _) => await onClick();
        return button;
    }

    private void ShowPage(string page)
    {
        _pageHost.Content = _pages[page];
        foreach (var (name, button) in _navButtons)
            button.Classes.Set("active", name == page); // the active tab is styled differently in App.axaml
        if (page == "export") RefreshExportPage();
    }

    private void SetStatus(string message, bool isError = false)
    {
        _status.Text = message;
        _status.Classes.Set("error", isError);
        _statusBar.IsVisible = message.Length > 0; // hide the bar when there's nothing to say
    }

    // ======================= Page 1: Who's free =======================

    private Control BuildFreePage()
    {
        var now = new Button { Content = "Right now", Classes = { "accent" }, VerticalAlignment = VerticalAlignment.Center };
        now.Click += (_, _) => ShowRightNow();

        var header = new DockPanel();
        DockPanel.SetDock(now, Dock.Right);
        header.Children.Add(now);
        header.Children.Add(new TextBlock { Text = "Who's free", Classes = { "h1" } });

        var page = new StackPanel { Spacing = 14, Margin = new Thickness(32, 28, 32, 28) };
        page.Children.Add(header);
        page.Children.Add(_dayButtons);
        page.Children.Add(_ribbon);
        page.Children.Add(new Border { Height = 8 });
        page.Children.Add(_answer);
        page.Children.Add(_freeBadges);
        page.Children.Add(_nonContactHeading);
        page.Children.Add(_nonContactBadges);
        page.Children.Add(_teachingHeading);
        page.Children.Add(_teachingTable);
        return new ScrollViewer { Content = page };
    }

    /// <summary>Chooses today and the period happening now, or the first day and period.</summary>
    private void PickDefaultSlot()
    {
        var slot = _school.SlotAt(DateTime.Now);
        _day = slot?.Day
               ?? _school.Days.FirstOrDefault(d => School.ToDayOfWeek(d) == DateTime.Today.DayOfWeek)
               ?? _school.Days.FirstOrDefault();
        _period = slot?.Period ?? _school.Periods.FirstOrDefault();
    }

    private void ShowRightNow()
    {
        var now = DateTime.Now;
        var slot = _school.SlotAt(now);
        _breakNow = slot is null ? _school.BreakAt(now) : null;

        if (slot is not null)
        {
            (_day, _period) = slot.Value; // "deconstructs" the pair into the two fields
        }
        else if (_breakNow is not null)
        {
            _day = _school.Days.First(d => School.ToDayOfWeek(d) == now.DayOfWeek);
            _period = null;
        }
        else
        {
            SetStatus("There are no lessons right now – it's outside the school day.");
            return;
        }
        SetStatus("");
        RefreshFreePage();
    }

    private void RefreshFreePage()
    {
        // --- Day buttons ---
        _dayButtons.Children.Clear();
        foreach (var day in _school.Days)
        {
            var button = new Button { Content = day, Classes = { "chip" } };
            button.Classes.Set("selected", day == _day && _breakNow is null);
            button.Click += (_, _) => { _day = day; _breakNow = null; RefreshFreePage(); };
            _dayButtons.Children.Add(button);
        }

        RefreshRibbon();

        // --- The answer ---
        _freeBadges.Children.Clear();
        _nonContactBadges.Children.Clear();
        _nonContactHeading.IsVisible = false;
        _teachingTable.Children.Clear();
        _teachingTable.RowDefinitions.Clear();
        _teachingHeading.IsVisible = false;

        if (_school.Teachers.Count == 0)
        {
            _answer.Text = "No timetables yet. Add a teacher on the Timetables page, or open a folder of timetables.";
            return;
        }
        if (_breakNow is not null)
        {
            _answer.Text = $"It's {_breakNow.Name} until {_breakNow.End:hh\\:mm}, so everyone is free.";
            return;
        }
        if (_day is null || _period is null)
        {
            _answer.Text = "Pick a day and a period.";
            return;
        }

        var free = _school.FreeTeachers(_day, _period.Name).ToList();
        var nonContact = _school.NonContactTeachers(_day, _period.Name).ToList();
        var busy = _school.BusyTeachers(_day, _period.Name).ToList();
        var when = _period.HasTimes ? $"{_day}, {_period.Name} ({_period.Start:hh\\:mm}–{_period.End:hh\\:mm})" : $"{_day}, {_period.Name}";

        // A "switch expression": picks the sentence that matches the number of free teachers
        _answer.Text = free.Count switch
        {
            0 when nonContact.Count > 0 => $"Nobody is free on {when}, but {nonContact.Count} {(nonContact.Count == 1 ? "teacher has" : "teachers have")} non-contact time.",
            0 => $"Everyone is teaching on {when}.",
            1 => $"1 teacher is free on {when}",
            _ => $"{free.Count} teachers are free on {when}",
        };

        foreach (var teacher in free)
            _freeBadges.Children.Add(TeacherBadge(teacher, "free", null));

        // Non-contact: not teaching, but the time is set aside, so they're shown separately
        _nonContactHeading.IsVisible = nonContact.Count > 0;
        _nonContactHeading.Text = $"Non-contact ({nonContact.Count})";
        foreach (var teacher in nonContact)
            _nonContactBadges.Children.Add(TeacherBadge(teacher, "noncontact", teacher.LessonAt(_day, _period.Name)));

        // Who's teaching, and what – useful for knowing which class needs covering
        _teachingHeading.IsVisible = busy.Count > 0;
        _teachingHeading.Text = $"Teaching ({busy.Count})";
        for (int i = 0; i < busy.Count; i++)
        {
            _teachingTable.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = new TextBlock { Text = busy[i].Name, Margin = new Thickness(0, 4) };
            var lesson = new TextBlock { Text = busy[i].LessonAt(_day, _period.Name), Classes = { "muted" }, Margin = new Thickness(0, 4) };
            Grid.SetRow(name, i);
            Grid.SetRow(lesson, i);
            Grid.SetColumn(lesson, 2);
            _teachingTable.Children.Add(name);
            _teachingTable.Children.Add(lesson);
        }
    }

    /// <summary>
    /// A teacher's name badge with an email link underneath. Clicking the name opens their timetable;
    /// the link opens an email asking them to cover this period.
    /// </summary>
    private Control TeacherBadge(Teacher teacher, string kind, string? note)
    {
        var badge = new Button { Content = teacher.Name, Classes = { "badge", kind } };
        ToolTip.SetTip(badge, note is null ? $"Open {teacher.Name}'s timetable" : $"{note} – open {teacher.Name}'s timetable");
        badge.Click += (_, _) => OpenTeacher(teacher);

        Button link;
        if (teacher.Contact.Email.Length > 0)
        {
            link = LinkButton("Email");
            ToolTip.SetTip(link, $"Email {teacher.Contact.Email} asking them to cover");
            link.Click += async (_, _) => await EmailTeacher(teacher);
        }
        else
        {
            link = LinkButton("Add email");
            ToolTip.SetTip(link, $"No email saved for {teacher.Name} yet");
            link.Click += async (_, _) => await EditContact(teacher);
        }

        return new StackPanel
        {
            Margin = new Thickness(0, 0, 10, 10),
            HorizontalAlignment = HorizontalAlignment.Left,
            Children = { badge, link },
        };
    }

    private static Button LinkButton(string text) => new()
    {
        Classes = { "link" },
        Content = new TextBlock { Text = text, TextDecorations = TextDecorations.Underline },
    };

    /// <summary>Opens the computer's email app with a cover request to this teacher already written.</summary>
    private async Task EmailTeacher(Teacher teacher)
    {
        if (_day is null || _period is null) return;
        var link = Contacts.CoverRequestLink(teacher, _day, _period);
        bool opened = await Launcher.LaunchUriAsync(link);
        if (opened)
        {
            SetStatus($"Opened an email to {teacher.Name}.");
        }
        else
        {
            // No email app set up: copy the address instead, so it can be pasted into webmail
            if (Clipboard is not null) await Clipboard.SetTextAsync(teacher.Contact.Email);
            SetStatus($"Couldn't open an email app, so {teacher.Contact.Email} has been copied – paste it into your email.", isError: true);
        }
    }

    /// <summary>
    /// The day ribbon: the school day drawn as a strip, where each period's width matches how long it is,
    /// and breaks/lunches are the gaps in between. It uses a Grid whose columns have "star" widths:
    /// a 60-minute period gets 60*, a 20-minute break gets 20*, so the space is shared out in proportion.
    /// </summary>
    private void RefreshRibbon()
    {
        _ribbon.Children.Clear();
        _ribbon.ColumnDefinitions.Clear();

        var nowSlot = _school.SlotAt(DateTime.Now);
        int column = 0;

        foreach (var (period, breakSlot) in DayRows())
        {
            Control piece;
            if (period is not null)
            {
                _ribbon.ColumnDefinitions.Add(new ColumnDefinition(Minutes(period.Start, period.End), GridUnitType.Star));
                bool isNow = nowSlot is { } s && s.Period == period && s.Day == _day;
                var segment = new Button
                {
                    Classes = { "segment" },
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = period.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                            new TextBlock
                            {
                                Text = isNow ? "Now" : period.HasTimes ? $"{period.Start:hh\\:mm}" : "",
                                FontSize = 12,
                                FontWeight = isNow ? FontWeight.Bold : FontWeight.Normal,
                                Opacity = isNow ? 1 : 0.75,
                            },
                        },
                    },
                };
                segment.Classes.Set("selected", period == _period && _breakNow is null);
                if (period.HasTimes) ToolTip.SetTip(segment, period.ToString());
                segment.Click += (_, _) => { _period = period; _breakNow = null; RefreshFreePage(); };
                piece = segment;
            }
            else
            {
                _ribbon.ColumnDefinitions.Add(new ColumnDefinition(Minutes(breakSlot!.Start, breakSlot.End), GridUnitType.Star));
                piece = new Border { Classes = { "ribbonbreak" }, Child = new TextBlock { Text = breakSlot.Name } };
                ToolTip.SetTip(piece, breakSlot.ToString());
            }

            Grid.SetColumn(piece, column++);
            _ribbon.Children.Add(piece);
        }
    }

    private static double Minutes(TimeSpan? start, TimeSpan? end) =>
        start is { } s && end is { } e && e > s ? (e - s).TotalMinutes : 60;

    /// <summary>
    /// The school day in order: each period, followed by any break or lunch that comes straight after it.
    /// Each item is a pair where exactly one side is filled in.
    /// </summary>
    private List<(Period? Period, BreakSlot? Break)> DayRows()
    {
        var rows = new List<(Period?, BreakSlot?)>();
        for (int p = 0; p < _school.Periods.Count; p++)
        {
            var period = _school.Periods[p];
            rows.Add((period, null));
            var nextStart = p + 1 < _school.Periods.Count ? _school.Periods[p + 1].Start : null;
            foreach (var b in _school.Breaks.Where(b => period.End is { } end && b.Start >= end && (nextStart is null || b.End <= nextStart)))
                rows.Add((null, b));
        }
        return rows;
    }

    // ======================= Page 2: Timetables =======================

    private Control BuildTimetablesPage()
    {
        var add = new Button { Content = "Add teacher…", Classes = { "accent" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        add.Click += async (_, _) => await AddTeacher();

        var listPane = new DockPanel { Margin = new Thickness(16, 24, 16, 16) };
        DockPanel.SetDock(_search, Dock.Top);
        DockPanel.SetDock(add, Dock.Bottom);
        _search.Margin = new Thickness(0, 0, 0, 10);
        add.Margin = new Thickness(0, 10, 0, 0);
        listPane.Children.Add(_search);
        listPane.Children.Add(add);
        listPane.Children.Add(_teacherList);

        _editButton.Click += (_, _) => ToggleEditing();
        _cancelEditButton.Click += (_, _) => { _editing = false; RefreshTimetablePage(); };
        _renameButton.Click += async (_, _) => await RenameTeacher();
        _removeButton.Click += async (_, _) => await RemoveTeacher();
        _contactButton.Click += async (_, _) => { if (_teacher is not null) await EditContact(_teacher); };
        _teacherToolbar.Children.AddRange(new Control[] { _editButton, _cancelEditButton, _contactButton, _renameButton, _removeButton });

        var titles = new StackPanel { Spacing = 2, Children = { _teacherName, _teacherSummary, _teacherContact } };
        // Name and details first, then the buttons on their own row so they never cover the text
        _teacherToolbar.HorizontalAlignment = HorizontalAlignment.Left;
        var header = new StackPanel { Spacing = 14, Margin = new Thickness(0, 0, 0, 20), Children = { titles, _teacherToolbar } };

        var gridScroller = new ScrollViewer
        {
            Content = _grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, // lets the "*" columns fill the width
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var detail = new DockPanel { Margin = new Thickness(32, 28, 32, 20) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_gridHint, Dock.Bottom);
        detail.Children.Add(header);
        detail.Children.Add(_gridHint);
        detail.Children.Add(gridScroller);

        var page = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*") };
        var left = new Border { Classes = { "listpane" }, Child = listPane };
        Grid.SetColumn(detail, 1);
        page.Children.Add(left);
        page.Children.Add(detail);
        return page;
    }

    private void OpenTeacher(Teacher teacher)
    {
        _teacher = teacher;
        _search.Text = "";
        RefreshTeacherList();
        RefreshTimetablePage();
        ShowPage("timetables");
    }

    private void RefreshTeacherList()
    {
        var term = (_search.Text ?? "").Trim();
        // LINQ: keep only the teachers whose name contains the search text
        var matches = _school.Teachers.Where(t => t.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        _teacherList.ItemsSource = matches;
        _teacherList.SelectedItem = _teacher is not null && matches.Contains(_teacher) ? _teacher : null;
    }

    private void RefreshTimetablePage()
    {
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        _grid.ColumnDefinitions.Clear();
        _lessonEditors.Clear();

        var teacher = _teacher;
        if (teacher is null) _editing = false;

        // The toolbar and the list change depending on whether we're editing
        _teacherToolbar.IsVisible = teacher is not null;
        _editButton.Content = _editing ? "Save changes" : "Edit timetable";
        _editButton.Classes.Set("accent", _editing);
        _cancelEditButton.IsVisible = _editing;
        _renameButton.IsVisible = _removeButton.IsVisible = _contactButton.IsVisible = !_editing;
        _teacherList.IsEnabled = _search.IsEnabled = !_editing;

        if (teacher is null)
        {
            _teacherName.Text = "No teacher selected";
            _teacherSummary.Text = _school.Teachers.Count == 0 ? "Click “Add teacher…” to create the first timetable." : "Pick a teacher from the list.";
            _teacherContact.Text = "";
            _gridHint.Text = "";
            return;
        }

        _teacherName.Text = teacher.Name;
        int slots = _school.Days.Count * _school.Periods.Count;
        // Count how many of each kind of slot this teacher has in a week
        int Count(SlotKind kind) => _school.Days.Sum(d => _school.Periods.Count(p => teacher.KindAt(d, p.Name) == kind));
        int lessons = Count(SlotKind.Lesson), free = Count(SlotKind.Free), nonContact = Count(SlotKind.NonContact);
        _teacherSummary.Text = nonContact > 0
            ? $"{lessons} lessons, {free} free periods and {nonContact} non-contact a week"
            : $"{lessons} lessons and {free} free periods a week";

        var contact = teacher.Contact;
        _teacherContact.Text = contact.IsEmpty
            ? "No contact details yet"
            : string.Join("   ", new[] { contact.Email, contact.Phone }.Where(x => x.Length > 0));
        _gridHint.Text = _editing
            ? "Type the class in each box. Leave it empty for a free period, or type “Non-contact” (or PPA) for non-contact time. Then click “Save changes”. To change times, breaks or the number of periods, use School day settings."
            : "Green cells are free periods. Purple cells are non-contact time.";

        // Column 0 holds the period names, then one column per day
        _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        // "*" columns share the space equally; MinWidth stops them getting too squashed in a small window
        foreach (var _ in _school.Days) _grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star) { MinWidth = 96 });
        _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int d = 0; d < _school.Days.Count; d++)
            AddCell(0, d + 1, new TextBlock { Text = _school.Days[d], FontWeight = FontWeight.SemiBold }, "plain");

        int row = 0;
        foreach (var (period, breakSlot) in DayRows())
        {
            row++;
            if (breakSlot is not null)
            {
                // A thin band across the whole week
                _grid.RowDefinitions.Add(new RowDefinition(new GridLength(30)));
                AddCell(row, 0, new TextBlock { Text = $"{breakSlot.Start:hh\\:mm}–{breakSlot.End:hh\\:mm}", Classes = { "muted", "small" } }, "plain");
                var band = AddCell(row, 1, new TextBlock { Text = breakSlot.Name }, "break");
                Grid.SetColumnSpan(band, Math.Max(1, _school.Days.Count));
                continue;
            }

            _grid.RowDefinitions.Add(new RowDefinition(new GridLength(54)));
            var label = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = period!.Name, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = period.HasTimes ? $"{period.Start:hh\\:mm}–{period.End:hh\\:mm}" : "", Classes = { "muted", "small" } },
                },
            };
            AddCell(row, 0, label, "plain");

            for (int d = 0; d < _school.Days.Count; d++)
            {
                var day = _school.Days[d];
                var lesson = teacher.LessonAt(day, period.Name);

                if (_editing)
                {
                    var box = new TextBox { Text = lesson ?? "", Watermark = "Free", Margin = new Thickness(2), VerticalContentAlignment = VerticalAlignment.Center };
                    Grid.SetRow(box, row);
                    Grid.SetColumn(box, d + 1);
                    _grid.Children.Add(box);
                    _lessonEditors[(day, period.Name)] = box;
                }
                else
                {
                    var kind = teacher.KindAt(day, period.Name) switch
                    {
                        SlotKind.Free => "free",
                        SlotKind.NonContact => "noncontact",
                        _ => "lesson",
                    };
                    AddCell(row, d + 1, new TextBlock { Text = lesson ?? "Free" }, kind);
                }
            }
        }
    }

    /// <summary>Adds a rounded cell to the timetable grid. Its colour comes from its class (free, lesson, break or plain).</summary>
    private Border AddCell(int row, int column, Control content, string kind)
    {
        var cell = new Border { Classes = { "cell", kind }, Child = content };
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        _grid.Children.Add(cell);
        return cell;
    }

    // ======================= Page 3: Calendar export =======================

    private Control BuildExportPage()
    {
        var exportOne = new Button { Content = "Export this teacher…", Classes = { "accent" } };
        exportOne.Click += async (_, _) => await ExportSelectedTeacher();
        var exportAll = new Button { Content = "Export every teacher…" };
        exportAll.Click += async (_, _) => await ExportAllTeachers();

        var options = new WrapPanel();
        options.Children.Add(Field("Teacher", _exportTeacher));
        options.Children.Add(Field("First week", _startDate));
        options.Children.Add(Field("Repeat for (weeks)", _weeks));
        options.Children.Add(Field("Include", new StackPanel { Children = { _includeFree, _includeNonContact, _includeLessons } }));

        var page = new StackPanel { Spacing = 14, Margin = new Thickness(32, 28, 32, 28), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        page.Children.Add(new TextBlock { Text = "Add a timetable to a calendar", Classes = { "h1" } });
        page.Children.Add(new TextBlock
        {
            Text = "Each period becomes an event that repeats every week, starting from the first week you choose. " +
                   "Free periods are marked as available, so they won't block your calendar.",
            TextWrapping = TextWrapping.Wrap,
            Classes = { "muted" },
        });
        page.Children.Add(options);
        page.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { exportOne, exportAll } });

        page.Children.Add(new TextBlock { Text = "Opening the file", Classes = { "h3" }, Margin = new Thickness(0, 16, 0, 0) });
        var howTo = new[]
        {
            ("Apple Calendar", "double-click the .ics file and choose which calendar to add it to. On an iPhone or iPad, AirDrop or email it to yourself and tap it."),
            ("Outlook", "File → Open & Export → Import/Export → Import an iCalendar file."),
            ("Google Calendar", "Settings → Import & export → Import."),
        };
        foreach (var (app, how) in howTo)
        {
            var line = new TextBlock { TextWrapping = TextWrapping.Wrap };
            line.Inlines!.Add(new Run(app) { FontWeight = FontWeight.SemiBold });
            line.Inlines.Add(new Run(": " + how));
            page.Children.Add(line);
        }
        return new ScrollViewer { Content = page };
    }

    private static Control Field(string label, Control control) => new StackPanel
    {
        Spacing = 6,
        Margin = new Thickness(0, 0, 20, 12),
        Children = { new TextBlock { Text = label, Classes = { "muted" } }, control },
    };

    private void RefreshExportPage()
    {
        _exportTeacher.ItemsSource = _school.Teachers.ToList();
        _exportTeacher.SelectedItem = _teacher ?? _school.Teachers.FirstOrDefault();
    }

    // ======================= Loading the timetables folder =======================

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
        if (_editing) { SetStatus("Save or cancel your changes first."); return; }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the folder containing the teachers' timetable CSV files",
            AllowMultiple = false,
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null) LoadFolder(path);
    }

    /// <summary>Loads every timetable in a folder, keeping the current selections where they still exist.</summary>
    private void LoadFolder(string? folder, string? selectTeacher = null)
    {
        _editing = false;
        var oldPeriodIndex = _period is null ? 0 : Math.Max(0, _school.Periods.IndexOf(_period));
        selectTeacher ??= _teacher?.Name; // ??= means "only if it's null"

        if (folder is null || !Directory.Exists(folder))
        {
            _school = new School();
            _folderName.Text = "No timetables folder open";
            ToolTip.SetTip(_folderName, null);
        }
        else
        {
            _school = School.Load(folder);
            _folderName.Text = $"{Path.GetFileName(folder)} folder, {_school.Teachers.Count} teachers";
            ToolTip.SetTip(_folderName, folder);
            RememberFolder(folder);
        }

        // Keep the same day/period/teacher if they still exist, otherwise fall back sensibly
        _day = _school.Days.FirstOrDefault(d => d == _day) ?? _school.Days.FirstOrDefault();
        _period = _school.Periods.FirstOrDefault(p => p.Name == _period?.Name)
                  ?? (_school.Periods.Count > 0 ? _school.Periods[Math.Min(oldPeriodIndex, _school.Periods.Count - 1)] : null);
        _teacher = _school.Teachers.FirstOrDefault(t => t.Name == selectTeacher) ?? _school.Teachers.FirstOrDefault();
        _breakNow = null;

        if (_school.Warnings.Count > 0)
            SetStatus("Some files could not be read: " + string.Join("  ", _school.Warnings), isError: true);
        else
            SetStatus("");

        RefreshFreePage();
        RefreshTeacherList();
        RefreshTimetablePage();
        RefreshExportPage();
    }

    // ======================= Adding, editing and removing teachers =======================

    private async Task AddTeacher()
    {
        if (_editing) { SetStatus("Save or cancel your changes first."); return; }
        var name = await Dialogs.AskText(this, "Add teacher", "Teacher's name (this is also the CSV file name):", "",
            text => _school.CheckName(text));
        if (name is null) return;

        try
        {
            var teacher = _school.AddTeacher(_school.Folder ?? DocumentsFolder, name);
            LoadFolder(_school.Folder, teacher.Name);
            _editing = true; // jump straight into filling in their timetable
            RefreshTimetablePage();
            SetStatus($"Added {teacher.Name}. Fill in their lessons, then click “Save changes”.");
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't add {name}: {ex.Message}", isError: true);
        }
    }

    private async Task RenameTeacher()
    {
        if (_teacher is not { } teacher) return;
        var name = await Dialogs.AskText(this, "Rename teacher", $"New name for {teacher.Name}:", teacher.Name,
            text => _school.CheckName(text, except: teacher));
        if (name is null || name == teacher.Name) return;

        try
        {
            var oldName = teacher.Name;
            _school.RenameTeacher(teacher, name);
            LoadFolder(_school.Folder, name);
            SetStatus($"Renamed {oldName} to {name}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't rename {teacher.Name}: {ex.Message}", isError: true);
        }
    }

    private async Task RemoveTeacher()
    {
        if (_teacher is not { } teacher) return;
        bool sure = await Dialogs.Confirm(this, "Remove teacher",
            $"Remove {teacher.Name}? This deletes their timetable file:\n{teacher.FilePath}", "Remove");
        if (!sure) return;

        try
        {
            _school.RemoveTeacher(teacher);
            _teacher = null;
            LoadFolder(_school.Folder);
            SetStatus($"Removed {teacher.Name}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't remove {teacher.Name}: {ex.Message}", isError: true);
        }
    }

    private async Task EditContact(Teacher teacher)
    {
        if (_editing) { SetStatus("Save or cancel your changes first."); return; }
        var result = await Dialogs.EditContact(this, teacher.Name, teacher.Contact);
        if (result is not { } details) return;

        try
        {
            _school.SetContact(teacher, details.Email, details.Phone);
            SetStatus($"Saved contact details for {teacher.Name}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't save contact details for {teacher.Name}: {ex.Message}", isError: true);
        }
        RefreshFreePage();
        RefreshTimetablePage();
    }

    private void ToggleEditing()
    {
        if (_teacher is not { } teacher) return;
        if (!_editing)
        {
            _editing = true;
            RefreshTimetablePage();
            return;
        }

        try
        {
            foreach (var ((day, period), box) in _lessonEditors) teacher.SetLesson(day, period, box.Text ?? "");
            _school.Save(teacher);
            _editing = false;
            SetStatus($"Saved {teacher.Name}'s timetable.");
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't save {teacher.Name}'s timetable: {ex.Message}", isError: true);
        }

        RefreshFreePage();
        RefreshTimetablePage();
    }

    // ======================= School day settings =======================

    private async Task OpenSchoolDaySettings()
    {
        if (_editing) { SetStatus("Save or cancel your changes first."); return; }

        var schedule = await new SchoolDayWindow(_school.Schedule).ShowAsync(this);
        if (schedule is null) return;

        int lost = _school.LessonsLostBy(schedule);
        if (lost > 0)
        {
            var removed = string.Join(", ", _school.Periods.Skip(schedule.Periods.Count).Select(p => p.Name));
            bool sure = await Dialogs.Confirm(this, "Remove periods?",
                $"Going down to {schedule.Periods.Count} periods removes {removed} from every timetable. " +
                $"{lost} lesson{(lost == 1 ? "" : "s")} in {(removed.Contains(',') ? "those periods" : "that period")} will be deleted.",
                "Remove them");
            if (!sure) return;
        }

        try
        {
            if (_school.Folder is null)
            {
                // Nothing loaded yet: start a timetables folder in Documents so the settings have somewhere to live
                Directory.CreateDirectory(DocumentsFolder);
                LoadFolder(DocumentsFolder);
            }
            _school.ApplySchedule(schedule);
            LoadFolder(_school.Folder);
            SetStatus($"Saved the school day: {_school.Periods.Count} periods and {_school.Breaks.Count} break{(_school.Breaks.Count == 1 ? "" : "s")}. Every teacher's timetable has been updated.");
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't save the school day settings: {ex.Message}", isError: true);
        }
    }

    // ======================= Calendar export =======================

    private bool TryGetExportSettings(out DateTime firstWeek, out int weeks, out bool free, out bool lessons, out bool nonContact)
    {
        firstWeek = _startDate.SelectedDate ?? DateTime.Today;
        weeks = (int)(_weeks.Value ?? 13);
        free = _includeFree.IsChecked == true;
        lessons = _includeLessons.IsChecked == true;
        nonContact = _includeNonContact.IsChecked == true;

        if (!free && !lessons && !nonContact)
        {
            SetStatus("Tick at least one of “Free periods”, “Non-contact time” or “Lessons” before exporting.", isError: true);
            return false;
        }
        if (!_school.Periods.Any(p => p.HasTimes))
        {
            SetStatus("Periods need start and end times to go in a calendar. Set them in School day settings.", isError: true);
            return false;
        }
        return true;
    }

    private async Task ExportSelectedTeacher()
    {
        if (_exportTeacher.SelectedItem is not Teacher teacher) { SetStatus("Pick a teacher to export.", isError: true); return; }
        if (!TryGetExportSettings(out var firstWeek, out var weeks, out var free, out var lessons, out var nonContact)) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save calendar file",
            SuggestedFileName = $"{teacher.Name} timetable.ics",
            DefaultExtension = "ics",
            ShowOverwritePrompt = true,
            FileTypeChoices = new[] { new FilePickerFileType("Calendar file") { Patterns = new[] { "*.ics" } } },
        });
        if (file is null) return;

        var ics = CalendarExport.Build(_school, teacher, firstWeek, weeks, free, lessons, nonContact);
        await using (var stream = await file.OpenWriteAsync())
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            await writer.WriteAsync(ics);

        SetStatus($"Saved {file.Name}. Open it to add the events to your calendar.");
    }

    private async Task ExportAllTeachers()
    {
        if (_school.Teachers.Count == 0) { SetStatus("There are no timetables to export.", isError: true); return; }
        if (!TryGetExportSettings(out var firstWeek, out var weeks, out var free, out var lessons, out var nonContact)) return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose where to save one calendar file per teacher",
            AllowMultiple = false,
        });
        var folder = folders.FirstOrDefault()?.TryGetLocalPath();
        if (folder is null) return;

        foreach (var teacher in _school.Teachers)
        {
            var ics = CalendarExport.Build(_school, teacher, firstWeek, weeks, free, lessons, nonContact);
            await File.WriteAllTextAsync(Path.Combine(folder, $"{teacher.Name} timetable.ics"), ics, new UTF8Encoding(false));
        }

        SetStatus($"Saved {_school.Teachers.Count} calendar files to {folder}.");
    }

    private static DateTime NextMonday(DateTime from) =>
        from.AddDays(((int)DayOfWeek.Monday - (int)from.DayOfWeek + 7) % 7);
}
