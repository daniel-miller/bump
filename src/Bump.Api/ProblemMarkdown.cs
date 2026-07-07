using System.Text;
using Newtonsoft.Json;

namespace Bump.Api;

/// <summary>
/// Renders a full <see cref="ProblemReportRecord"/> as Markdown — problem
/// fields, context (app/environment/account), extensions, and the exception
/// chain. Suitable for pasting into a bug tracker or downloading as a .md.
/// </summary>
public static class ProblemMarkdown
{
    public static string Render(ProblemReportRecord r)
    {
        var sb = new StringBuilder();

        sb.Append("# Problem #").Append(r.ProblemKey);
        if (!string.IsNullOrWhiteSpace(r.Title))
            sb.Append(" — ").Append(r.Title);
        sb.AppendLine();
        sb.AppendLine();

        sb.AppendLine("## Problem");
        sb.AppendLine();
        AppendKv(sb, "Type", r.Type);
        AppendKv(sb, "Title", r.Title);
        AppendKv(sb, "Status", r.Status?.ToString());
        AppendKv(sb, "Detail", r.Detail);
        AppendKv(sb, "Instance", r.Instance);
        AppendKv(sb, "Fingerprint", r.Fingerprint);
        AppendKv(sb, "Reported at", r.ReportedAt.ToString("u"));
        AppendKv(sb, "Dispatched at", r.DispatchedAt?.ToString("u"));
        AppendKv(sb, "Resolved at", r.ResolvedAt?.ToString("u"));
        sb.AppendLine();

        sb.AppendLine("## Context");
        sb.AppendLine();
        AppendKv(sb, "App",         Join(" · ", r.AppSlug, r.AppName, "v" + r.AppVersion));
        AppendKv(sb, "Environment", Join(" · ", r.Environment, r.EnvironmentName, r.EnvironmentDescription));
        AppendKv(sb, "Account",     Join(" · ", r.UserId?.ToString(), r.UserEmail));
        sb.AppendLine();

        AppendExtensions(sb, r.Extensions);

        sb.Append(ExceptionMarkdown.Render(r.Exception));

        return sb.ToString();
    }

    private static void AppendKv(StringBuilder sb, string key, string? value)
    {
        sb.Append("- **").Append(key).Append(":** ");
        if (string.IsNullOrWhiteSpace(value))
            sb.AppendLine("_(none)_");
        else
            sb.AppendLine(value);
    }

    private static string? Join(string sep, params string?[] parts)
    {
        var nonEmpty = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        return nonEmpty.Length == 0 ? null : string.Join(sep, nonEmpty);
    }

    private static void AppendExtensions(StringBuilder sb, string? extensionsJson)
    {
        sb.AppendLine("## Extensions");
        sb.AppendLine();

        if (string.IsNullOrWhiteSpace(extensionsJson))
        {
            sb.AppendLine("_(none)_");
            sb.AppendLine();
            return;
        }

        Dictionary<string, object?>? dict;
        try
        {
            dict = JsonConvert.DeserializeObject<Dictionary<string, object?>>(extensionsJson);
        }
        catch
        {
            sb.AppendLine("```json");
            sb.AppendLine(extensionsJson);
            sb.AppendLine("```");
            sb.AppendLine();
            return;
        }

        if (dict is null || dict.Count == 0)
        {
            sb.AppendLine("_(none)_");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Key | Value |");
        sb.AppendLine("|-----|-------|");
        foreach (var (k, v) in dict)
        {
            var valueStr = v switch
            {
                null              => "",
                string s          => s,
                _                 => JsonConvert.SerializeObject(v)
            };
            // Escape pipe + newline so they don't break the table row.
            valueStr = valueStr.Replace("|", "\\|").Replace("\r\n", " ").Replace("\n", " ");
            sb.Append("| ").Append(k.Replace("|", "\\|")).Append(" | ").Append(valueStr).AppendLine(" |");
        }
        sb.AppendLine();
    }
}
