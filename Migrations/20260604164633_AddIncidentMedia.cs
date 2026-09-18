using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPulseAI.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Incidents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "ArchiveReports",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "ArchiveReports");
        }
    }
}
