using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TimetableChecker;

/// <summary>
/// "School day settings": day start time, number of periods, each period's length,
/// and breaks/lunches. Shows the resulting times as you type. Returns the new settings, or null if cancelled.
/// </summary>
public class SchoolDayWindow : Window
{
    private readonly DaySchedule _schedule;
    private DaySchedule? _result;

    private readonly TextBox _dayStart = new() { Width = 90 };
    private readonly NumericUpDown _periodCount = Number(1, DaySchedule.MaxPeriods, 110);
    private readonly NumericUpDown _defaultLength = Number(5, 300, 110);
    private readonly StackPanel _periodRows = new() { Spacing = 6 };
    private readonly StackPanel _breakRows = new() { Spacing = 6 };
    private readonly TextBlock _preview = new() { FontFamily = new FontFamily("Consolas, Menlo, monospace"), LineHeight = 22 };
    private readonly TextBlock _error = new() { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };

    private bool _building; // stops change events firing while rows are being rebuilt

    public SchoolDayWindow(DaySchedule current)
    {
        _schedule = current.Clone();

        Title = "School day settings";
        Width = 760;
        Height = 680;
        MinWidth = 600;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _dayStart.Text = _schedule.DayStartText;
        _periodCount.Value = _schedule.Periods.Count;
        _defaultLength.Value = _schedule.Periods.Count > 0 ? _schedule.Periods[0].LengthMinutes : 60;

        Content = BuildLayout();
        RebuildPeriodRows();
        RebuildBreakRows();
        UpdatePreview();

        _dayStart.TextChanged += (_, _) => UpdatePreview();
        _periodCount.ValueChanged += (_, _) => ChangePeriodCount();
    }

    /// <summary>Shows the window; returns the new settings, or null if cancelled.</summary>
    public async Task<DaySchedule?> ShowAsync(Window owner)
    {
        await ShowDialog(owner);
        return _result;
    }

    private static NumericUpDown Number(int min, int max, double width) =>
        new() { Minimum = min, Maximum = max, Increment = 1, FormatString = "0", Width = width };

    private Control BuildLayout()
    {
        var setAll = new Button { Content = "Make every period this long", VerticalAlignment = VerticalAlignment.Bottom };
        setAll.Click += (_, _) =>
        {
            int length = (int)(_defaultLength.Value ?? 60);
            foreach (var p in _schedule.Periods) p.LengthMinutes = length;
            RebuildPeriodRows();
            UpdatePreview();
        };

        var addBreak = new Button { Content = "+ Add break or lunch" };
        addBreak.Click += (_, _) =>
        {
            int after = Math.Max(1, Math.Min(_schedule.Periods.Count - 1, (_schedule.Breaks.LastOrDefault()?.AfterPeriod ?? 0) + 1));
            _schedule.Breaks.Add(new DaySchedule.BreakSpec { Name = _schedule.Breaks.Count == 0 ? "Break" : "Lunch", AfterPeriod = after, LengthMinutes = 20 });
            RebuildBreakRows();
            UpdatePreview();
        };

        var top = new WrapPanel();
        top.Children.Add(Labelled("School day starts at", _dayStart));
        top.Children.Add(Labelled("Number of periods", _periodCount));
        top.Children.Add(Labelled("Period length (minutes)", _defaultLength));
        top.Children.Add(new StackPanel { Children = { setAll }, Margin = new Thickness(0, 0, 0, 8), VerticalAlignment = VerticalAlignment.Bottom });

        var left = new StackPanel { Spacing = 10 };
        left.Children.Add(Heading("Periods"));
        left.Children.Add(Hint("Give a period a different length here if it isn't the same as the others."));
        left.Children.Add(_periodRows);
        left.Children.Add(Heading("Breaks and lunches", top: 12));
        left.Children.Add(Hint("Each one comes straight after the period you choose."));
        left.Children.Add(_breakRows);
        left.Children.Add(addBreak);

        var right = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#808080"), 0.10),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new StackPanel { Spacing = 8, Children = { Heading("The day will look like this"), _preview } },
        };

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,20,Auto") };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 2);
        columns.Children.Add(left);
        columns.Children.Add(right);

