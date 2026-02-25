using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DailyTrackerAPI.Migrations
{
    /// <inheritdoc />
    public partial class supportLogMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaEvidences_Users_UserId",
                table: "MediaEvidences");

            migrationBuilder.AddForeignKey(
                name: "FK_MediaEvidences_Users_UserId",
                table: "MediaEvidences",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaEvidences_Users_UserId",
                table: "MediaEvidences");

            migrationBuilder.AddForeignKey(
                name: "FK_MediaEvidences_Users_UserId",
                table: "MediaEvidences",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
