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
using Serilog.Events;

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

            // Serilog configured in code (per-project log path). Shared
            // appsettings.json holds Bump:* keys only; logging stays here.
            builder.Host.UseSerilog((context, services, configuration) => configuration
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .ReadFrom.Services(services)
                .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File("tmp/logs/bump-api-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}"));

            // ---- Database ----
            var connectionString = builder.Configuration.GetConnectionString("Bump");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:Bump is empty. Set it via config/appsettings.work.json or the ConnectionStrings__Bump environment variable.");
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
                From = builder.Configuration["Bump:Mailgun:From"] ?? "Bump <noreply@example.com>",
                Region = builder.Configuration["Bump:Mailgun:Region"] ?? "us",
            };
            if (string.IsNullOrWhiteSpace(mailgunOpts.ApiKey))
            {
                throw new InvalidOperationException(
                    "Bump:Mailgun:ApiKey is empty. Set it via config/appsettings.work.json or the Bump__Mailgun__ApiKey environment variable.");
            }
            if (string.IsNullOrWhiteSpace(mailgunOpts.Domain))
            {
                throw new InvalidOperationException(
                    "Bump:Mailgun:Domain is empty. Set it via config/appsettings.work.json or the Bump__Mailgun__Domain environment variable.");
            }
            builder.Services.AddSingleton(mailgunOpts);
            builder.Services.AddHttpClient<IMailgunClient, MailgunClient>();

            // ---- CAPTCHA ----
            builder.Services.AddHttpClient(nameof(CaptchaVerifier));
            builder.Services.AddSingleton<CaptchaVerifier>();

            // ---- CORS ----
            var origins = builder.Configuration.GetSection("Bump:Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
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

            builder.Services.AddBumpRateLimiting();

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

                        - **Apps Bearer key** — `/api/apps/**` endpoints. Pre-shared key from `Bump:Security:Apps:ApiKeys`.
                        - **Problems Bearer key** — `/api/problems` (write). Pre-shared key from `Bump:Security:Problems:ApiKey`.
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
                if (TryParseSemver(builder.Configuration["Bump:Hosting:Version"],
                        out var maj, out var min, out var pat))
                {
                    apps.SetVersionAsync("bump", maj, min, pat).GetAwaiter().GetResult();
                }
            }

            app.UseMiddleware<ProblemJsonExceptionHandler>();
            app.UseSerilogRequestLogging();

            var pathBase = builder.Configuration["PathBase"];
            if (!string.IsNullOrWhiteSpace(pathBase))
            {
                app.UsePathBase(pathBase);
            }

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
