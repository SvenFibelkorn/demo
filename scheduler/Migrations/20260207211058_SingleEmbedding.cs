using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace scheduler.Migrations
{
    /// <inheritdoc />
    public partial class SingleEmbedding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
                        migrationBuilder.Sql(@"DO $$
BEGIN
        IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = current_schema()
                    AND table_name = 'Articles'
                    AND column_name = 'SummaryEmbedding') THEN
                EXECUTE 'ALTER TABLE ""Articles"" DROP COLUMN IF EXISTS ""SummaryEmbedding""';
        END IF;
        IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = current_schema()
                    AND table_name = 'Articles'
                    AND column_name = 'TitleEmbedding') THEN
                EXECUTE 'ALTER TABLE ""Articles"" RENAME COLUMN ""TitleEmbedding"" TO ""Embedding""';
        END IF;
END
$$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
                        migrationBuilder.Sql(@"DO $$
BEGIN
        IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = current_schema()
                    AND table_name = 'Articles'
                    AND column_name = 'Embedding') THEN
                EXECUTE 'ALTER TABLE ""Articles"" RENAME COLUMN ""Embedding"" TO ""TitleEmbedding""';
        END IF;
        IF NOT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = current_schema()
                    AND table_name = 'Articles'
                    AND column_name = 'SummaryEmbedding') THEN
                EXECUTE 'ALTER TABLE ""Articles"" ADD COLUMN ""SummaryEmbedding"" vector(768)';
        END IF;
END
$$;");
        }
    }
}
