using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PackForge.Web.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Published",
                table: "PackageBuilds",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAt",
                table: "PackageBuilds",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Published",
                table: "PackageBuilds");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "PackageBuilds");
        }
    }
}
