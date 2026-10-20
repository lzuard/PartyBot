using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PartyBot.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GameFinalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ClosedAt",
                table: "Games",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinalReason",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FinalizedBy",
                table: "Games",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FinishedAt",
                table: "Games",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ForceFinalized",
                table: "Games",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "UnreviewedCount",
                table: "Games",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "FinalStandings",
                columns: table => new
                {
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    ParticipantId = table.Column<int>(type: "INTEGER", nullable: false),
                    TelegramUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Disqualified = table.Column<bool>(type: "INTEGER", nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: true),
                    Points = table.Column<long>(type: "INTEGER", nullable: false),
                    LastApprovedSubmittedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinalStandings", x => new { x.GameId, x.ParticipantId });
                    table.ForeignKey(
                        name: "FK_FinalStandings_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinalStandings_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransitionRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<long>(type: "INTEGER", nullable: false),
                    GameVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    PendingCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransitionRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinalStandings_GameId_Rank",
                table: "FinalStandings",
                columns: new[] { "GameId", "Rank" },
                unique: true,
                filter: "Rank IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FinalStandings_GameId_TelegramUserId",
                table: "FinalStandings",
                columns: new[] { "GameId", "TelegramUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinalStandings_ParticipantId",
                table: "FinalStandings",
                column: "ParticipantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinalStandings");

            migrationBuilder.DropTable(
                name: "TransitionRequests");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "FinalReason",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "FinalizedBy",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "FinishedAt",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "ForceFinalized",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "UnreviewedCount",
                table: "Games");
        }
    }
}
