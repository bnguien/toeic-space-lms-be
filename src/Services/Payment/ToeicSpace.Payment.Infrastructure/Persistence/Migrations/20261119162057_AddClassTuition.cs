using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToeicSpace.Payment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClassTuition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TuitionOrderId",
                table: "PaymentTransactions",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateTable(
                name: "TuitionOrders",
                columns: table => new
                {
                    PaymentOrderId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClassId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StudentId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClassEnrollmentId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    AmountOwed = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    AmountPaid = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    AmountWaived = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    PaymentReference = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TuitionOrders", x => x.PaymentOrderId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TuitionEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PaymentOrderId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Kind = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    RecordedBy = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    SePayId = table.Column<long>(type: "bigint", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TuitionEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TuitionEntries_TuitionOrders_PaymentOrderId",
                        column: x => x.PaymentOrderId,
                        principalTable: "TuitionOrders",
                        principalColumn: "PaymentOrderId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_TuitionOrderId",
                table: "PaymentTransactions",
                column: "TuitionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_TuitionEntries_PaymentOrderId",
                table: "TuitionEntries",
                column: "PaymentOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_TuitionEntries_SePayId",
                table: "TuitionEntries",
                column: "SePayId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TuitionOrders_ClassId_StudentId",
                table: "TuitionOrders",
                columns: new[] { "ClassId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_TuitionOrders_PaymentReference",
                table: "TuitionOrders",
                column: "PaymentReference",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentTransactions_TuitionOrders_TuitionOrderId",
                table: "PaymentTransactions",
                column: "TuitionOrderId",
                principalTable: "TuitionOrders",
                principalColumn: "PaymentOrderId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentTransactions_TuitionOrders_TuitionOrderId",
                table: "PaymentTransactions");

            migrationBuilder.DropTable(
                name: "TuitionEntries");

            migrationBuilder.DropTable(
                name: "TuitionOrders");

            migrationBuilder.DropIndex(
                name: "IX_PaymentTransactions_TuitionOrderId",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "TuitionOrderId",
                table: "PaymentTransactions");
        }
    }
}
