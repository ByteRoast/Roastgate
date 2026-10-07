using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roastgate.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigBackups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "config_backups",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    area = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    reason = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_config_backups", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_config_backups_area_name",
                table: "config_backups",
                columns: new[] { "area", "name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "config_backups");
        }
    }
}
