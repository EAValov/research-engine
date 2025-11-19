using System;
using System.Collections.Generic;

namespace ResearchApi.Endpoints;

public record PlanRequest(string Query, int MaxQuestions = 3);

public record PlanResponse(string Query, IReadOnlyList<string> Questions);
