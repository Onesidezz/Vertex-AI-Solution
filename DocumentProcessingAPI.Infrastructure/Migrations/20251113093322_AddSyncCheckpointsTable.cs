using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DocumentProcessingAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncCheckpointsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncCheckpoints",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    JobName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastSyncDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastProcessedPage = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Completed"),
                    TotalRecordsProcessed = table.Column<long>(type: "bigint", nullable: false),
                    SuccessCount = table.Column<long>(type: "bigint", nullable: false),
                    FailureCount = table.Column<long>(type: "bigint", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncCheckpoints", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncCheckpoints_JobName",
                table: "SyncCheckpoints",
                column: "JobName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncCheckpoints_LastSyncDate",
                table: "SyncCheckpoints",
                column: "LastSyncDate");

            migrationBuilder.CreateIndex(
                name: "IX_SyncCheckpoints_Status",
                table: "SyncCheckpoints",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncCheckpoints");
        }
    }
}
