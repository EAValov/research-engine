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
                    TargetLanguage = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Region = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    ReportMarkdown = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "scraped_pages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Region = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scraped_pages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "clarifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Question = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Answer = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false)
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
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Stage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false)
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
                name: "visited_urls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_visited_urls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_visited_urls_research_jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "research_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "learnings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    PageId = table.Column<Guid>(type: "uuid", nullable: false),
                    QueryHash = table.Column<string>(type: "text", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Embedding = table.Column<Vector>(type: "vector(1024)", nullable: true),
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
                        name: "FK_learnings_scraped_pages_PageId",
                        column: x => x.PageId,
                        principalTable: "scraped_pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_clarifications_JobId",
                table: "clarifications",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_learnings_Embedding",
                table: "learnings",
                column: "Embedding")
                .Annotation("Npgsql:IndexMethod", "ivfflat")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" })
                .Annotation("Npgsql:StorageParameter:lists", 100);

            migrationBuilder.CreateIndex(
                name: "IX_learnings_JobId",
                table: "learnings",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_learnings_PageId_QueryHash",
                table: "learnings",
                columns: new[] { "PageId", "QueryHash" });

            migrationBuilder.CreateIndex(
                name: "IX_research_events_JobId",
                table: "research_events",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_scraped_pages_ContentHash",
                table: "scraped_pages",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_scraped_pages_Url",
                table: "scraped_pages",
                column: "Url");

            migrationBuilder.CreateIndex(
                name: "IX_visited_urls_JobId_Url",
                table: "visited_urls",
                columns: new[] { "JobId", "Url" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clarifications");

            migrationBuilder.DropTable(
                name: "learnings");

            migrationBuilder.DropTable(
                name: "research_events");

            migrationBuilder.DropTable(
                name: "visited_urls");

            migrationBuilder.DropTable(
                name: "scraped_pages");

            migrationBuilder.DropTable(
                name: "research_jobs");
        }
    }
}
