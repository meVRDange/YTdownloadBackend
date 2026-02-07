using Microsoft.EntityFrameworkCore;
using YTdownloadBackend.Data;
using YTdownloadBackend.Models;

namespace YTdownloadBackend.Services
{
    public interface IFirebaseUrlService
    {
        /// <summary>
        /// Gets or generates a Firebase Storage download URL.
        /// </summary>
        Task<string?> GetOrGenerateDownloadUrlAsync(PlaylistSong song, TimeSpan? duration = null);
    }

    public class FirebaseUrlService : IFirebaseUrlService
    {
        private readonly IFirebaseStorageService _storageService;
        private readonly AppDbContext _dbContext;
        private readonly ILogger<FirebaseUrlService> _logger;

        public FirebaseUrlService(IFirebaseStorageService storageService, AppDbContext dbContext, ILogger<FirebaseUrlService> logger)
        {
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string?> GetOrGenerateDownloadUrlAsync(PlaylistSong song, TimeSpan? duration = null)
        {
            if (song is null)
                throw new ArgumentNullException(nameof(song));

            // Default to 48 hours if not specified
            duration ??= TimeSpan.FromHours(48);

            // If URL exists and is still "fresh" (within 5 min of expiry), return it
            if (!string.IsNullOrEmpty(song.FirebaseDownloadUrl) &&
                song.DownloadUrlExpiry > DateTime.UtcNow.AddMinutes(5))
            {
                _logger.LogInformation("Returning existing Firebase download URL for song {SongId}", song.Id);
                return song.FirebaseDownloadUrl;
            }

            // If no Firebase storage path, URL can't be generated
            if (string.IsNullOrEmpty(song.FirebaseStoragePath))
            {
                _logger.LogWarning("Song {SongId} has no FirebaseStoragePath set", song.Id);
                return null;
            }

            // Generate new download URL
            try
            {
                var downloadUrl = await _storageService.GetDownloadUrlAsync(song.FirebaseStoragePath, duration);

                if (downloadUrl is null)
                {
                    _logger.LogError("Failed to generate download URL for song {SongId}", song.Id);
                    return null;
                }

                // Update database
                song.FirebaseDownloadUrl = downloadUrl;
                song.DownloadUrlExpiry = DateTime.UtcNow.Add(duration.Value);

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Generated new Firebase download URL for song {SongId}, valid until {Expiry}",
                    song.Id, song.DownloadUrlExpiry);

                return downloadUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate download URL for song {SongId}", song.Id);
                return null;
            }
        }
    }
}