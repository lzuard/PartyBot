using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PartyBot.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PhotoAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Done",
                table: "OutgoingActions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "EntityId",
                table: "OutgoingActions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EntityVersion",
                table: "OutgoingActions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "OutgoingActions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "OutgoingActions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseFileIds",
                table: "OutgoingActions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmissionId",
                table: "AuditEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AnswerCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParticipantId = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaGroupId = table.Column<string>(type: "TEXT", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    LastPartAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ReadyAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnswerCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnswerCandidates_Attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "Attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnswerCandidates_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Submissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParticipantId = table.Column<int>(type: "INTEGER", nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", nullable: false),
                    AuthorSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    TextSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    PointsSnapshot = table.Column<int>(type: "INTEGER", nullable: false),
                    RepeatableSnapshot = table.Column<bool>(type: "INTEGER", nullable: false),
                    SubmittedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    ModeratorId = table.Column<long>(type: "INTEGER", nullable: true),
                    DecidedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    RejectReason = table.Column<string>(type: "TEXT", nullable: true),
                    ArchiveChatId = table.Column<long>(type: "INTEGER", nullable: false),
                    WorkChatId = table.Column<long>(type: "INTEGER", nullable: false),
                    ArchiveCardId = table.Column<int>(type: "INTEGER", nullable: true),
                    WorkCardId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Submissions", x => x.Id);
                    table.CheckConstraint("CK_Submission_Points", "PointsSnapshot > 0");
                    table.ForeignKey(
                        name: "FK_Submissions_Attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "Attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Submissions_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Submissions_TaskDefinitions_TaskId",
                        column: x => x.TaskId,
                        principalTable: "TaskDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CandidateMedia",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CandidateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceChatId = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceMessageId = table.Column<int>(type: "INTEGER", nullable: false),
                    FileId = table.Column<string>(type: "TEXT", nullable: false),
                    FileUniqueId = table.Column<string>(type: "TEXT", nullable: false),
                    Caption = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateMedia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CandidateMedia_AnswerCandidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "AnswerCandidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SubmissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChatId = table.Column<long>(type: "INTEGER", nullable: false),
                    MessageId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsWork = table.Column<bool>(type: "INTEGER", nullable: false),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryMessages_Submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "Submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RejectChoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmissionVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ActorId = table.Column<long>(type: "INTEGER", nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RejectChoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RejectChoices_Submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "Submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScoreEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParticipantId = table.Column<int>(type: "INTEGER", nullable: false),
                    Points = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoreEntries", x => x.Id);
                    table.CheckConstraint("CK_Score_Positive", "Points > 0");
                    table.ForeignKey(
                        name: "FK_ScoreEntries_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScoreEntries_Submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "Submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubmissionMedia",
                columns: table => new
                {
                    SubmissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    FileId = table.Column<string>(type: "TEXT", nullable: false),
                    FileUniqueId = table.Column<string>(type: "TEXT", nullable: false),
                    SourceChatId = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceMessageId = table.Column<int>(type: "INTEGER", nullable: false),
                    CaptionSnapshot = table.Column<string>(type: "TEXT", nullable: true),
                    ArchiveMessageId = table.Column<int>(type: "INTEGER", nullable: true),
                    WorkMessageId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmissionMedia", x => new { x.SubmissionId, x.Order });
                    table.ForeignKey(
                        name: "FK_SubmissionMedia_Submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "Submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnswerCandidates_AttemptId",
                table: "AnswerCandidates",
                column: "AttemptId",
                unique: true,
                filter: "State IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_AnswerCandidates_ParticipantId_MediaGroupId",
                table: "AnswerCandidates",
                columns: new[] { "ParticipantId", "MediaGroupId" },
                unique: true,
                filter: "MediaGroupId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateMedia_CandidateId",
                table: "CandidateMedia",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateMedia_SourceChatId_SourceMessageId",
                table: "CandidateMedia",
                columns: new[] { "SourceChatId", "SourceMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryMessages_ChatId_MessageId",
                table: "DeliveryMessages",
                columns: new[] { "ChatId", "MessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryMessages_SubmissionId",
                table: "DeliveryMessages",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_RejectChoices_SubmissionId",
                table: "RejectChoices",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoreEntries_ParticipantId",
                table: "ScoreEntries",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoreEntries_SubmissionId",
                table: "ScoreEntries",
                column: "SubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionMedia_SourceChatId_SourceMessageId",
                table: "SubmissionMedia",
                columns: new[] { "SourceChatId", "SourceMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_AttemptId",
                table: "Submissions",
                column: "AttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_ParticipantId",
                table: "Submissions",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_TaskId",
                table: "Submissions",
                column: "TaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CandidateMedia");

            migrationBuilder.DropTable(
                name: "DeliveryMessages");

            migrationBuilder.DropTable(
                name: "RejectChoices");

            migrationBuilder.DropTable(
                name: "ScoreEntries");

            migrationBuilder.DropTable(
                name: "SubmissionMedia");

            migrationBuilder.DropTable(
                name: "AnswerCandidates");

            migrationBuilder.DropTable(
                name: "Submissions");

            migrationBuilder.DropColumn(
                name: "Done",
                table: "OutgoingActions");

            migrationBuilder.DropColumn(
                name: "EntityId",
                table: "OutgoingActions");

            migrationBuilder.DropColumn(
                name: "EntityVersion",
                table: "OutgoingActions");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "OutgoingActions");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "OutgoingActions");

            migrationBuilder.DropColumn(
                name: "UseFileIds",
                table: "OutgoingActions");

            migrationBuilder.DropColumn(
                name: "SubmissionId",
                table: "AuditEvents");
        }
    }
}
