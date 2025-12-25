using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace ResearchApi.Migrations
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
                    Breadth = table.Column<int>(type: "integer", nullable: false),
                    Depth = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TargetLanguage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Region = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_jobs", x => x.Id);
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
                name: "research_events",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Stage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false)
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
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Region = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
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
                name: "learnings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    QueryHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    ImportanceScore = table.Column<float>(type: "real", nullable: false),
                    EvidenceText = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learnings", x => x.Id);
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
                name: "IX_learnings_JobId_SourceId",
                table: "learnings",
                columns: new[] { "JobId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_learnings_SourceId_QueryHash",
                table: "learnings",
                columns: new[] { "SourceId", "QueryHash" });

            migrationBuilder.CreateIndex(
                name: "IX_research_events_JobId",
                table: "research_events",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_sources_ContentHash",
                table: "sources",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_sources_JobId_Url",
                table: "sources",
                columns: new[] { "JobId", "Url" },
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
                name: "research_events");

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
                name: "sources");

            migrationBuilder.DropTable(
                name: "research_jobs");
        }
    }
}
