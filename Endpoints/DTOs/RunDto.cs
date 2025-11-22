namespace ResearchApi.Endpoints.DTOs;

public record AnswerDto(string Question, string Answer);

public record RunRequest(
    string Query,
    List<AnswerDto> Answers,
    int Breadth = 4,
    int Depth = 2);

public record RunResponse(Guid JobId, string Status);