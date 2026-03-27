using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResearchEngine.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceReliabilityAndDiscoveryModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Classification",
                table: "sources",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Domain",
                table: "sources",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPrimarySource",
                table: "sources",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ReliabilityRationale",
                table: "sources",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "ReliabilityScore",
                table: "sources",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "ReliabilityTier",
                table: "sources",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SearchCategory",
                table: "sources",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultDiscoveryMode",
                table: "runtime_settings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DiscoveryMode",
                table: "research_jobs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Classification",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "Domain",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "IsPrimarySource",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "ReliabilityRationale",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "ReliabilityScore",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "ReliabilityTier",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "SearchCategory",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "DefaultDiscoveryMode",
                table: "runtime_settings");

            migrationBuilder.DropColumn(
                name: "DiscoveryMode",
                table: "research_jobs");
        }
    }
}
