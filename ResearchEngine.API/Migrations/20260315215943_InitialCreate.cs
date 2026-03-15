using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace ResearchEngine.API.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "research_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Query = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ChatModelName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EmbeddingModelName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Breadth = table.Column<int>(type: "integer", nullable: false),
                    Depth = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TargetLanguage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Region = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    HangfireJobId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CancelRequested = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CancelRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "runtime_settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    LimitSearches = table.Column<int>(type: "integer", nullable: false),
                    MaxUrlParallelism = table.Column<int>(type: "integer", nullable: false),
                    MaxUrlsPerSerpQuery = table.Column<int>(type: "integer", nullable: false),
                    MinImportance = table.Column<float>(type: "real", nullable: false),
                    DiversityMaxPerUrl = table.Column<int>(type: "integer", nullable: false),
                    DiversityMaxTextSimilarity = table.Column<double>(type: "double precision", nullable: false),
                    MaxLearningsPerSegment = table.Column<int>(type: "integer", nullable: false),
                    MinLearningsPerSegment = table.Column<int>(type: "integer", nullable: false),
                    GroupAssignSimilarityThreshold = table.Column<float>(type: "real", nullable: false),
                    GroupSearchTopK = table.Column<int>(type: "integer", nullable: false),
                    MaxEvidenceLength = table.Column<int>(type: "integer", nullable: false),
                    ChatEndpoint = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ChatApiKey = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ChatModelId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ChatMaxContextLength = table.Column<int>(type: "integer", nullable: true),
                    CrawlEndpoint = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CrawlApiKey = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CrawlHttpClientTimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runtime_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "clarifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Question = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Answer = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clarifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_clarifications_research_jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "research_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "learning_groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalText = table.Column<string>(type: "text", nullable: false),
                    CanonicalImportanceScore = table.Column<float>(type: "real", nullable: false),
                    MemberCount = table.Column<int>(type: "integer", nullable: false),
                    DistinctSourceCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_groups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_groups_research_jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "research_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "research_events",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Stage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    SynthesisId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_research_events_research_jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "research_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Language = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Region = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sources_research_jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "research_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "syntheses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChatModelName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EmbeddingModelName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ParentSynthesisId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Outline = table.Column<string>(type: "text", nullable: true),
                    Instructions = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_syntheses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_syntheses_research_jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "research_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_syntheses_syntheses_ParentSynthesisId",
                        column: x => x.ParentSynthesisId,
                        principalTable: "syntheses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "learning_group_embeddings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LearningGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Vector = table.Column<Vector>(type: "vector(1024)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_group_embeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_group_embeddings_learning_groups_LearningGroupId",
                        column: x => x.LearningGroupId,
                        principalTable: "learning_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "learnings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LearningGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsUserProvided = table.Column<bool>(type: "boolean", nullable: false),
                    QueryHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    ImportanceScore = table.Column<float>(type: "real", nullable: false),
                    EvidenceText = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learnings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learnings_learning_groups_LearningGroupId",
                        column: x => x.LearningGroupId,
                        principalTable: "learning_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_learnings_research_jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "research_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_learnings_sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "synthesis_sections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SynthesisId = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Index = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IsConclusion = table.Column<bool>(type: "boolean", nullable: false),
                    ContentMarkdown = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_synthesis_sections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_synthesis_sections_syntheses_SynthesisId",
                        column: x => x.SynthesisId,
                        principalTable: "syntheses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "synthesis_source_overrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SynthesisId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Excluded = table.Column<bool>(type: "boolean", nullable: true),
                    Pinned = table.Column<bool>(type: "boolean", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_synthesis_source_overrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_synthesis_source_overrides_sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_synthesis_source_overrides_syntheses_SynthesisId",
                        column: x => x.SynthesisId,
                        principalTable: "syntheses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "learning_embeddings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LearningId = table.Column<Guid>(type: "uuid", nullable: false),
                    Vector = table.Column<Vector>(type: "vector(1024)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_embeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_embeddings_learnings_LearningId",
                        column: x => x.LearningId,
                        principalTable: "learnings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "synthesis_learning_overrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SynthesisId = table.Column<Guid>(type: "uuid", nullable: false),
                    LearningId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScoreOverride = table.Column<float>(type: "real", nullable: true),
                    Excluded = table.Column<bool>(type: "boolean", nullable: true),
                    Pinned = table.Column<bool>(type: "boolean", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_synthesis_learning_overrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_synthesis_learning_overrides_learnings_LearningId",
                        column: x => x.LearningId,
                        principalTable: "learnings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_synthesis_learning_overrides_syntheses_SynthesisId",
                        column: x => x.SynthesisId,
                        principalTable: "syntheses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_clarifications_JobId",
                table: "clarifications",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_learning_embeddings_LearningId",
                table: "learning_embeddings",
                column: "LearningId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_learning_embeddings_Vector",
                table: "learning_embeddings",
                column: "Vector")
                .Annotation("Npgsql:IndexMethod", "ivfflat")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" })
                .Annotation("Npgsql:StorageParameter:lists", 100);

            migrationBuilder.CreateIndex(
                name: "IX_learning_group_embeddings_LearningGroupId",
                table: "learning_group_embeddings",
                column: "LearningGroupId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_learning_group_embeddings_Vector",
                table: "learning_group_embeddings",
                column: "Vector")
                .Annotation("Npgsql:IndexMethod", "ivfflat")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" })
                .Annotation("Npgsql:StorageParameter:lists", 100);

            migrationBuilder.CreateIndex(
                name: "IX_learning_groups_JobId_UpdatedAt",
                table: "learning_groups",
                columns: new[] { "JobId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_learnings_JobId_DeletedAt",
                table: "learnings",
                columns: new[] { "JobId", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_learnings_JobId_LearningGroupId",
                table: "learnings",
                columns: new[] { "JobId", "LearningGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_learnings_JobId_SourceId",
                table: "learnings",
                columns: new[] { "JobId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_learnings_LearningGroupId",
                table: "learnings",
                column: "LearningGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_learnings_SourceId_QueryHash",
                table: "learnings",
                columns: new[] { "SourceId", "QueryHash" });

            migrationBuilder.CreateIndex(
                name: "IX_research_events_JobId_SynthesisId",
                table: "research_events",
                columns: new[] { "JobId", "SynthesisId" });

            migrationBuilder.CreateIndex(
                name: "IX_research_jobs_DeletedAt",
                table: "research_jobs",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_research_jobs_Status",
                table: "research_jobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_sources_ContentHash",
                table: "sources",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_sources_JobId_DeletedAt",
                table: "sources",
                columns: new[] { "JobId", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sources_JobId_Reference",
                table: "sources",
                columns: new[] { "JobId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_syntheses_JobId_CreatedAt",
                table: "syntheses",
                columns: new[] { "JobId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_syntheses_ParentSynthesisId",
                table: "syntheses",
                column: "ParentSynthesisId");

            migrationBuilder.CreateIndex(
                name: "IX_syntheses_Status",
                table: "syntheses",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_synthesis_learning_overrides_LearningId",
                table: "synthesis_learning_overrides",
                column: "LearningId");

            migrationBuilder.CreateIndex(
                name: "IX_synthesis_learning_overrides_SynthesisId_LearningId",
                table: "synthesis_learning_overrides",
                columns: new[] { "SynthesisId", "LearningId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_synthesis_sections_SynthesisId_Index",
                table: "synthesis_sections",
                columns: new[] { "SynthesisId", "Index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_synthesis_sections_SynthesisId_SectionKey",
                table: "synthesis_sections",
                columns: new[] { "SynthesisId", "SectionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_synthesis_source_overrides_SourceId",
                table: "synthesis_source_overrides",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_synthesis_source_overrides_SynthesisId_SourceId",
                table: "synthesis_source_overrides",
                columns: new[] { "SynthesisId", "SourceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clarifications");

            migrationBuilder.DropTable(
                name: "learning_embeddings");

            migrationBuilder.DropTable(
                name: "learning_group_embeddings");

            migrationBuilder.DropTable(
                name: "research_events");

            migrationBuilder.DropTable(
                name: "runtime_settings");

            migrationBuilder.DropTable(
                name: "synthesis_learning_overrides");

            migrationBuilder.DropTable(
                name: "synthesis_sections");

            migrationBuilder.DropTable(
                name: "synthesis_source_overrides");

            migrationBuilder.DropTable(
                name: "learnings");

            migrationBuilder.DropTable(
                name: "syntheses");

            migrationBuilder.DropTable(
                name: "learning_groups");

            migrationBuilder.DropTable(
                name: "sources");

            migrationBuilder.DropTable(
                name: "research_jobs");
        }
    }
}
