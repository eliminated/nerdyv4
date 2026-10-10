using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Nerdy.Contracts;

namespace Nerdy.Api.Infrastructure;

/// <summary>
/// Collects field errors and turns them into a 400 validation problem
/// (<c>{ status, title, errors: { "field.path": ["message"] }, code: "validation_failed" }</c>).
/// </summary>
public sealed partial class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool Any => _errors.Count > 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var messages))
        {
            _errors[field] = messages = [];
        }

        messages.Add(message);
    }

    public void MaxLength(string field, string? value, int max)
    {
        if (value is not null && value.Length > max)
        {
            Add(field, $"Must be at most {max} characters.");
        }
    }

    public void Required(string field, string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(field, "Is required.");
        }
        else
        {
            MaxLength(field, value, max);
        }
    }

    public void Range(string field, long value, long min, long max)
    {
        if (value < min || value > max)
        {
            Add(field, $"Must be between {min} and {max}.");
        }
    }

    public void ColorHex(string field, string? value)
    {
        if (value is not null && !ColorHexPattern().IsMatch(value))
        {
            Add(field, "Must be a colour in the form #RRGGBB.");
        }
    }

    public void Defined<TEnum>(string field, TEnum value) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            Add(field, $"Is not a valid {typeof(TEnum).Name}.");
        }
    }

    public ValidationProblem ToProblem() => TypedResults.ValidationProblem(
        _errors.ToDictionary(e => e.Key, e => e.Value.ToArray()),
        extensions: new Dictionary<string, object?> { ["code"] = ApiErrorCodes.ValidationFailed });

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorHexPattern();
}
