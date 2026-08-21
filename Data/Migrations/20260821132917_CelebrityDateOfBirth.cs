using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StanTrack.Data.Migrations
{
    /// <inheritdoc />
    public partial class CelebrityDateOfBirth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DateOfBirth",
                table: "Celebrities",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "Celebrities");
        }
    }
}
