using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Claims.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClaimProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Providers",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CoverageEnd",
                table: "Members",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CoverageStart",
                table: "Members",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(2020, 1, 1));

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Members",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "DenialCode",
                table: "Claims",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DenialMessage",
                table: "Claims",
                type: "nvarchar(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DuplicateClaimId",
                table: "Claims",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessedAt",
                table: "Claims",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ValidatedAt",
                table: "Claims",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Members_Coverage",
                table: "Members",
                sql: "[CoverageEnd] IS NULL OR [CoverageEnd] >= [CoverageStart]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Members_Coverage",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Providers");

            migrationBuilder.DropColumn(
                name: "CoverageEnd",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "CoverageStart",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "DenialCode",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "DenialMessage",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "DuplicateClaimId",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "ProcessedAt",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "ValidatedAt",
                table: "Claims");
        }
    }
}
