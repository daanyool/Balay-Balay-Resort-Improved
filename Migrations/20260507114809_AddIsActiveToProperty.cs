using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Balay_Balay_Resort.Migrations
{
    /// <inheritdoc />
    public partial class AddIsActiveToProperty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Properties",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Properties");
        }
    }
}
