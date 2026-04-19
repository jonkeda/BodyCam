using System.Text.RegularExpressions;
using System.Windows.Input;
using BodyCam.Models;
using BodyCam.Mvvm;

namespace BodyCam.Services;

/// <summary>
/// Scans transcript text for actionable content (URLs, phone numbers, emails)
/// and produces <see cref="ContentAction"/> buttons.
/// </summary>
public static partial class ContentActionDetector
{
    [GeneratedRegex(@"https?://[^\s""<>\)\]]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"mailto:([^\s""<>\)\]]+)", RegexOptions.IgnoreCase)]
    private static partial Regex MailtoPattern();

    [GeneratedRegex(@"(?<!\w)[\w.+-]+@[\w-]+\.[\w.-]+(?!\w)", RegexOptions.IgnoreCase)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?:tel:|\+?\d[\d\-\.\s\(\)]{6,}\d)", RegexOptions.IgnoreCase)]
    private static partial Regex PhonePattern();

    public static List<ContentAction> Detect(string text)
    {
        var actions = new List<ContentAction>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // URLs
        foreach (Match m in UrlPattern().Matches(text))
        {
            var url = m.Value.TrimEnd('.', ',', ';', ')', ']');
            if (!seen.Add(url)) continue;
            actions.Add(new ContentAction
            {
                Label = "Open Link",
                Icon = "🔗",
                Url = url,
                Command = new RelayCommand(() => _ = Launcher.OpenAsync(new Uri(url)))
            });
        }

        // mailto: links
        foreach (Match m in MailtoPattern().Matches(text))
        {
            var email = m.Groups[1].Value;
            if (!seen.Add($"email:{email}")) continue;
            actions.Add(new ContentAction
            {
                Label = "Email",
                Icon = "✉️",
                Url = email,
                Command = new RelayCommand(() => _ = Launcher.OpenAsync(new Uri($"mailto:{email}")))
            });
        }

        // Bare email addresses (only if not already captured via mailto:)
        foreach (Match m in EmailPattern().Matches(text))
        {
            var email = m.Value;
            if (!seen.Add($"email:{email}")) continue;
            actions.Add(new ContentAction
            {
                Label = "Email",
                Icon = "✉️",
                Url = email,
                Command = new RelayCommand(() => _ = Launcher.OpenAsync(new Uri($"mailto:{email}")))
            });
        }

        // Phone numbers
        foreach (Match m in PhonePattern().Matches(text))
        {
            var raw = m.Value;
            if (raw.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
                raw = raw[4..];
            var digits = new string(raw.Where(c => char.IsDigit(c) || c == '+').ToArray());
            if (digits.Length < 7 || !seen.Add($"tel:{digits}")) continue;
            actions.Add(new ContentAction
            {
                Label = "Call",
                Icon = "📞",
                Url = raw.Trim(),
                Command = new RelayCommand(() => _ = Launcher.OpenAsync(new Uri($"tel:{digits}")))
            });
        }

        return actions;
    }
}
