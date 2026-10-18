using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PartyBot.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GameQueueRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    At = table.Column<long>(type: "INTEGER", nullable: false),
                    ActorId = table.Column<long>(type: "INTEGER", nullable: true),
                    ParticipantId = table.Column<int>(type: "INTEGER", nullable: true),
                    TaskId = table.Column<string>(type: "TEXT", nullable: true),
                    AttemptId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    OpenedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    SkipCooldownMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    SubmitCooldownMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    DemonstrationCatalog = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                    table.CheckConstraint("CK_Game_Singleton", "Id = 1");
                });

            migrationBuilder.CreateTable(
                name: "OutgoingActions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LogicalKey = table.Column<string>(type: "TEXT", nullable: false),
                    ChatId = table.Column<long>(type: "INTEGER", nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    RetryAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    MessageId = table.Column<int>(type: "INTEGER", nullable: true),
                    Failed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutgoingActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Participants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TelegramUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    PrivateChatId = table.Column<long>(type: "INTEGER", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    Disqualified = table.Column<bool>(type: "INTEGER", nullable: false),
                    NextTaskAvailableAt = table.Column<long>(type: "INTEGER", nullable: true),
                    NextBucket = table.Column<int>(type: "INTEGER", nullable: false),
                    NextPosition = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Participants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PollingStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    NextUpdateId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PollingStates", x => x.Id);
                    table.CheckConstraint("CK_Polling_Singleton", "Id = 1");
                });

            migrationBuilder.CreateTable(
                name: "ProcessedUpdates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedUpdates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaskDefinitions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    Points = table.Column<int>(type: "INTEGER", nullable: false),
                    Repeatable = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskDefinitions", x => x.Id);
                    table.CheckConstraint("CK_Task_Points", "Points > 0");
                });

            migrationBuilder.CreateTable(
                name: "Attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParticipantId = table.Column<int>(type: "INTEGER", nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    TakenAt = table.Column<long>(type: "INTEGER", nullable: false),
                    EndedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attempts", x => x.Id);
                    table.UniqueConstraint("AK_Attempts_Id_ParticipantId_TaskId", x => new { x.Id, x.ParticipantId, x.TaskId });
                    table.ForeignKey(
                        name: "FK_Attempts_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Attempts_TaskDefinitions_TaskId",
                        column: x => x.TaskId,
                        principalTable: "TaskDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskAvailabilities",
                columns: table => new
                {
                    ParticipantId = table.Column<int>(type: "INTEGER", nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", nullable: false),
                    Location = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrentAttemptId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskAvailabilities", x => new { x.ParticipantId, x.TaskId });
                    table.CheckConstraint("CK_Availability_Attempt", "(Location IN (2, 3) AND CurrentAttemptId IS NOT NULL) OR (Location IN (0, 1, 4) AND CurrentAttemptId IS NULL)");
                    table.ForeignKey(
                        name: "FK_TaskAvailabilities_Attempts_CurrentAttemptId_ParticipantId_TaskId",
                        columns: x => new { x.CurrentAttemptId, x.ParticipantId, x.TaskId },
                        principalTable: "Attempts",
                        principalColumns: new[] { "Id", "ParticipantId", "TaskId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskAvailabilities_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskAvailabilities_TaskDefinitions_TaskId",
                        column: x => x.TaskId,
                        principalTable: "TaskDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_ParticipantId",
                table: "Attempts",
                column: "ParticipantId",
                unique: true,
                filter: "State = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_TaskId",
                table: "Attempts",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_At",
                table: "AuditEvents",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingActions_Failed_MessageId_RetryAt",
                table: "OutgoingActions",
                columns: new[] { "Failed", "MessageId", "RetryAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingActions_LogicalKey",
                table: "OutgoingActions",
                column: "LogicalKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Participants_PrivateChatId",
                table: "Participants",
                column: "PrivateChatId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Participants_TelegramUserId",
                table: "Participants",
                column: "TelegramUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskAvailabilities_CurrentAttemptId_ParticipantId_TaskId",
                table: "TaskAvailabilities",
                columns: new[] { "CurrentAttemptId", "ParticipantId", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskAvailabilities_ParticipantId",
                table: "TaskAvailabilities",
                column: "ParticipantId",
                unique: true,
                filter: "Location = 2");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAvailabilities_TaskId",
                table: "TaskAvailabilities",
                column: "TaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "Games");

            migrationBuilder.DropTable(
                name: "OutgoingActions");

            migrationBuilder.DropTable(
                name: "PollingStates");

            migrationBuilder.DropTable(
                name: "ProcessedUpdates");

            migrationBuilder.DropTable(
                name: "TaskAvailabilities");

            migrationBuilder.DropTable(
                name: "Attempts");

            migrationBuilder.DropTable(
                name: "Participants");

            migrationBuilder.DropTable(
                name: "TaskDefinitions");
        }
    }
}
