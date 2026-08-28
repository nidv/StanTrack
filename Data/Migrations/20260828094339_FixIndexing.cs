using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StanTrack.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixIndexing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Events_CelebrityId",
                table: "Events");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Celebrities",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "Celebrities",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_Events_CelebrityId_EventDate",
                table: "Events",
                columns: new[] { "CelebrityId", "EventDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_EventDate",
                table: "Events",
                column: "EventDate");

            migrationBuilder.CreateIndex(
                name: "IX_Celebrities_Category",
                table: "Celebrities",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Celebrities_Category_Name",
                table: "Celebrities",
                columns: new[] { "Category", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Celebrities_Name",
                table: "Celebrities",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Events_CelebrityId_EventDate",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_EventDate",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Celebrities_Category",
                table: "Celebrities");

            migrationBuilder.DropIndex(
                name: "IX_Celebrities_Category_Name",
                table: "Celebrities");

            migrationBuilder.DropIndex(
                name: "IX_Celebrities_Name",
                table: "Celebrities");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Celebrities",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "Celebrities",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.CreateIndex(
                name: "IX_Events_CelebrityId",
                table: "Events",
                column: "CelebrityId");
        }
    }
}
