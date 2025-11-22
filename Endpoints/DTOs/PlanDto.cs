namespace ResearchApi.Endpoints.DTOs;

public record PlanRequest(string Query, int MaxQuestions = 3);

public record PlanResponse(string Query, IReadOnlyList<string> Questions);
