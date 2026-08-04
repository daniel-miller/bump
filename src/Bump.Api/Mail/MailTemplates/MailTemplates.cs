using System.Text.Encodings.Web;

namespace Bump.Api.Mail.MailTemplates;

public static class TwoFactorRecoveryCodes
{
    public static MailMessage Build(string toEmail, IReadOnlyList<string> codes)
    {
        var text = $"""
            Your Bump two-factor authentication recovery codes:

            {string.Join("\n", codes)}

            Each code may be used once. Store them somewhere safe; if you lose
            access to your authenticator app these are the only way back in.
            """;
        return new MailMessage
        {
            To = new[] { toEmail },
            Subject = "Your Bump recovery codes",
            Text = text,
            Tags = new[] { "auth", "2fa" }
        };
    }
}

public static class TestEmail
{
    public static MailMessage Build(string toEmail)
    {
        var text = $"""
            This is a test message from Bump.

            If you received this, outbound email delivery is working for your
            account ({toEmail}). You can safely delete this message.
            """;
        return new MailMessage
        {
            To = new[] { toEmail },
            Subject = "Bump test email",
            Text = text,
            Tags = new[] { "account", "test" }
        };
    }
}

public static class EmailChangeConfirm
{
    public static MailMessage Build(string toEmail, string confirmUrl)
    {
        var text = $"""
            Confirm the new email address for your Bump account.

            {confirmUrl}

            The link expires in 1 hour. If you did not request this change,
            ignore this email — your account is unchanged.
            """;
        return new MailMessage
        {
            To = new[] { toEmail },
            Subject = "Confirm your new Bump email",
            Text = text,
            Tags = new[] { "auth", "email-change" }
        };
    }
}

public static class PasswordReset
{
    public static MailMessage Build(string toEmail, string resetUrl)
    {
        var text = $"""
            A password reset was requested for your Bump account.

            {resetUrl}

            The link expires in 1 hour. If you did not request a reset, you
            can safely ignore this message — your password is unchanged.
            """;
        return new MailMessage
        {
            To = new[] { toEmail },
            Subject = "Reset your Bump password",
            Text = text,
            Tags = new[] { "auth", "password-reset" }
        };
    }
}

public static class SubscriberConfirm
{
    public static MailMessage Build(string toEmail, string boardName, string confirmUrl, string unsubscribeUrl)
    {
        var text = $"""
            Confirm your subscription to {boardName}:

            {confirmUrl}

            You will receive outage and announcement notifications for this
            status board until you unsubscribe:

            {unsubscribeUrl}
            """;
        return new MailMessage
        {
            To = new[] { toEmail },
            Subject = $"Confirm your subscription to {boardName}",
            Text = text,
            Tags = new[] { "subscriber", "confirm" }
        };
    }
}

public static class OutageOpened
{
    public static MailMessage Build(string toEmail, string boardName, string title, string status, string startedAt, string unsubscribeUrl)
    {
        var text = $"""
            Outage opened on {boardName}: {title}

            Status:  {status}
            Started: {startedAt}

            Manage notifications: {unsubscribeUrl}
            """;
        return new MailMessage
        {
            To = new[] { toEmail },
            Subject = $"[{boardName}] Outage: {title}",
            Text = text,
            Tags = new[] { "outage", "opened" }
        };
    }
}

public static class OutageResolved
{
    public static MailMessage Build(string toEmail, string boardName, string title, string resolvedAt, string unsubscribeUrl)
    {
        var text = $"""
            Outage resolved on {boardName}: {title}

            Resolved: {resolvedAt}

            Manage notifications: {unsubscribeUrl}
            """;
        return new MailMessage
        {
            To = new[] { toEmail },
            Subject = $"[{boardName}] Resolved: {title}",
            Text = text,
            Tags = new[] { "outage", "resolved" }
        };
    }
}

