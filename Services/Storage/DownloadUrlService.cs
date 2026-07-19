using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YTdownloadBackend.Data;
using YTdownloadBackend.Models;

namespace YTdownloadBackend.Services.Storage
{
    public interface IDownloadUrlService
    {
        /// <summary>
        /// Gets a cached download URL if still fresh, or generates a new one via the active storage provider.
        /// </summary>
        Task<string?> GetOrGenerateDownloadUrlAsync(PlaylistSong song, TimeSpan? duration = null);
    }

    /// <summary>
    /// Provider-agnostic download URL service. Resolves the active <see cref="IStorageProvider"/>
    /// at runtime via <see cref="IStorageProviderFactory"/> so it works with any storage backend.
    /// </summary>
    public class DownloadUrlService : IDownloadUrlService
    {
        private readonly IStorageProviderFactory _factory;
        private readonly AppDbContext _dbContext;
        private readonly ILogger<DownloadUrlService> _logger;

        public DownloadUrlService(
            IStorageProviderFactory factory,
            AppDbContext dbContext,
            ILogger<DownloadUrlService> logger)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string?> GetOrGenerateDownloadUrlAsync(PlaylistSong song, TimeSpan? duration = null)
        {
            if (song is null)
                throw new ArgumentNullException(nameof(song));

            duration ??= TimeSpan.FromHours(48);

            // Return cached URL if still fresh (>5 min before expiry)
            if (!string.IsNullOrEmpty(song.DownloadUrl) &&
                song.DownloadUrlExpiry > DateTime.UtcNow.AddMinutes(5))
            {
                _logger.LogInformation("Returning existing download URL for song {SongId}", song.Id);
                return song.DownloadUrl;
            }

            if (string.IsNullOrEmpty(song.StoragePath))
            {
                _logger.LogWarning("Song {SongId} has no StoragePath set", song.Id);
                return null;
            }

            try
            {
                var provider = _factory.GetActiveProvider();
                var downloadUrl = await provider.GetDownloadUrlAsync(song.StoragePath, duration);

                if (downloadUrl is null)
                {
                    _logger.LogError("Failed to generate download URL for song {SongId} via provider {Provider}",
                        song.Id, provider.Name);
                    return null;
                }

                song.DownloadUrl = downloadUrl;
                song.DownloadUrlExpiry = DateTime.UtcNow.Add(duration.Value);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Generated new download URL for song {SongId} via {Provider}, valid until {Expiry}",
                    song.Id, provider.Name, song.DownloadUrlExpiry);

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