        var cancel = new Button { Content = "Cancel", MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close();
        var save = new Button { Content = "Save", MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center, Classes = { "accent" } };
        save.Click += (_, _) => Save();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, save } };

        var footer = new StackPanel { Spacing = 8, Margin = new Thickness(20, 8, 20, 16), Children = { _error, buttons } };
        var body = new StackPanel { Spacing = 12, Margin = new Thickness(20, 16, 20, 8), Children = { top, columns } };

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = body });
        return root;
    }

    private static TextBlock Heading(string text, double top = 0) =>
        new() { Text = text, FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, top, 0, 0) };

    private static TextBlock Hint(string text) =>
        new() { Text = text, Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap };

    private static Control Labelled(string label, Control control) => new StackPanel
    {
        Spacing = 4,
        Margin = new Thickness(0, 0, 16, 8),
        Children = { new TextBlock { Text = label, Opacity = 0.8 }, control },
    };

    // ---------- Rows ----------

    private void ChangePeriodCount()
    {
        if (_building) return;
        int count = (int)(_periodCount.Value ?? 1);
        int length = (int)(_defaultLength.Value ?? 60);

        while (_schedule.Periods.Count < count)
            _schedule.Periods.Add(new DaySchedule.PeriodSpec { Name = UnusedName(_schedule.Periods.Count + 1), LengthMinutes = length });
        while (_schedule.Periods.Count > count)
            _schedule.Periods.RemoveAt(_schedule.Periods.Count - 1);

        // Breaks after the (new) last period don't make sense any more
        _schedule.Breaks.RemoveAll(b => b.AfterPeriod >= count);

        RebuildPeriodRows();
        RebuildBreakRows();
        UpdatePreview();
    }

    private string UnusedName(int number)
    {
        var name = $"P{number}";
        while (_schedule.Periods.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name += "'";
        return name;
    }

    private void RebuildPeriodRows()
    {
        _building = true;
        _periodRows.Children.Clear();

        foreach (var spec in _schedule.Periods)
        {
            var name = new TextBox { Text = spec.Name, Width = 110 };
            var length = Number(5, 300, 120);
            length.Value = spec.LengthMinutes;

            name.TextChanged += (_, _) => { spec.Name = (name.Text ?? "").Trim(); UpdatePreview(); };
            length.ValueChanged += (_, _) => { spec.LengthMinutes = (int)(length.Value ?? 60); UpdatePreview(); };

            _periodRows.Children.Add(Row(name, length, new TextBlock { Text = "minutes", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 }));
        }

        _building = false;
    }

    private void RebuildBreakRows()
    {
        _building = true;
        _breakRows.Children.Clear();

        if (_schedule.Periods.Count < 2)
            _breakRows.Children.Add(Hint("Add a second period to have breaks between them."));

        foreach (var spec in _schedule.Breaks.ToList())
        {
            var name = new TextBox { Text = spec.Name, Width = 110 };
            var after = new ComboBox
            {
                Width = 130,
                ItemsSource = _schedule.Periods.Take(_schedule.Periods.Count - 1).Select(p => $"after {p.Name}").ToList(),
                SelectedIndex = spec.AfterPeriod - 1,
            };
            var length = Number(1, 300, 120);
            length.Value = spec.LengthMinutes;
            var remove = new Button { Content = "✕", Padding = new Thickness(10, 4) };
            ToolTip.SetTip(remove, "Remove this break");

            name.TextChanged += (_, _) => { spec.Name = (name.Text ?? "").Trim(); UpdatePreview(); };
            after.SelectionChanged += (_, _) => { if (after.SelectedIndex >= 0) spec.AfterPeriod = after.SelectedIndex + 1; UpdatePreview(); };
            length.ValueChanged += (_, _) => { spec.LengthMinutes = (int)(length.Value ?? 20); UpdatePreview(); };
            remove.Click += (_, _) => { _schedule.Breaks.Remove(spec); RebuildBreakRows(); UpdatePreview(); };

            _breakRows.Children.Add(Row(name, after, length, new TextBlock { Text = "minutes", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 }, remove));
        }

        _building = false;
    }

    private static StackPanel Row(params Control[] controls)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var c in controls) row.Children.Add(c);
        return row;
    }

    // ---------- Preview and saving ----------

    /// <summary>Reads the day start box; returns an error message if it isn't a time.</summary>
    private string? ReadDayStart()
    {
        var start = School.ParseTime(_dayStart.Text ?? "");
        if (start is null) return "The start of the day must be a time like 08:50.";
        _schedule.DayStart = start.Value;
        return null;
    }

    private void UpdatePreview()
    {
        if (_building) return;
        var problem = ReadDayStart() ?? _schedule.Validate();
        _error.Text = problem ?? "";
        if (problem is not null) return;

        var (periods, breaks) = _schedule.Build();
        var lines = periods.Select(p => (p.Start!.Value, $"{p.Start:hh\\:mm}–{p.End:hh\\:mm}  {p.Name}"))
            .Concat(breaks.Select(b => (b.Start, $"{b.Start:hh\\:mm}–{b.End:hh\\:mm}  {b.Name}")))
            .OrderBy(x => x.Item1)
            .Select(x => x.Item2);
        var end = breaks.Select(b => b.End).Append(periods[^1].End!.Value).Max();
        _preview.Text = string.Join("\n", lines) + $"\n\nDay ends at {end:hh\\:mm}";
    }

    private void Save()
    {
        var problem = ReadDayStart() ?? _schedule.Validate();
        if (problem is not null) { _error.Text = problem; return; }

        foreach (var p in _schedule.Periods) p.Name = p.Name.Trim();
        foreach (var b in _schedule.Breaks) b.Name = b.Name.Trim();
        _schedule.Breaks.Sort((a, b) => a.AfterPeriod.CompareTo(b.AfterPeriod));
        _result = _schedule;
        Close();
    }
}
