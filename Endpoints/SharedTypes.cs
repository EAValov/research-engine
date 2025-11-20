using System.Text;
using System.Text.RegularExpressions;
using ResearchApi.Domain;

public sealed class OpenAiChatMessage
{
    public string Role { get; set; } = default!;
    public string Content { get; set; } = default!;
}

public sealed class OpenAiChatRequest
{
    public string Model { get; set; } = "local-deep-research";
    public bool Stream { get; set; } = true;
    public List<OpenAiChatMessage> Messages { get; set; } = new();
}
