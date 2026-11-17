using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// The project an attachment belongs to, as a nullable FK a deleted
    /// project takes the attachment with - see
    /// <see cref="Hatch.Api.Modules.Hatch.EfHatchAttachment.ProjectId"/>.
    /// Every existing attachment reads blank until something sets one.
    /// </summary>
    public partial class AddAttachmentProject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProjectId",
                schema: "hatch",
                table: "Attachments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_ProjectId",
                schema: "hatch",
                table: "Attachments",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Attachments_Projects_ProjectId",
                schema: "hatch",
                table: "Attachments",
                column: "ProjectId",
                principalSchema: "hatch",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attachments_Projects_ProjectId",
                schema: "hatch",
                table: "Attachments");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_ProjectId",
                schema: "hatch",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                schema: "hatch",
                table: "Attachments");
        }
    }
}
