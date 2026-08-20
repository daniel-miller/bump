using Newtonsoft.Json;

namespace Bump.Api.Services;

/// <summary>
/// Builds the GET /api/status payload — the single source of truth for
/// the public board page. KPI math, 14-day trend window, and history
/// shaping all live here.
/// </summary>
public sealed class StatusComposer
{
    private readonly ServiceRepository _services;
    private readonly OutageRepository _outages;
    private readonly OwnerRepository _owners;
    private readonly AnnouncementRepository _announcements;
    private readonly ProblemRepository _problems;

    public StatusComposer(ServiceRepository services, OutageRepository outages, OwnerRepository owners, AnnouncementRepository announcements, ProblemRepository problems)
    {
        _services = services;
        _outages = outages;
        _owners = owners;
        _announcements = announcements;
        _problems = problems;
    }

    public async Task<object> ComposeAsync(string? ownerHandle, bool excludePaused, string ianaTz, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var tz = TimeZoneInfo.FindSystemTimeZoneById(ianaTz);
        var todayLocal = TimeZoneInfo.ConvertTimeFromUtc(now.UtcDateTime, tz).Date;

        Owner? owner = ownerHandle is null ? null : await _owners.GetByHandleAsync(ownerHandle, ct);
        var allServices = await _services.ListAsync(ct);
        var allOwners = await _owners.ListAsync(ct);

        IReadOnlyList<Service> scopedServices = allServices;
        if (owner is not null)
        {
            var ids = (await _owners.GetServiceIdsAsync(owner.OwnerId, ct)).ToHashSet();
            scopedServices = allServices.Where(m => ids.Contains(m.ServiceId)).ToList();
        }

        var serviceIds = scopedServices.Select(m => m.ServiceId).ToList();
        var states = await _services.GetStatesAsync(serviceIds, ct);
        var openOutages = await _outages.ListOpenForServicesAsync(serviceIds, ct);
        var dailies = await _services.GetDailyForServicesAsync(serviceIds, 14, ianaTz, ct);
        var outageDayCounts = await _outages.CountByDayForServicesAsync(serviceIds, 14, ianaTz, ct);
        // No owner scope = global admin view. Count all unresolved problems
        // so unmonitored (app, environment) pairs still surface in the KPI.
        // With an owner scope, stay tied to the owner's services so the count
        // matches the listed services.
        var activeProblems = owner is null
            ? await _problems.CountAllActiveAsync(TimeSpan.FromHours(24), ct)
            : await _problems.CountActiveAsync(serviceIds, TimeSpan.FromHours(24), ct);
        var problemDayCounts = await _problems.CountByDayAsync(14, ianaTz, ct);

        // Real availability per service over the 14-day window, from probe_event
        // (the same source the trend bars use). Up = not down, so a slow-but-
        // reachable service is not penalized as unavailable. This replaces the
        // clamped [95-100] bar score that service_state.uptime_pct carries.
        var uptimeByService = dailies
            .GroupBy(d => d.ServiceId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    int probes = g.Sum(x => x.Probes);
                    int up = g.Sum(x => x.UpCount);
                    return probes == 0 ? 100m : Math.Round((decimal)up / probes * 100m, 2);
                });

        // Per-service view. Paused services stay in the list so callers can render
        // them as paused; pass excludePaused=true to drop them from the response.
        var servicePayload = scopedServices
            .Where(m => !excludePaused || !m.ServicePaused)
            .Select(m =>
            {
                states.TryGetValue(m.ServiceId, out var s);
                var history = s?.History ?? Enumerable.Repeat(ServiceStatuses.Operational, 60).ToList();
                return new
                {
                    handle = m.ServiceHandle,
                    name = m.ServiceName,
                    owner = m.ServiceOwner,
                    environment = m.ServiceEnvironment,
                    url = m.ServiceUrl,
                    paused = m.ServicePaused,
                    status = s?.LastStatus ?? ServiceStatuses.Operational,
                    latencyMs = s?.LatencyMs ?? 0,
                    uptime = uptimeByService.TryGetValue(m.ServiceId, out var up) ? up : 100m,
                    history,
                    lastOutageAt = s?.LastOutageAt
                };
            }).ToList();

