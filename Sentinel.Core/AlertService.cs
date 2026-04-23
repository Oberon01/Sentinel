using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Sentinel.Core;

public sealed class AlertService
{
    private readonly ILogger<AlertService> _logger;
    private readonly Dictionary<string, DateTimeOffset> _lastAlertTimes = new();
    private static readonly TimeSpan AlertCooldown = TimeSpan.FromMinutes(5);

    public AlertService(ILogger<AlertService> logger)
    {
        _logger = logger;
    }

    public void SendAlert(string title, string message)
    {
        if (IsOnCooldown(title))
            return;

        _logger.LogWarning("ALERT [{Title}]: {Message}", title, message);
        _lastAlertTimes[title] = DateTimeOffset.UtcNow;

        try
        {
            var script = $"""
                [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
                $template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
                $textNodes = $template.GetElementsByTagName('text')
                $textNodes[0].AppendChild($template.CreateTextNode('{Escape(title)}')) | Out-Null
                $textNodes[1].AppendChild($template.CreateTextNode('{Escape(message)}')) | Out-Null
                $toast = [Windows.UI.Notifications.ToastNotification]::new($template)
                [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Sentinel').Show($toast)
                """;

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -Command \"{script.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send toast notification");
        }
    }

    private bool IsOnCooldown(string key) =>
        _lastAlertTimes.TryGetValue(key, out var last) &&
        DateTimeOffset.UtcNow - last < AlertCooldown;

    private static string Escape(string input) =>
        input.Replace("'", "''");
}