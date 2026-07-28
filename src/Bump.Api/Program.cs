using System.Data;
using Bump.Api.Auth;
using Bump.Api.Mail;
using Bump.Api.Migrations;
using Bump.Api.Services;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Newtonsoft.Json.Serialization;
using Npgsql;
using Serilog;

namespace Bump.Api
{
    // Defensive: no API path currently passes DateOnly to Dapper, but the
    // same Dapper version that bit Bump.Worker (cannot bind DateOnly to an
    // Npgsql parameter) is referenced here too. Register the handler so any
    // future caller is safe.
    internal sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.Date;
            parameter.Value = value.ToDateTime(TimeOnly.MinValue);
        }

        public override DateOnly Parse(object value) => DateOnly.FromDateTime((DateTime)value);
    }

    static class Program
    {
        private static void Main(string[] args)
        {
            SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());

            if (args.Length >= 1 && args[0] == "hash")
            {
                var password = args.Length >= 2 ? args[1] : ReadPassword();
                var (hash, salt) = Bump.Api.Auth.PasswordHasher.Hash(password);
                Console.WriteLine($"hash_hex={Convert.ToHexString(hash)}");
                Console.WriteLine($"salt_hex={Convert.ToHexString(salt)}");
                Console.WriteLine();
                Console.WriteLine("Seed SQL:");
                Console.WriteLine($"INSERT INTO account (account_email, account_full_name, account_timezone, password_hash, password_salt)");
                Console.WriteLine($"VALUES ('admin@example.com', 'Admin', 'UTC', decode('{Convert.ToHexString(hash)}','hex'), decode('{Convert.ToHexString(salt)}','hex'));");
                return;
            }

            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateBootstrapLogger();

            try
            {
                RunApp(args);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Bump.Api terminated unexpectedly.");
                // Nonzero so a service manager, container runtime, or deploy health check
                // reads a failed start as a failure. Returning 0 here made a config error
                // look like a clean shutdown.
                Environment.ExitCode = 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        private static string ReadPassword()
        {
            Console.Write("Password: ");
            var sb = new System.Text.StringBuilder();
            while (true)
            {
                var k = Console.ReadKey(intercept: true);
                if (k.Key == ConsoleKey.Enter) { Console.WriteLine(); break; }
                if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
                sb.Append(k.KeyChar);
            }
            return sb.ToString();
        }

        private static void RunApp(string[] args)
        {
            // ContentRoot pinned to AppContext.BaseDirectory so appsettings.json
            // and appsettings.work.json (linked into the bin/ output via csproj
            // <None>) load regardless of the caller's working directory.
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                ContentRootPath = AppContext.BaseDirectory
            });

            builder.Configuration.AddJsonFile("appsettings.work.json", optional: true, reloadOnChange: true);

            // Levels come from the Serilog section so a noisy production incident can be
            // debugged by editing config, not by cutting a release. The sinks stay in code
            // because the file path is the only part that varies per deployment, and it
            // has its own key.
            var apiLogPath = builder.Configuration["Bump:Api:LogPath"];
            if (string.IsNullOrWhiteSpace(apiLogPath))
            {
                apiLogPath = Path.Combine("tmp", "logs", "api");
            }
            builder.Host.UseSerilog((context, services, configuration) => configuration
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .ReadFrom.Services(services)
                .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(Path.Combine(apiLogPath, "serilog-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}"));

            // ---- Release ----
            // Release:Environment is a reporting label (work, demo, test, live) and is NOT
            // ASPNETCORE_ENVIRONMENT, which switches behaviour. Two facts, two keys.
            // Validated because blank reads as a configuration that worked: the About page
            // renders an empty Environment row and nobody can tell which box they are on.
            var release = builder.Configuration.GetSection("Release").Get<ReleaseSettings>() ?? new ReleaseSettings();
            if (string.IsNullOrWhiteSpace(release.Environment))
            {
                throw new InvalidOperationException(
                    "Release:Environment is empty. It labels this deployment on the About page. "
                    + "Set it via config/appsettings.work.json or the Release__Environment environment variable.");
            }
            builder.Services.AddSingleton(release);

            // ---- Hosting ----
            // The listen address cannot live at the config root: Bump.Api and Bump.Worker
            // link the same appsettings.json, so one shared Urls key put both hosts on the
            // same port and whichever started second died on bind.
            var apiUrls = builder.Configuration["Bump:Api:Hosting:Urls"];
            if (string.IsNullOrWhiteSpace(apiUrls))
            {
                throw new InvalidOperationException(
                    "Bump:Api:Hosting:Urls is empty. It is the address Kestrel binds. Set it via "
                    + "config/appsettings.work.json or the Bump__Api__Hosting__Urls environment variable.");
            }
            builder.WebHost.UseUrls(apiUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            // Every outgoing email link and the probe user agent are built from this. Empty
            // does not fail, it produces host-less links like /reset-password?token=... that
            // land in a user's inbox and cannot be clicked, so it is checked at boot.
            var webBaseUrl = builder.Configuration["Bump:Web:BaseUrl"];
            if (string.IsNullOrWhiteSpace(webBaseUrl))
            {
                throw new InvalidOperationException(
                    "Bump:Web:BaseUrl is empty. It is the public status-page URL embedded in password-reset, "
                    + "email-change, and subscriber-confirmation links. Set it via config/appsettings.work.json "
                    + "or the Bump__Web__BaseUrl environment variable.");
            }

            // ---- Database ----
            var connectionString = builder.Configuration["Bump:Database:ConnectionString"];
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Bump:Database:ConnectionString is empty. Set it via config/appsettings.work.json or the Bump__Database__ConnectionString environment variable.");
            }
            var dataSource = NpgsqlDataSource.Create(connectionString);
            builder.Services.AddSingleton(dataSource);

            // ---- Repositories (existing + new) ----
            builder.Services.AddSingleton<AppRepository>();
            builder.Services.AddSingleton<EnvironmentRepository>();
            builder.Services.AddSingleton<ProblemRepository>();
            builder.Services.AddSingleton<IdempotencyRepository>();
            builder.Services.AddSingleton<AppUserRepository>();
            builder.Services.AddSingleton<UserSessionRepository>();
            builder.Services.AddSingleton<UserRecoveryCodeRepository>();
            builder.Services.AddSingleton<PasswordResetTokenRepository>();
            builder.Services.AddSingleton<EmailChangeTokenRepository>();
            builder.Services.AddSingleton<ServiceRepository>();
            builder.Services.AddSingleton<OutageRepository>();
            builder.Services.AddSingleton<BoardRepository>();
            builder.Services.AddSingleton<AnnouncementRepository>();
            builder.Services.AddSingleton<SubscriberRepository>();
            builder.Services.AddSingleton<StatusComposer>();
            builder.Services.AddMemoryCache();
            builder.Services.AddSingleton<ITimezoneResolver, TimezoneResolver>();

            // ---- Auth ----
            builder.Services.AddSingleton(BumpCookieOptions.FromConfig(builder.Configuration));
            builder.Services.AddSingleton<JwtIssuer>();
            builder.Services
                .AddAuthentication(SessionAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, SessionAuthHandler>(SessionAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();

            // ---- Filters ----
            builder.Services.AddSingleton<ApiKeyAuthFilter>();
            builder.Services.AddSingleton<ProblemsAuthFilter>();
            builder.Services.AddSingleton<IdempotencyFilter>();
            builder.Services.AddSingleton<CsrfFilter>();

            builder.Services.AddSingleton<Migrator>();

            // ---- Mailgun ----
            var mailgunOpts = new MailgunOptions
            {
                ApiKey = builder.Configuration["Bump:Mailgun:ApiKey"] ?? "",
                Domain = builder.Configuration["Bump:Mailgun:Domain"] ?? "",
                From = builder.Configuration["Bump:Mailgun:From"] ?? "Bump <bump@example.com>",
                Region = builder.Configuration["Bump:Mailgun:Region"] ?? "us",
            };
            // Mailgun is optional, as the README says: a clone without an account still runs,
            // it just cannot send. MailgunClient already logs and returns on every send, so
            // the only thing missing was a single loud line at boot instead of a warning
            // buried in whichever request first tried to send a password reset.
            if (string.IsNullOrWhiteSpace(mailgunOpts.ApiKey) || string.IsNullOrWhiteSpace(mailgunOpts.Domain))
            {
                Log.Warning("Bump:Mailgun:ApiKey or Bump:Mailgun:Domain is empty. Outbound email is disabled - "
                    + "password resets, email-change confirmations, subscriber confirmations, and alert digests "
                    + "will not be delivered. Set both in config/appsettings.work.json to enable them.");
            }
            builder.Services.AddSingleton(mailgunOpts);
            builder.Services.AddHttpClient<IMailgunClient, MailgunClient>();

            // ---- Bearer secrets ----
            // Both keys ship empty and are substituted per deployment. Empty has to stop
            // the app rather than degrade: an empty Apps list silently 401s every deploy
            // pipeline that calls /api/apps, and an empty Problems key silently drops
            // /api/problems back to session-only, so every SDK consumer stops reporting.
            // Neither looks like a configuration error at the point it happens. A
            // placeholder left unsubstituted is worse - it authenticates.
            var problemsSecret = builder.Configuration["Bump:Api:Hosting:ClientSecret"];
            if (string.IsNullOrWhiteSpace(problemsSecret))
            {
                throw new InvalidOperationException(
                    "Bump:Api:Hosting:ClientSecret is empty. It is the bearer key every Bump.Sdk consumer "
                    + "presents to POST /api/problems. Set it via config/appsettings.work.json or the "
                    + "Bump__Api__Hosting__ClientSecret environment variable.");
            }

            var appsSecrets = builder.Configuration.GetSection("Bump:Api:Security:Apps:ClientSecrets").Get<string[]>()
                ?? Array.Empty<string>();
            if (appsSecrets.Length == 0 || appsSecrets.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidOperationException(
                    "Bump:Api:Security:Apps:ClientSecrets is empty or contains a blank entry. It is the bearer key "
                    + "list for /api/apps/**, used by deploy pipelines to bump versions. Set it via "
                    + "config/appsettings.work.json or the Bump__Api__Security__Apps__ClientSecrets__0 environment variable.");
            }

            // ---- CORS ----
            var origins = builder.Configuration.GetSection("Bump:Api:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
            builder.Services.AddCors(opts => opts.AddDefaultPolicy(p =>
            {
                if (origins.Length > 0)
                {
                    p.WithOrigins(origins)
                     .AllowAnyHeader()
                     .WithMethods("GET", "POST", "PATCH", "DELETE", "OPTIONS")
                     .AllowCredentials()
                     .WithExposedHeaders("Idempotent-Replayed", "Retry-After", "Location");
                }
            }));

            // ---- Bound sections ----
            // Defaults live on the settings classes, not at call sites. See BumpSettings.cs.
            var serviceSettings = builder.Configuration.GetSection("Bump:Services").Get<ServicesSettings>() ?? new ServicesSettings();
            var subscribers = builder.Configuration.GetSection("Bump:Api:Subscribers").Get<SubscribersSettings>() ?? new SubscribersSettings();
            var tokens = builder.Configuration.GetSection("Bump:Api:Security:Tokens").Get<TokenSettings>() ?? new TokenSettings();
            var rateLimits = builder.Configuration.GetSection("Bump:Api:RateLimits").Get<RateLimitSettings>() ?? new RateLimitSettings();

            if (tokens.PasswordResetHours <= 0 || tokens.EmailChangeHours <= 0)
            {
                throw new InvalidOperationException(
                    "Bump:Api:Security:Tokens lifetimes must be greater than zero "
                    + $"(PasswordResetHours={tokens.PasswordResetHours}, EmailChangeHours={tokens.EmailChangeHours}). "
                    + "A non-positive value mails a link that has already expired.");
            }

            builder.Services.AddSingleton(serviceSettings);
            builder.Services.AddSingleton(subscribers);
            builder.Services.AddSingleton(tokens);
            builder.Services.AddBumpRateLimiting(rateLimits);

            builder.Services
                .AddControllers(mvc =>
                {
                    // Global CSRF gate. State-mutating requests carrying a
                    // session cookie must include a matching X-Bump-Csrf
                    // header. Bearer-key controllers opt out with
                    // [BypassCsrf]; safe methods pass through unconditionally.
                    mvc.Filters.AddService<CsrfFilter>();
                })
                .AddNewtonsoftJson(options =>
                {
                    // CamelCase on the wire so the React SPA reads `slug`/`history`
                    // etc. directly. Newtonsoft's deserialize is case-insensitive,
                    // so PascalCase requests from existing SDK consumers still bind.
                    options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
                });

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title = "Bump API",
                    Version = "v1",
                    Description = """
                        App version management, problem reporting, and uptime monitoring.

                        ## Authentication

                        Three schemes are in use:

                        - **Apps Bearer key** — `/api/apps/**` endpoints. Pre-shared key from `Bump:Api:Security:Apps:ClientSecrets`.
                        - **Problems Bearer key** — `/api/problems` (write). Pre-shared key from `Bump:Api:Hosting:ClientSecret`.
                        - **Session cookie** — `/api/auth/**`, `/api/accounts/**`, and all admin surfaces. Established via `POST /api/auth/login`. State-changing requests must also send `X-Bump-Csrf` matching the `bump_csrf` cookie.

                        ## Idempotency

                        POST endpoints marked with the `Idempotency-Key` header parameter accept a client-generated key (UUID recommended). Resending the same key replays the cached response (`Idempotent-Replayed: true`) instead of re-executing.

                        ## Rate limits

                        Per-API-key fixed-window buckets. 429 responses include `Retry-After`.
                        """,
                    Contact = new Microsoft.OpenApi.Models.OpenApiContact
                    {
                        Name = "Bump",
                        Url = new Uri("https://github.com/")
                    }
                });

                var bearerScheme = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "Opaque",
                    In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                    Description = "Enter the API key (without the 'Bearer ' prefix).",
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                };
                options.AddSecurityDefinition("Bearer", bearerScheme);
                options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
                {
                    { bearerScheme, Array.Empty<string>() }
                });

                options.OperationFilter<IdempotencyKeyOperationFilter>();

                var xmlPath = Path.Combine(AppContext.BaseDirectory, "Bump.Api.xml");
                if (File.Exists(xmlPath))
                {
                    options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
                }
            });
            builder.Services.AddSwaggerGenNewtonsoftSupport();

            var app = builder.Build();

            // Run migrations on boot (idempotent, safe to re-run).
            using (var scope = app.Services.CreateScope())
            {
                var migrator = scope.ServiceProvider.GetRequiredService<Migrator>();
                migrator.ApplyAsync().GetAwaiter().GetResult();

                // Self-register the API as a tracked app so its current
                // version is visible alongside other deployed services.
                var apps = scope.ServiceProvider.GetRequiredService<AppRepository>();
                apps.UpsertAsync(
                    slug: "bump",
                    name: "Bump",
                    major: 0, minor: 0, patch: 1).GetAwaiter().GetResult();

                // Push the deployed version from config to the DB row so
                // the About page reflects what is actually running. Config
                // is the source of truth; the upsert above only seeds.
                if (TryParseSemver(release.Version,
                        out var maj, out var min, out var pat))
                {
                    apps.SetVersionAsync("bump", maj, min, pat).GetAwaiter().GetResult();
                }
            }

            app.UseMiddleware<ProblemJsonExceptionHandler>();
            app.UseSerilogRequestLogging();

            // No UsePathBase. Bump owns a hostname; it is never mounted under a
            // path prefix. Sub-path hosting would put it on the same origin as
            // whatever else lives there, which is a browser security boundary
            // and not one a path divides - an XSS next door could read the CSRF
            // cookie, which is deliberately not HttpOnly so the SPA can read it.
            // Two things would break outright anyway: the Vite build has no
            // `base`, so the SPA fetches /assets/* from the root, and the auth
            // cookies hardcode Path = "/". UsePathBase fixes neither, so the
            // knob only ever offered a blank page and leaking cookies.

            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("v1/swagger.json", "Bump API v1");
                options.RoutePrefix = "swagger";
            });

            // SPA static assets (Vite build copied into wwwroot at publish time).
            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.UseRouting();
            app.UseCors();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseRateLimiter();

            app.MapControllers();
            // Client-side routing fallback. MapFallback only fires when no
            // other route matches, so /api/** and /swagger/** are unaffected.
            app.MapFallbackToFile("index.html");

            app.Run();
        }

        private static bool TryParseSemver(string? value, out int major, out int minor, out int patch)
        {
            major = minor = patch = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var parts = value.Trim().Split('.');
            return parts.Length == 3
                && int.TryParse(parts[0], out major)
                && int.TryParse(parts[1], out minor)
                && int.TryParse(parts[2], out patch);
        }
    }
}
