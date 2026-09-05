using Calametra.Domain.Abstractions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Calametra.Api.Extensions;

/// <summary>
/// The single place a domain <see cref="Error"/> becomes an HTTP status code.
/// </summary>
/// <remarks>
/// Kept in one file so the mapping table exists exactly once. If a second place ever
/// translates errors, the two will disagree and the API's behaviour becomes a matter
/// of which code path ran.
/// </remarks>
internal static class ResultExtensions
{
    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess
            ? Results.NoContent()
            : Problem(result.Error!);

    public static IResult ToHttpResult<TValue>(this Result<TValue> result) =>
        result.IsSuccess
            ? Results.Ok(result.Value)
            : Problem(result.Error!);

    /// <summary>Maps a success to 201 Created with a location header.</summary>
    public static IResult ToCreatedResult<TValue>(this Result<TValue> result, string location) =>
        result.IsSuccess
            ? Results.Created(location, result.Value)
            : Problem(result.Error!);

    private static IResult Problem(Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };

        return Results.Problem(
            title: TitleFor(error.Type),
            detail: error.Description,
            statusCode: statusCode,
            // The stable machine key travels in an extension member so clients can
            // branch on it without parsing prose.
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    private static string TitleFor(ErrorType type) => type switch
    {
        ErrorType.Validation => "Validation failed",
        ErrorType.Unauthorized => "Authentication required",
        ErrorType.Forbidden => "Not permitted",
        ErrorType.NotFound => "Not found",
        ErrorType.Conflict => "Conflict",
        _ => "Request failed",
    };
}