public static class OutageAlert
{
    public static MailMessage Build(string toEmail, string serviceName, string title, string startedAt, string detail)
    {
        var text = $"""
            New outage on {serviceName}: {title}

            Started: {startedAt}
            {detail}
            """;
        return new MailMessage
        {
            To = new[] { toEmail },
            Subject = $"[Bump] Outage: {title}",
            Text = text,
            Tags = new[] { "outage", "alert" }
        };
    }
}

public static class AnnouncementPublished
{
    public static MailMessage Build(string toEmail, string boardName, string title, string content, string unsubscribeUrl)
    {
        var text = $"""
            Announcement on {boardName}: {title}

            {content}

            Manage notifications: {unsubscribeUrl}
            """;
        return new MailMessage
        {
            To = new[] { toEmail },
            Subject = $"[{boardName}] {title}",
            Text = text,
            Tags = new[] { "announcement" }
        };
    }
}

public static class ProblemDigest
{
    public sealed record DigestEntry(
        string Fingerprint,
        string Type,
        string Title,
        string AppHandle,
        string AppName,
        string Environment,
        string EnvironmentName,
        int Occurrences,
        DateTimeOffset LastSeen,
        long LatestProblemKey);

    public static MailMessage Build(string toEmail, DigestEntry e, string publicBaseUrl)
    {
        var trimmedBase = (publicBaseUrl ?? "").TrimEnd('/');
        var detailUrl = string.IsNullOrEmpty(trimmedBase)
            ? null
            : $"{trimmedBase}/problems/{e.LatestProblemKey}";

        var intro = $"An unexpected problem occurred in the {e.AppName} application running in the {e.EnvironmentName} environment. See below for a summary.";

        var text = detailUrl is null
            ? $"""
                {intro}

                Type: {e.Type}
                Title: {e.Title}
                App: {e.AppHandle}
                Environment: {e.Environment}
                Occurrences: {e.Occurrences}
                Last seen: {e.LastSeen:u}
                Fingerprint: {e.Fingerprint}
                """
            : $"""
                {intro}

                Visit this page for a detailed report: {detailUrl}

                Type: {e.Type}
                Title: {e.Title}
                App: {e.AppHandle}
                Environment: {e.Environment}
                Occurrences: {e.Occurrences}
                Last seen: {e.LastSeen:u}
                Fingerprint: {e.Fingerprint}
                """;

        var introHtml = $"<p style=\"font-family:-apple-system,Segoe UI,Arial,sans-serif;font-size:14px;line-height:1.5\">{HtmlEncoder.Default.Encode(intro)}</p>";
        var linkHtml = detailUrl is null
            ? ""
            : $"<p style=\"font-family:-apple-system,Segoe UI,Arial,sans-serif;font-size:14px;line-height:1.5\">"
              + $"Visit <a href=\"{HtmlEncoder.Default.Encode(detailUrl)}\">this page</a> for a detailed report."
              + "</p>";

        var html = $"""
            {introHtml}
            {linkHtml}
            <table style="border-collapse:collapse;font-family:-apple-system,Segoe UI,Arial,sans-serif;font-size:14px">
              {Row("Type",        e.Type)}
              {Row("Title",       e.Title)}
              {Row("App",         e.AppHandle)}
              {Row("Environment", e.Environment)}
              {Row("Occurrences", e.Occurrences.ToString())}
              {Row("Last seen",   e.LastSeen.ToString("u"))}
              {Row("Fingerprint", e.Fingerprint)}
            </table>
            """;

        return new MailMessage
        {
            To = new[] { toEmail },
            Subject = $"[Bump] {e.Type} in {e.AppHandle} ({e.Environment})",
            Text = text,
            Html = html,
            Tags = new[] { "problem", "digest" }
        };
    }

    private static string Row(string label, string value) =>
        $"<tr><td style=\"padding:2px 16px 2px 0;color:#666;vertical-align:top\">{HtmlEncoder.Default.Encode(label)}</td>"
        + $"<td style=\"padding:2px 0;vertical-align:top\">{HtmlEncoder.Default.Encode(value)}</td></tr>";
}
