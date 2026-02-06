using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentModule.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDetailsToStoredCard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "StoredCards",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "StoredCards",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "StoredCards",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "StoredCards",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "StoredCards",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "StoredCards",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "StoredCards",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                table: "StoredCards");

            migrationBuilder.DropColumn(
                name: "City",
                table: "StoredCards");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "StoredCards");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "StoredCards");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "StoredCards");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "StoredCards");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "StoredCards");
        }
    }
}
