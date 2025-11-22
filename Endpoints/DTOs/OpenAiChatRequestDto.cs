namespace ResearchApi.Endpoints.DTOs;

public sealed class OpenAiChatRequestDto
{
    public string Model { get; set; } = "local-deep-research";
    public bool Stream { get; set; } = true;
    public List<OpenAiChatMessageDto> Messages { get; set; } = new();
}

public sealed class OpenAiChatMessageDto
{
    public string Role { get; set; } = default!;
    public string Content { get; set; } = default!;
}