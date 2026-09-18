using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CityPulseAI.Migrations
{
    /// <inheritdoc />
    public partial class AddArchiveReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArchiveReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DepartmentId = table.Column<int>(type: "integer", nullable: false),
                    IncidentId = table.Column<int>(type: "integer", nullable: false),
                    IncidentTitle = table.Column<string>(type: "text", nullable: false),
                    IncidentDescription = table.Column<string>(type: "text", nullable: false),
                    ReportedBy = table.Column<string>(type: "text", nullable: false),
                    ResolvedByCrew = table.Column<string>(type: "text", nullable: false),
                    BudgetSpent = table.Column<decimal>(type: "numeric", nullable: false),
                    ReportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchiveReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArchiveReports_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveReports_DepartmentId",
                table: "ArchiveReports",
                column: "DepartmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArchiveReports");
        }
    }
}
