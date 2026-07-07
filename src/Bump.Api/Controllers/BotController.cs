using System.Net;
using Bump.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Bump.Api.Controllers;

/// <summary>
/// Public landing page for the URL embedded in the prober's
/// <c>User-Agent</c> header. Target audience: a sysadmin who just
/// noticed Bump in their access log and wants to know who/what/why.
/// Returns plain HTML, no JS, no auth, indexable=false.
/// </summary>
[ApiController]
[Route("bot")]
public sealed class BotController : ControllerBase
{
    private readonly IConfiguration _config;

    public BotController(IConfiguration config) => _config = config;

    [HttpGet("")]
    [HttpHead("")]
    public ContentResult Get()
    {
        var ua = BumpUserAgent.Build(_config);
        var contact = _config["Bump:Services:AbuseContact"];
        var interval = _config.GetValue("Bump:Services:IntervalSeconds", 60);
        var timeout = _config.GetValue("Bump:Services:TimeoutSeconds", 5);

        var contactBlock = string.IsNullOrWhiteSpace(contact)
            ? ""
            : $"""
              <h2>Contact</h2>
              <p>Abuse reports or stop-probing requests: <a href="mailto:{WebUtility.HtmlEncode(contact)}">{WebUtility.HtmlEncode(contact)}</a>. Include the URL you want removed and we will take it off the customer's service list.</p>
              """;

        var html = $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width,initial-scale=1">
          <meta name="robots" content="noindex,nofollow">
          <title>About the Bump bot</title>
          <style>
            :root { color-scheme: light dark; }
            body { font: 16px/1.55 system-ui, -apple-system, Segoe UI, sans-serif; max-width: 44rem; margin: 2.5rem auto; padding: 0 1rem; }
            h1 { margin: 0 0 0.25rem; }
            h2 { margin-top: 2rem; }
            code, pre { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: 0.95em; }
            pre { background: rgba(127,127,127,0.12); padding: 0.75rem 1rem; border-radius: 6px; overflow-x: auto; }
            .muted { color: #666; }
            ul li { margin: 0.25rem 0; }
          </style>
        </head>
        <body>
          <h1>The Bump bot</h1>
          <p class="muted">A small HTTP uptime prober. It reaches your URL only because a Bump customer added it to their service list.</p>

          <h2>What it does</h2>
          <p>Bump sends a single HTTP <code>GET</code> against each configured URL roughly every {{interval}} seconds, with a {{timeout}}-second timeout. It records the response status code and latency. It does <strong>not</strong>:</p>
          <ul>
            <li>follow redirects (a <code>30x</code> is recorded as-is)</li>
            <li>execute JavaScript or render pages</li>
            <li>submit forms, log in, or accept cookies</li>
            <li>crawl links from the response</li>
          </ul>

          <h2>How to identify it</h2>
          <p>Every probe carries this exact <code>User-Agent</code> header:</p>
          <pre>{{WebUtility.HtmlEncode(ua)}}</pre>
          <p>The token always starts with <code>Bump/</code>. The URL inside the parentheses points back to this page.</p>

          <h2>How to block it</h2>
          <ul>
            <li>Block by <code>User-Agent</code> prefix <code>Bump/</code> at your edge, WAF, or CDN.</li>
            <li>Or return HTTP <code>403</code> / <code>410</code>. The prober does not retry on its own &mdash; it records the status until the Bump customer removes the URL.</li>
            <li><code>robots.txt</code> is not consulted: this is a health probe, not a crawler. Use one of the methods above.</li>
          </ul>

          {{contactBlock}}
        </body>
        </html>
        """;

        return new ContentResult
        {
            Content = html,
            ContentType = "text/html; charset=utf-8",
            StatusCode = StatusCodes.Status200OK
        };
    }
}
