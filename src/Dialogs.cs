using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace TimetableChecker;

/// <summary>Small pop-up windows: ask for some text, or ask yes/no.</summary>
public static class Dialogs
{
    /// <summary>Asks for a line of text. <paramref name="validate"/> returns an error message, or null if OK.</summary>
    public static async Task<string?> AskText(Window owner, string title, string prompt, string initial,
                                              Func<string, string?> validate)
    {
        var input = new TextBox { Text = initial, Watermark = "e.g. Mr Smith" };
        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
        string? result = null;

        var dialog = MakeWindow(title);
        void Submit()
        {
            var message = validate(input.Text ?? "");
            if (message is not null) { error.Text = message; return; }
            result = (input.Text ?? "").Trim();
            dialog.Close();
        }

        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) Submit(); };
        dialog.Content = Layout(prompt, input, error, ("Cancel", dialog.Close), ("OK", Submit));
        dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); };

        await dialog.ShowDialog(owner);
        return result;
    }

    /// <summary>Asks for a teacher's email and phone number. Returns null if cancelled.</summary>
    public static async Task<(string Email, string Phone)?> EditContact(Window owner, string teacherName, ContactInfo current)
    {
        var email = new TextBox { Text = current.Email, Watermark = "e.g. j.smith@school.org" };
        var phone = new TextBox { Text = current.Phone, Watermark = "Optional, e.g. extension 214" };
        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
        (string, string)? result = null;

        var dialog = MakeWindow($"Contact details for {teacherName}");
        void Submit()
        {
            var message = Contacts.CheckEmail(email.Text ?? "");
            if (message is not null) { error.Text = message; return; }
            result = ((email.Text ?? "").Trim(), (phone.Text ?? "").Trim());
            dialog.Close();
        }

        var fields = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "Email" }, email,
                new TextBlock { Text = "Phone", Margin = new Thickness(0, 6, 0, 0) }, phone,
            },
        };
        email.KeyDown += (_, e) => { if (e.Key == Key.Enter) Submit(); };
        phone.KeyDown += (_, e) => { if (e.Key == Key.Enter) Submit(); };
        dialog.Content = Layout($"How can {teacherName} be contacted? Leave a box empty to remove it.", fields, error,
            ("Cancel", dialog.Close), ("Save", Submit));
        dialog.Opened += (_, _) => email.Focus();

        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<bool> Confirm(Window owner, string title, string message, string yesText)
    {
        bool result = false;
        var dialog = MakeWindow(title);
        dialog.Content = Layout(message, null, null, ("Cancel", dialog.Close), (yesText, () => { result = true; dialog.Close(); }));
        await dialog.ShowDialog(owner);
        return result;
    }

    private static Window MakeWindow(string title) => new()
    {
        Title = title,
        Width = 420,
        SizeToContent = SizeToContent.Height,
        CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        ShowInTaskbar = false,
    };

    private static Control Layout(string message, Control? input, Control? error, params (string Text, Action Click)[] buttons)
    {
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        if (input is not null) panel.Children.Add(input);
        if (error is not null) panel.Children.Add(error);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (text, click) in buttons)
        {
            var button = new Button { Content = text, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            button.Click += (_, _) => click();
            row.Children.Add(button);
        }
        panel.Children.Add(row);
        return panel;
    }
}
