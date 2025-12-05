using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResearchApi.Migrations
{
    /// <inheritdoc />
    public partial class LearningImportance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "ImportanceScore",
                table: "learnings",
                type: "real",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImportanceScore",
                table: "learnings");
        }
    }
}