        // KPIs and overall status always ignore paused services regardless of the flag —
        // a paused service contributes neither uptime nor latency and never trips overall to Down.
        var activePayload = servicePayload.Where(p => !p.paused).ToList();
        var statusValues = activePayload.Select(p => (string)p.status).ToList();
        string overall = statusValues.Count == 0
            ? ServiceStatuses.Operational
            : statusValues.Any(s => s == ServiceStatuses.Down) ? ServiceStatuses.Down
            : statusValues.All(s => s == ServiceStatuses.Operational) ? ServiceStatuses.Operational
            : ServiceStatuses.Degraded;

        int total = activePayload.Count;
        int operational = statusValues.Count(s => s == ServiceStatuses.Operational);
        int affected = total - operational;
        decimal avgUptime = activePayload.Count == 0 ? 100m : activePayload.Average(p => p.uptime);
        int avgLatency = activePayload.Count == 0 ? 0 : (int)activePayload.Average(p => p.latencyMs);
        int activeOutages = openOutages.Count;

        var kpis = new
        {
            operational = new
            {
                label = "Services operational",
                value = $"{operational}/{total}",
                subtitle = affected == 0 ? "All green" : $"{affected} affected",
                trend = affected == 0 ? "up" : "down"
            },
            uptime = new
            {
                label = "Average uptime (14d)",
                value = $"{avgUptime:0.00}%",
                subtitle = WeekDelta(dailies, "uptime", todayLocal),
                trend = "up"
            },
            latency = new
            {
                label = "Average latency",
                value = $"{avgLatency} ms",
                subtitle = WeekDelta(dailies, "latency", todayLocal),
                trend = "up"
            },
            outages = new
            {
                label = "Active outages",
                value = activeOutages.ToString(),
                subtitle = activeOutages == 0 ? "None" : "Investigating",
                trend = activeOutages == 0 ? "up" : "down"
            },
            problems = new
            {
                label = "Unresolved problems",
                value = activeProblems.DistinctFingerprints.ToString(),
                subtitle = activeProblems.Total == 0
                    ? "None in last 24h"
                    : $"{activeProblems.Total} report{(activeProblems.Total == 1 ? "" : "s")} in last 24h",
                trend = activeProblems.DistinctFingerprints == 0 ? "up" : "down"
            }
        };

        // 14-day trend
        var trend = BuildTrend(dailies, outageDayCounts, todayLocal);
        var problemsTrend = BuildProblemsTrend(problemDayCounts, todayLocal);

        // Outages shown on the public board (open + recent resolved)
        var outagesOut = openOutages.Select(i => new
        {
            id = i.OutageId,
            serviceHandle = i.ServiceId is int mid ? scopedServices.FirstOrDefault(m => m.ServiceId == mid)?.ServiceHandle : null,
            title = i.OutageTitle,
            status = i.OutageStatus,
            startedAt = i.StartedAt,
            resolvedAt = i.ResolvedAt,
            updates = Array.Empty<object>()
        }).ToList();

        // Announcements visible right now
        var visibleAnnouncements = await _announcements.ListVisibleAsync(owner?.OwnerId, now, ct);
        var announcementsOut = visibleAnnouncements.Select(a => new
        {
            id = a.AnnouncementId,
            title = a.AnnouncementTitle,
            type = a.AnnouncementType,
            content = a.AnnouncementContent,
            publishAt = a.PublishAt,
            autoHideAt = a.AutoHideAt
        }).ToList();

