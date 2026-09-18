using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CityPulseAI.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DepartmentId",
                table: "Incidents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DepartmentId",
                table: "Crews",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ManagerName = table.Column<string>(type: "text", nullable: false),
                    PhoneNumber = table.Column<string>(type: "text", nullable: false),
                    BudgetLimit = table.Column<decimal>(type: "numeric", nullable: false),
                    BudgetSpent = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_DepartmentId",
                table: "Incidents",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Crews_DepartmentId",
                table: "Crews",
                column: "DepartmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Crews_Departments_DepartmentId",
                table: "Crews",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Incidents_Departments_DepartmentId",
                table: "Incidents",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Crews_Departments_DepartmentId",
                table: "Crews");

            migrationBuilder.DropForeignKey(
                name: "FK_Incidents_Departments_DepartmentId",
                table: "Incidents");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_Incidents_DepartmentId",
                table: "Incidents");

            migrationBuilder.DropIndex(
                name: "IX_Crews_DepartmentId",
                table: "Crews");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "Crews");
        }
    }
}
