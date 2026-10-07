using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roastgate.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_account",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    username = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    password_hash = table.Column<string>(type: "TEXT", nullable: false),
                    totp_secret = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_account", x => x.id);
                    table.CheckConstraint("ck_admin_account_single_row", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    occurred_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    actor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    target = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    success = table.Column<bool>(type: "INTEGER", nullable: false),
                    details = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settings", x => x.key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_account");

            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "settings");
        }
    }
}
