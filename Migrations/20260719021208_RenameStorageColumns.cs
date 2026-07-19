using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YTdownloadBackend.Migrations
{
    /// <inheritdoc />
    public partial class RenameStorageColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FirebaseStoragePath",
                table: "PlaylistSongs",
                newName: "StoragePath");

            migrationBuilder.RenameColumn(
                name: "FirebaseDownloadUrl",
                table: "PlaylistSongs",
                newName: "DownloadUrl");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "StoragePath",
                table: "PlaylistSongs",
                newName: "FirebaseStoragePath");

            migrationBuilder.RenameColumn(
                name: "DownloadUrl",
                table: "PlaylistSongs",
                newName: "FirebaseDownloadUrl");
        }
    }
}
