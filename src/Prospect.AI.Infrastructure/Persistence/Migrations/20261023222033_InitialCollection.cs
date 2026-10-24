using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prospect.AI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCollection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_collected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "raw_postings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    canonical_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    content_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    collected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_raw_postings", x => x.id);
                    table.ForeignKey(
                        name: "fk_raw_postings_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "missions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_posting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    company_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_missions", x => x.id);
                    table.ForeignKey(
                        name: "fk_missions_raw_postings_raw_posting_id",
                        column: x => x.raw_posting_id,
                        principalTable: "raw_postings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_missions_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_missions_owner_id_status_created_at",
                table: "missions",
                columns: new[] { "owner_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_missions_raw_posting_id",
                table: "missions",
                column: "raw_posting_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_missions_source_id",
                table: "missions",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_raw_postings_owner_id_canonical_url",
                table: "raw_postings",
                columns: new[] { "owner_id", "canonical_url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_raw_postings_owner_id_content_hash",
                table: "raw_postings",
                columns: new[] { "owner_id", "content_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_raw_postings_source_id",
                table: "raw_postings",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_sources_owner_id",
                table: "sources",
                column: "owner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "missions");

            migrationBuilder.DropTable(
                name: "raw_postings");

            migrationBuilder.DropTable(
                name: "sources");
        }
    }
}
