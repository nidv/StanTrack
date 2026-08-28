using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StanTrack.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncBookkeeping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastEventSyncAt",
                table: "Celebrities",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastMusicBrainzYield",
                table: "Celebrities",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastEventSyncAt",
                table: "Celebrities");

            migrationBuilder.DropColumn(
                name: "LastMusicBrainzYield",
                table: "Celebrities");
        }
    }
}
