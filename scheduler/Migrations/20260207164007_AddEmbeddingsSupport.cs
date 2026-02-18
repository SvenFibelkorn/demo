using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace scheduler.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbeddingsSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.AddColumn<Vector>(
                name: "SummaryEmbedding",
                table: "Articles",
                type: "vector(768)",
                nullable: true);

            migrationBuilder.AddColumn<Vector>(
                name: "TitleEmbedding",
                table: "Articles",
                type: "vector(768)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"Articles\" DROP COLUMN IF EXISTS \"SummaryEmbedding\";");
            migrationBuilder.Sql("ALTER TABLE \"Articles\" DROP COLUMN IF EXISTS \"TitleEmbedding\";");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
