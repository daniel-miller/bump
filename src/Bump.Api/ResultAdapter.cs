using Microsoft.AspNetCore.Mvc;

namespace Bump.Api;

/// <summary>
/// Adapts an <see cref="IResult"/> (the minimal-API response shape used by
/// <see cref="JsonResults"/>) into an <see cref="IActionResult"/> so MVC
/// controllers can return them directly. Keeps the response helpers in one
/// place across the codebase.
/// </summary>
public sealed class ResultAdapter : IActionResult
{
    private readonly IResult _inner;

    public ResultAdapter(IResult inner) => _inner = inner;

    public Task ExecuteResultAsync(ActionContext context) =>
        _inner.ExecuteAsync(context.HttpContext);
}

public static class ResultAdapterExtensions
{
    /// <summary>Wraps an <see cref="IResult"/> as an <see cref="IActionResult"/>.</summary>
    public static IActionResult AsAction(this IResult result) => new ResultAdapter(result);
}
