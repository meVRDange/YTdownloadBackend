using Microsoft.EntityFrameworkCore;
using YTdownloadBackend.Models;
using YTdownloadBackend.Models.YTdownloadBackend.Models; // adjust namespace if your Models folder is named differently

namespace YTdownloadBackend.Data
{
    // DbContext = EF Core’s gateway to the database
    public class AppDbContext : DbContext
    {
        // constructor that passes options to the base class
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // This will create a table named "LoginRequests" by default
        public DbSet<User> Users => Set<User>();
        public DbSet<Playlist> Playlists => Set<Playlist>();

        public DbSet<PlaylistSong> PlaylistSongs => Set<PlaylistSong>();
        public DbSet<DownloadTask> DownloadTasks => Set<DownloadTask>();

    }
}
