using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PackForge.Web.Migrations
{
    /// <inheritdoc />
    public partial class MigrationInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MigrationItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourcePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    SourceSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BlobSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BlobName = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    DiscoveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationItems_SourceSystem_SourcePath",
                table: "MigrationItems",
                columns: new[] { "SourceSystem", "SourcePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationItems_Status",
                table: "MigrationItems",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MigrationItems");
        }
    }
}
