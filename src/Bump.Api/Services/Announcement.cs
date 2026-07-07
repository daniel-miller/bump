using Dapper;
using Npgsql;

namespace Bump.Api.Services;

public sealed class Announcement
{
    public int AnnouncementId { get; set; }
    public int? BoardId { get; set; }
    public string AnnouncementTitle { get; set; } = "";
    public string AnnouncementType { get; set; } = "info";
    public string AnnouncementContent { get; set; } = "";
    public DateTimeOffset PublishAt { get; set; }
    public DateTimeOffset? AutoHideAt { get; set; }
    public bool NotifySubscribers { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class AnnouncementRepository(NpgsqlDataSource dataSource)
{
    private const string Cols = """
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
        """;

    public async Task<IReadOnlyList<Announcement>> ListAsync(int? boardId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        if (boardId is int b)
        {
            var rows = await conn.QueryAsync<Announcement>(
                Cols + " WHERE tenant_key = @B OR tenant_key IS NULL ORDER BY publish_at DESC",
                new { B = b });
            return rows.AsList();
        }
        else
        {
            var rows = await conn.QueryAsync<Announcement>(Cols + " ORDER BY publish_at DESC");
            return rows.AsList();
        }
    }

    public async Task<IReadOnlyList<Announcement>> ListVisibleAsync(int? boardId, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        if (boardId is int b)
        {
            var rows = await conn.QueryAsync<Announcement>(
                Cols + " WHERE (tenant_key = @B OR tenant_key IS NULL)"
                     + " AND publish_at <= @Now"
                     + " AND (auto_hide_at IS NULL OR auto_hide_at > @Now)"
                     + " ORDER BY publish_at DESC",
                new { B = b, Now = now });
            return rows.AsList();
        }
        else
        {
            var rows = await conn.QueryAsync<Announcement>(
                Cols + " WHERE tenant_key IS NULL"
                     + " AND publish_at <= @Now"
                     + " AND (auto_hide_at IS NULL OR auto_hide_at > @Now)"
                     + " ORDER BY publish_at DESC",
                new { Now = now });
            return rows.AsList();
        }
    }

    public async Task<Announcement?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Announcement>(Cols + " WHERE announcement_key = @I", new { I = id });
    }

    public async Task<Announcement> CreateAsync(int? boardId, string title, string type, string content, DateTimeOffset publishAt, DateTimeOffset? autoHideAt, bool notifySubscribers, Guid? createdBy, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<Announcement>(
            """
            INSERT INTO announcement (tenant_key, announcement_title, announcement_type, announcement_content, publish_at, auto_hide_at, notify_subscribers, created_by)
            VALUES (@B, @T, @Y, @C, @P, @H, @N, @U)
            RETURNING announcement_key      AS AnnouncementId,
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
            """,
            new { B = boardId, T = title, Y = type, C = content, P = publishAt, H = autoHideAt, N = notifySubscribers, U = createdBy });
    }

    public async Task UpdateAsync(int id, string title, string type, string content, DateTimeOffset publishAt, DateTimeOffset? autoHideAt, bool notifySubscribers, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE announcement
               SET announcement_title = @T,
                   announcement_type = @Y,
                   announcement_content = @C,
                   publish_at = @P,
                   auto_hide_at = @H,
                   notify_subscribers = @N,
                   updated_at = now()
             WHERE announcement_key = @I
            """,
            new { I = id, T = title, Y = type, C = content, P = publishAt, H = autoHideAt, N = notifySubscribers });
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.ExecuteAsync("DELETE FROM announcement WHERE announcement_key = @I", new { I = id });
        return rows > 0;
    }
}
