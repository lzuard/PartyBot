using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PartyBot.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecoveryRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<long>(type: "INTEGER", nullable: false),
                    ActionId = table.Column<long>(type: "INTEGER", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecoveryRequests_OutgoingActions_ActionId",
                        column: x => x.ActionId,
                        principalTable: "OutgoingActions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryRequests_ActionId",
                table: "RecoveryRequests",
                column: "ActionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecoveryRequests");
        }
    }
}
