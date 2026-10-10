using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roastgate.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPasskeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "passkeys",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    credential_id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    public_key = table.Column<byte[]>(type: "BLOB", nullable: false),
                    signature_counter = table.Column<long>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_passkeys", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_passkeys_credential_id",
                table: "passkeys",
                column: "credential_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "passkeys");
        }
    }
}
