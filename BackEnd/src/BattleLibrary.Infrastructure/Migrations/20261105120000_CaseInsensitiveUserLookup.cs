using BattleLibrary.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BattleLibrary.Infrastructure.Migrations
{
    // Expression indexes backing lower(column) lookups. Non-unique so legacy case-only duplicates cannot block startup.
    [DbContext(typeof(BattleLibraryDbContext))]
    [Migration("20261105120000_CaseInsensitiveUserLookup")]
    public partial class CaseInsensitiveUserLookup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Users_Username_Lower\" ON \"Users\" (lower(\"Username\"));");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Users_Email_Lower\" ON \"Users\" (lower(\"Email\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Users_Email_Lower\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Users_Username_Lower\";");
        }
    }
}
