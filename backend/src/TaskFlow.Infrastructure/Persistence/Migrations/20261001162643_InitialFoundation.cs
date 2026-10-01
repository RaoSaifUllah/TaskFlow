using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Workspaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspaces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Memberships",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Memberships", x => new { x.WorkspaceId, x.UserId });
                    table.ForeignKey(
                        name: "FK_Memberships_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.UniqueConstraint("AK_Projects_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.ForeignKey(
                        name: "FK_Projects_Memberships_WorkspaceId_CreatorId",
                        columns: x => new { x.WorkspaceId, x.CreatorId },
                        principalTable: "Memberships",
                        principalColumns: new[] { "WorkspaceId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectShares",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Access = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectShares", x => new { x.WorkspaceId, x.ProjectId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ProjectShares_Memberships_WorkspaceId_UserId",
                        columns: x => new { x.WorkspaceId, x.UserId },
                        principalTable: "Memberships",
                        principalColumns: new[] { "WorkspaceId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectShares_Projects_WorkspaceId_ProjectId",
                        columns: x => new { x.WorkspaceId, x.ProjectId },
                        principalTable: "Projects",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    InboxOwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    DueKind = table.Column<int>(type: "integer", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DueLocalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    DueZone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DueAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                    table.UniqueConstraint("AK_Tasks_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_Task_Container", "(\"ProjectId\" IS NULL) <> (\"InboxOwnerId\" IS NULL)");
                    table.CheckConstraint("CK_Task_Due", "(\"DueKind\" = 0 AND \"DueDate\" IS NULL AND \"DueLocalTime\" IS NULL AND \"DueZone\" IS NULL AND \"DueAtUtc\" IS NULL) OR (\"DueKind\" = 1 AND \"DueDate\" IS NOT NULL AND \"DueLocalTime\" IS NULL AND \"DueZone\" IS NULL AND \"DueAtUtc\" IS NULL) OR (\"DueKind\" = 2 AND \"DueDate\" IS NOT NULL AND \"DueLocalTime\" IS NOT NULL AND \"DueZone\" IS NOT NULL AND \"DueAtUtc\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Tasks_Memberships_WorkspaceId_CreatorId",
                        columns: x => new { x.WorkspaceId, x.CreatorId },
                        principalTable: "Memberships",
                        principalColumns: new[] { "WorkspaceId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tasks_Memberships_WorkspaceId_InboxOwnerId",
                        columns: x => new { x.WorkspaceId, x.InboxOwnerId },
                        principalTable: "Memberships",
                        principalColumns: new[] { "WorkspaceId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tasks_Projects_WorkspaceId_ProjectId",
                        columns: x => new { x.WorkspaceId, x.ProjectId },
                        principalTable: "Projects",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_WorkspaceId_CreatorId",
                table: "Projects",
                columns: new[] { "WorkspaceId", "CreatorId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectShares_WorkspaceId_UserId",
                table: "ProjectShares",
                columns: new[] { "WorkspaceId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_WorkspaceId_CreatorId",
                table: "Tasks",
                columns: new[] { "WorkspaceId", "CreatorId" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_WorkspaceId_InboxOwnerId_Id",
                table: "Tasks",
                columns: new[] { "WorkspaceId", "InboxOwnerId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_WorkspaceId_ProjectId_Id",
                table: "Tasks",
                columns: new[] { "WorkspaceId", "ProjectId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectShares");

            migrationBuilder.DropTable(
                name: "Tasks");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "Memberships");

            migrationBuilder.DropTable(
                name: "Workspaces");
        }
    }
}
