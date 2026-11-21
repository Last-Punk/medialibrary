using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MediaLibrary.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersAndOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OwnerId",
                table: "MediaItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: true),
                    FirstName = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            // Existing media rows receive OwnerId 0 from the column default above.
            // Keep a non-authenticatable legacy owner so the new foreign key remains valid.
            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "CreatedAtUtc", "FirstName", "LastSeenAtUtc", "TelegramUserId", "Username" },
                values: new object[]
                {
                    0,
                    new DateTime(2026, 11, 17, 0, 0, 0, DateTimeKind.Utc),
                    "Legacy data",
                    new DateTime(2026, 11, 17, 0, 0, 0, DateTimeKind.Utc),
                    -1L,
                    "legacy"
                });

            migrationBuilder.AlterColumn<int>(
                name: "OwnerId",
                table: "MediaItems",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_OwnerId_ExternalSource_ExternalId",
                table: "MediaItems",
                columns: new[] { "OwnerId", "ExternalSource", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_OwnerId_Status",
                table: "MediaItems",
                columns: new[] { "OwnerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_TelegramUserId",
                table: "Users",
                column: "TelegramUserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MediaItems_Users_OwnerId",
                table: "MediaItems",
                column: "OwnerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaItems_Users_OwnerId",
                table: "MediaItems");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropIndex(
                name: "IX_MediaItems_OwnerId_ExternalSource_ExternalId",
                table: "MediaItems");

            migrationBuilder.DropIndex(
                name: "IX_MediaItems_OwnerId_Status",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "MediaItems");
        }
    }
}
