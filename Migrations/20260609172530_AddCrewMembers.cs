using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPulseAI.Migrations
{
    /// <inheritdoc />
    public partial class AddCrewMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Members",
                table: "Crews",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Members",
                table: "Crews");
        }
    }
}
