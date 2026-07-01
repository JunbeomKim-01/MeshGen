using System.Text.Json.Serialization;

namespace MeshGenLite.Integration.Mcp.Dtos;

/// <summary>
/// Uniform response envelope for every MeshGen MCP tool. Always serializable
/// regardless of success or failure so callers (LLM/Hub) can branch on
/// <see cref="Success"/> without parsing exception messages.
/// </summary>
public sealed class CommonToolResponse<T>
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public List<string> Warnings { get; init; } = new();

    public T? Data { get; init; }

    public static CommonToolResponse<T> Ok(string message, T data, params string[] warnings) =>
        new()
        {
            Success = true,
            Message = message,
            Data = data,
            Warnings = warnings.Length == 0 ? new List<string>() : new List<string>(warnings)
        };

    public static CommonToolResponse<T> Fail(string message) =>
        new() { Success = false, Message = message };
}

/// <summary>Convenience non-generic alias for tools that return no payload.</summary>
public sealed class EmptyData
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; } = true;
}
