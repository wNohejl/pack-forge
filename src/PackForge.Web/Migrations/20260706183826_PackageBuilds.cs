using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PackForge.Web.Migrations
{
    /// <inheritdoc />
    public partial class PackageBuilds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PackageBuilds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ModelSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PackageSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BlobName = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageBuilds", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackageBuilds_ModelName_Version",
                table: "PackageBuilds",
                columns: new[] { "ModelName", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackageBuilds_ModelSha256",
                table: "PackageBuilds",
                column: "ModelSha256");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackageBuilds");
        }
    }
}
