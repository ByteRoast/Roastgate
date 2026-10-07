using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roastgate.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTwoFactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "recovery_codes",
                table: "admin_account",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "totp_last_step",
                table: "admin_account",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "recovery_codes",
                table: "admin_account");

            migrationBuilder.DropColumn(
                name: "totp_last_step",
                table: "admin_account");
        }
    }
}
