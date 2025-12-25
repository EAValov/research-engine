using System.Text.Json;

public sealed class SynthesisOutline
{
    public required List<SynthesisOutlineSection> Sections { get; init; } = new();

    public static bool TryParse(string? outlineJson, out SynthesisOutline? outline)
    {
        try
        {
            if(string.IsNullOrWhiteSpace(outlineJson))
            {
                outline = null;
                return false;
            }

            outline = JsonSerializer.Deserialize<SynthesisOutline>(outlineJson, new JsonSerializerOptions () {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            if(outline is not null)
                return true;

            return false;
        }
        catch
        {
            outline = null;
            return false;
        }
    }
}

public sealed class SynthesisOutlineSection
{
    /// <summary>
    /// Stable identity across syntheses. If null => treat as NEW section (generate Guid).
    /// </summary>
    public Guid? SectionKey { get; init; }

    /// <summary>1-based, contiguous preferred. Will be normalized.</summary>
    public int Index { get; init; }

    public required string Title { get; init; }
    public required string Description { get; init; }

    public bool IsConclusion { get; init; }
}