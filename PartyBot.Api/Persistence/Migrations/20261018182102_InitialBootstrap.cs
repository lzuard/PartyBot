using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PartyBot.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialBootstrap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bootstrap",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bootstrap", x => x.Id);
                    table.CheckConstraint("CK_Bootstrap_Singleton", "Id = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bootstrap");
        }
    }
}
