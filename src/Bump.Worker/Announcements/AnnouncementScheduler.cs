using Bump.Api.Mail;
using Bump.Api.Mail.MailTemplates;
using Bump.Api.Services;
using Dapper;
using Npgsql;

namespace Bump.Worker.Announcements;

/// <summary>
/// Ticks every minute. Picks up announcements whose publish_at is past and
/// dispatched_at is null, claims them with SELECT ... FOR UPDATE SKIP
/// LOCKED, marks dispatched, and (when notify_subscribers is true) emails
/// confirmed subscribers of the matching board.
/// </summary>
public sealed class AnnouncementScheduler : BackgroundService
{
    private readonly ILogger<AnnouncementScheduler> _logger;
    private readonly NpgsqlDataSource _dataSource;
    private readonly BoardRepository _boards;
    private readonly SubscriberRepository _subscribers;
    private readonly IMailgunClient _mail;
    private readonly TimeSpan _interval;
    private readonly string _publicBaseUrl;
    private readonly WorkerStatus _status;

    public AnnouncementScheduler(
        ILogger<AnnouncementScheduler> logger,
        IConfiguration config,
        NpgsqlDataSource dataSource,
        BoardRepository boards,
        SubscriberRepository subscribers,
        IMailgunClient mail,
        WorkerStatus status)
    {
        _logger = logger;
        _dataSource = dataSource;
        _boards = boards;
        _subscribers = subscribers;
        _mail = mail;
        _status = status;
        _interval = TimeSpan.FromSeconds(config.GetValue("Bump:Announcements:TickSeconds", 60));
        _publicBaseUrl = (config["Bump:Hosting:PublicBaseUrl"] ?? "").TrimEnd('/');
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Announcement scheduler started. Interval {Interval}.", _interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
                _status.RecordAnnouncementTick();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Announcement tick failed.");
            }
            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var due = await conn.QueryAsync<Announcement>(
            """
            SELECT announcement_key      AS AnnouncementId,
                   tenant_key             AS BoardId,
                   announcement_title   AS AnnouncementTitle,
                   announcement_type    AS AnnouncementType,
                   announcement_content AS AnnouncementContent,
                   publish_at           AS PublishAt,
                   auto_hide_at         AS AutoHideAt,
                   notify_subscribers   AS NotifySubscribers,
                   dispatched_at        AS DispatchedAt,
                   created_by           AS CreatedBy,
                   created_at           AS CreatedAt,
                   updated_at           AS UpdatedAt
              FROM announcement
             WHERE publish_at <= now()
               AND dispatched_at IS NULL
             FOR UPDATE SKIP LOCKED
            """,
            transaction: tx);

        var list = due.ToList();
        if (list.Count == 0)
        {
            await tx.CommitAsync(ct);
            return;
        }

        foreach (var a in list)
        {
            await conn.ExecuteAsync(
                "UPDATE announcement SET dispatched_at = now() WHERE announcement_key = @I",
                new { I = a.AnnouncementId }, tx);
        }

        await tx.CommitAsync(ct);

        // Send emails outside the transaction.
        foreach (var a in list)
        {
            if (!a.NotifySubscribers) continue;
            var boardIds = a.BoardId is int bid ? new[] { bid } : (await _boards.ListAsync(ct)).Select(b => b.BoardId).ToArray();
            foreach (var boardId in boardIds)
            {
                var board = await _boards.GetByIdAsync(boardId, ct);
                if (board is null) continue;
                var subs = await _subscribers.ListConfirmedAsync(boardId, ct);
                foreach (var s in subs)
                {
                    var unsubUrl = $"{_publicBaseUrl}/unsubscribe?token={Convert.ToBase64String(s.UnsubscribeToken).Replace('+', '-').Replace('/', '_').TrimEnd('=')}";
                    await _mail.SendAsync(AnnouncementPublished.Build(s.SubscriberEmail, board.BoardName, a.AnnouncementTitle, a.AnnouncementContent, unsubUrl), ct);
                }
            }
        }
    }
}
