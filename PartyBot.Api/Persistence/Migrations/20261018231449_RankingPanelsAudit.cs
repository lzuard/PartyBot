using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PartyBot.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RankingPanelsAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DisciplineRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<long>(type: "INTEGER", nullable: false),
                    ParticipantId = table.Column<int>(type: "INTEGER", nullable: false),
                    ParticipantVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    GameVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Disqualify = table.Column<bool>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisciplineRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DisciplineRequests_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DisciplineRequests_ParticipantId",
                table: "DisciplineRequests",
                column: "ParticipantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DisciplineRequests");
        }
    }
}
