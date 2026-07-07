using System.Text;

namespace Bump.Api;

/// <summary>
/// Renders an <see cref="ExceptionInfo"/> chain as Markdown. The chain is
/// flattened post-order so the innermost exception is shown first
/// (matching how Sentry-style viewers display causality).
/// </summary>
public static class ExceptionMarkdown
{
    public static string Render(ExceptionInfo? root)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Exceptions");
        sb.AppendLine();

        if (root is null)
        {
            sb.AppendLine("_No exception attached._");
            return sb.ToString();
        }

        var flat = new List<ExceptionInfo>();
        Flatten(root, flat);

        for (int i = 0; i < flat.Count; i++)
        {
            var ex = flat[i];
            sb.Append("### Exception ").Append(i + 1).AppendLine();
            sb.Append("**Type:** ").AppendLine(ex.Type);
            sb.Append("**Value:** ").AppendLine(ex.Value);
            sb.AppendLine();
            sb.AppendLine("#### Stacktrace");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(string.IsNullOrWhiteSpace(ex.StackTrace) ? "(no stack trace)" : ex.StackTrace);
            sb.AppendLine("```");

            if (i < flat.Count - 1)
                sb.AppendLine("------");
        }

        return sb.ToString();
    }

    private static void Flatten(ExceptionInfo node, List<ExceptionInfo> acc)
    {
        if (node.InnerExceptions is { Count: > 0 } children)
        {
            foreach (var child in children)
                Flatten(child, acc);
        }
        acc.Add(node);
    }
}
