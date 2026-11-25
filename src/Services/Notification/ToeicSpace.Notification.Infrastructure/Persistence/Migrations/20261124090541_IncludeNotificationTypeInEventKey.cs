using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToeicSpace.Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IncludeNotificationTypeInEventKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE `Notifications`
                    DROP INDEX IX_Notifications_Source_SourceEventId_RecipientId,
                    ADD UNIQUE INDEX IX_Notifications_Source_SourceEventId_RecipientId_Type
                        (`Source`, `SourceEventId`, `RecipientId`, `Type`);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE `Notifications`
                    DROP INDEX IX_Notifications_Source_SourceEventId_RecipientId_Type,
                    ADD UNIQUE INDEX IX_Notifications_Source_SourceEventId_RecipientId
                        (`Source`, `SourceEventId`, `RecipientId`);
                """);
        }
    }
}