        return new
        {
            overall,
            updatedAt = now,
            owner = owner is null ? null : new { handle = owner.OwnerHandle, name = owner.OwnerName },
            kpis,
            trend,
            problemsTrend,
            services = servicePayload,
            outages = outagesOut,
            owners = allOwners.Select(o => new { handle = o.OwnerHandle, name = o.OwnerName }).ToList(),
            announcements = announcementsOut
        };
    }

    private static IReadOnlyList<object> BuildProblemsTrend(
        IReadOnlyList<ProblemRepository.DayCount> dayCounts,
        DateTime todayLocal)
    {
        var byDay = dayCounts.ToDictionary(d => d.Day.Date, d => d.Count);
        var result = new List<object>(14);
        for (int offset = 13; offset >= 0; offset--)
        {
            var day = todayLocal.AddDays(-offset);
            byDay.TryGetValue(day, out var count);
            result.Add(new
            {
                date = day.ToString("yyyy-MM-dd"),
                label = day.ToString("ddd"),
                count
            });
        }
        return result;
    }

    private static IReadOnlyList<object> BuildTrend(
        IReadOnlyList<ServiceRepository.DailyRow> dailies,
        IReadOnlyList<OutageRepository.DayCount> outageDayCounts,
        DateTime todayLocal)
    {
        // Aggregate per day across the scoped services. Take the last 14 days.
        var byDay = dailies
            .GroupBy(d => d.Day.Date)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    Probes = g.Sum(x => x.Probes),
                    Up = g.Sum(x => x.UpCount),
                    LatencySum = g.Sum(x => x.LatencySumMs)
                });

        var outagesByDay = outageDayCounts
            .GroupBy(d => d.Day.Date)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

        var result = new List<object>(14);
        for (int offset = 13; offset >= 0; offset--)
        {
            var day = todayLocal.AddDays(-offset);
            byDay.TryGetValue(day, out var v);
            int probes = v?.Probes ?? 0;
            int up = v?.Up ?? 0;
            long latencySum = v?.LatencySum ?? 0;
            decimal uptime = probes == 0 ? 100m : Math.Round((decimal)up / probes * 100m, 2);
            int latencyMs = probes == 0 ? 0 : (int)(latencySum / probes);
            outagesByDay.TryGetValue(day, out var outageCount);
            result.Add(new
            {
                date = day.ToString("yyyy-MM-dd"),
                label = day.ToString("ddd"),
                outages = outageCount,
                uptime,
                latencyMs,
                requests = probes
            });
        }
        return result;
    }

    private static string WeekDelta(IReadOnlyList<ServiceRepository.DailyRow> dailies, string kind, DateTime todayLocal)
    {
        // Compare the most-recent 7-day window against the prior 7 days.
        if (dailies.Count == 0) return "—";
        var byDay = dailies
            .GroupBy(d => d.Day.Date)
            .ToDictionary(
                g => g.Key,
                g => new { Probes = g.Sum(x => x.Probes), Up = g.Sum(x => x.UpCount), L = g.Sum(x => x.LatencySumMs) });

        decimal recent = 0, prior = 0;
        int recentDays = 0, priorDays = 0;
        for (int i = 0; i < 14; i++)
        {
            var d = todayLocal.AddDays(-i);
            if (!byDay.TryGetValue(d, out var v) || v.Probes == 0) continue;
            decimal val = kind == "uptime"
                ? (decimal)v.Up / v.Probes * 100m
                : (decimal)v.L / v.Probes;
            if (i < 7) { recent += val; recentDays++; } else { prior += val; priorDays++; }
        }
        if (recentDays == 0 || priorDays == 0) return "—";
        var rAvg = recent / recentDays;
        var pAvg = prior / priorDays;
        var delta = rAvg - pAvg;
        return kind == "uptime"
            ? $"{(delta >= 0 ? "+" : "")}{delta:0.00}% vs last week"
            : $"{(delta >= 0 ? "+" : "")}{(int)delta} ms vs last week";
    }
}
