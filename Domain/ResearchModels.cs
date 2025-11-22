using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ResearchApi.Domain;

public record Clarification(string Question, string Answer);

public record Learning(string Text, string SourceUrl);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResearchJobStatus { Pending, Running, Completed, Failed }

public record ResearchEvent(
    DateTimeOffset Timestamp,
    string Stage,
    string Message
);

public record ResearchJob(
    Guid Id,
    string Query,
    List<Clarification> Clarifications,
    int Breadth,
    int Depth,
    ResearchJobStatus Status,
    List<ResearchEvent> Events,
    string? ReportMarkdown,
    List<string> VisitedUrls,
    string TargetLanguage = "en",
    string? Region = null
);
