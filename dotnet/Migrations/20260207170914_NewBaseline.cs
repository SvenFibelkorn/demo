using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace dotnet.Migrations
{
    /// <inheritdoc />
    public partial class NewBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.Sql(
                "ALTER TABLE \"Articles\" ADD COLUMN IF NOT EXISTS \"SummaryEmbedding\" vector(768);");

            migrationBuilder.Sql(
                "ALTER TABLE \"Articles\" ADD COLUMN IF NOT EXISTS \"TitleEmbedding\" vector(768);");
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
