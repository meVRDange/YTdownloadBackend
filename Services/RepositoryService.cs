using Microsoft.EntityFrameworkCore;
using YTdownloadBackend.Data;
using YTdownloadBackend.Models;
using System.Threading.Tasks; // Ensure Task is available

namespace YTdownloadBackend.Services
{
    public class RepositoryService
    {
        AppDbContext _appDbContext;
        public RepositoryService(AppDbContext appDbContext) { 
            _appDbContext = appDbContext;
        }

        public async Task<List<PlaylistSong>> getPlaylistPendingSongs()
        {
            List<PlaylistSong> taskItemList = await _appDbContext.PlaylistSongs
                .Where(t => t.Status == PlaylistSongStatus.Pending && t.RetryCount <= 3)
                .ToListAsync();

            return taskItemList;
        }

        public async Task UpdatePlaylistSongStatus(PlaylistSong song, PlaylistSongStatus status)
        {
            song.Status = status;
            _appDbContext.PlaylistSongs.Update(song);
            song.LastChecked = DateTime.UtcNow;
            await _appDbContext.SaveChangesAsync();
        }
    }
}
