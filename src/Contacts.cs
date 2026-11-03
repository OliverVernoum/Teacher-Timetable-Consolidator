using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TimetableChecker;

/// <summary>A teacher's contact details. Empty strings mean "not given".</summary>
public class ContactInfo
{
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";

    [JsonIgnore] // worked out from the other two, so it isn't saved in the file
    public bool IsEmpty => Email.Length == 0 && Phone.Length == 0;
}

/// <summary>
/// Saves and loads contacts.json in the timetables folder: one entry per teacher, keyed by their name.
/// It's a separate file (not a column in the CSVs) because every .csv in the folder is read as a timetable.
/// </summary>
public static class Contacts
{
    public const string FileName = "contacts.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static Dictionary<string, ContactInfo> Load(string folder)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path)) return new(StringComparer.OrdinalIgnoreCase);
        var loaded = JsonSerializer.Deserialize<Dictionary<string, ContactInfo>>(File.ReadAllText(path), JsonOptions) ?? new();
        return new Dictionary<string, ContactInfo>(loaded, StringComparer.OrdinalIgnoreCase);
    }

    public static void Save(string folder, Dictionary<string, ContactInfo> contacts)
    {
        var toSave = contacts.Where(c => !c.Value.IsEmpty)
                             .OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
                             .ToDictionary(c => c.Key, c => c.Value);
        File.WriteAllText(Path.Combine(folder, FileName), JsonSerializer.Serialize(toSave, JsonOptions));
    }

    /// <summary>Returns a problem with this email address, or null if it's fine (or empty).</summary>
    public static string? CheckEmail(string email)
    {
        email = email.Trim();
        if (email.Length == 0) return null;
        // MailAddress understands the email standard; we also refuse characters that would break a mailto: link
        if (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email || email.IndexOfAny(new[] { ' ', '?', '&', '#', '%', ',' }) >= 0)
            return "That doesn't look like an email address (it should be like name@school.org).";
        return null;
    }

    /// <summary>
    /// Builds a mailto: link that opens the user's email app with the address, subject and message filled in.
    /// Text in a link must be "percent-encoded": a space becomes %20, a new line %0D%0A, and so on.
    /// </summary>
    public static Uri CoverRequestLink(Teacher teacher, string day, Period period)
    {
        var when = period.HasTimes ? $"{day} {period.Name} ({period.Start:hh\\:mm}–{period.End:hh\\:mm})" : $"{day} {period.Name}";
        var subject = $"Cover request: {when}";
        var body = $"Hi {teacher.Name},\r\n\r\nWould you be able to cover {when}?\r\n\r\nThanks";
        return new Uri($"mailto:{teacher.Contact.Email}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}");
    }
}
