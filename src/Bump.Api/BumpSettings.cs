namespace Bump.Api;

/// <summary>
/// Bound configuration sections. Every default lives here as a property initializer and
/// nowhere else.
/// </summary>
/// <remarks>
/// The alternative - <c>GetValue("Bump:Services:IntervalSeconds", 60)</c> at each call site -
/// wrote the same default into <c>appsettings.json</c> and into every caller, so one fact had
/// four spellings and nothing kept them equal. A section that loses a key during deploy-time
/// substitution then falls back to whichever literal the nearest caller happened to carry.
/// Bind once, inject the object, and the default has one home.
/// </remarks>
public sealed class ServicesSettings
{
    /// <summary>Seconds between probe sweeps.</summary>
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>Per-probe HTTP timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>Latency in milliseconds above which a healthy service is reported degraded.</summary>
    public int DegradedLatencyMs { get; set; } = 1000;

    /// <summary>Number of history bars retained per service on the status page.</summary>
    public int HistoryBars { get; set; } = 60;

    /// <summary>Address published in the probe user agent so a probed host can complain to a human.</summary>
    public string AbuseContact { get; set; } = "";

    /// <summary>Windows during which a failing probe does not open an outage.</summary>
    public List<MaintenanceWindow> MaintenanceWindows { get; set; } = new();

    public TimeSpan Interval => TimeSpan.FromSeconds(IntervalSeconds);
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);
}

/// <summary>A recurring daily window, in a named IANA time zone, when probe failures are expected.</summary>
public sealed class MaintenanceWindow
{
    public string? Start { get; set; }
    public string? End { get; set; }
    public string? TimeZone { get; set; }
}

/// <summary><c>Bump:Api:Subscribers</c>.</summary>
public sealed class SubscribersSettings
{
    /// <summary>Cap on confirmed subscribers per owner board.</summary>
    public int MaxPerOwner { get; set; } = 10_000;
}

/// <summary><c>Bump:Worker:Alerts</c>.</summary>
public sealed class AlertsSettings
{
    /// <summary>Seconds between digest polls. Health turns unhealthy after 3x this without a tick.</summary>
    public int PollSeconds { get; set; } = 300;

    /// <summary>Recipient of alert-digest and outage emails.</summary>
    public string Contact { get; set; } = "";

    public TimeSpan PollInterval => TimeSpan.FromSeconds(PollSeconds);
}

/// <summary><c>Bump:Worker:Announcements</c>.</summary>
public sealed class AnnouncementsSettings
{
    /// <summary>Seconds between scheduler ticks.</summary>
    public int TickSeconds { get; set; } = 60;

    public TimeSpan TickInterval => TimeSpan.FromSeconds(TickSeconds);
}

/// <summary>
/// <c>Bump:Api:Security:Tokens</c>. Lifetimes for the single-use tokens mailed to a user.
/// </summary>
public sealed class TokenSettings
{
    /// <summary>Hours a password-reset link stays valid.</summary>
    public int PasswordResetHours { get; set; } = 1;

    /// <summary>Hours an email-change confirmation link stays valid.</summary>
    public int EmailChangeHours { get; set; } = 1;
}

/// <summary>
/// <c>Bump:Api:RateLimits</c>. One entry per policy in <see cref="RateLimiting"/>.
/// </summary>
/// <remarks>
/// These are incident knobs. A customer tripping the login limiter or an IP abusing subscribe
/// should be answerable with a config edit, not a release - the same reason Serilog levels are
/// read from configuration. Subscribe matters most: with no CAPTCHA on that endpoint, its
/// limiter is the only thing standing in front of it.
/// </remarks>
public sealed class RateLimitSettings
{
    /// <summary><c>/api/apps/**</c>, partitioned by bearer token.</summary>
    public RateLimitPolicySettings Apps { get; set; } = new() { PermitLimit = 120, WindowMinutes = 1 };

    /// <summary><c>/api/problems</c>, partitioned by bearer token.</summary>
    public RateLimitPolicySettings Problems { get; set; } = new() { PermitLimit = 600, WindowMinutes = 1 };

    /// <summary><c>/api/auth/**</c>, partitioned by IP.</summary>
    public RateLimitPolicySettings Auth { get; set; } = new() { PermitLimit = 10, WindowMinutes = 5 };

    /// <summary><c>/api/auth/login</c>, partitioned by IP.</summary>
    public RateLimitPolicySettings AuthLogin { get; set; } = new() { PermitLimit = 5, WindowMinutes = 15 };

    /// <summary>Public subscribe, partitioned by IP.</summary>
    public RateLimitPolicySettings Subscribe { get; set; } = new() { PermitLimit = 3, WindowMinutes = 10 };

    /// <summary><c>/api/status/**</c>, partitioned by IP.</summary>
    public RateLimitPolicySettings Status { get; set; } = new() { PermitLimit = 60, WindowMinutes = 1 };
}

/// <summary>A fixed-window limiter's two tunable values.</summary>
public sealed class RateLimitPolicySettings
{
    public int PermitLimit { get; set; }
    public int WindowMinutes { get; set; }

    public TimeSpan Window => TimeSpan.FromMinutes(WindowMinutes);
}
