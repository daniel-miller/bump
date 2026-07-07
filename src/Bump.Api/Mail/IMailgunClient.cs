namespace Bump.Api.Mail;

public interface IMailgunClient
{
    Task SendAsync(MailMessage message, CancellationToken ct = default);
}

public sealed class MailMessage
{
    public string? From { get; init; }
    public required IReadOnlyList<string> To { get; init; }
    public required string Subject { get; init; }
    public required string Text { get; init; }
    public string? Html { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}
