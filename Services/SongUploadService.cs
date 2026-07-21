using YTdownloadBackend.Data;
using YTdownloadBackend.Models;
using YTdownloadBackend.Services.Storage;

namespace YTdownloadBackend.Services
{
    /// <summary>
    /// The post-download pipeline: upload to cloud storage → generate signed download URL →
    /// persist to DB → send FCM notification → clean local file.
    /// </summary>
    public interface ISongPipeline
    {
        Task<bool> ProcessAsync(PlaylistSong song, string localFilePath, string username, User user);
    }

    public class SongPipeline : ISongPipeline
    {
        private readonly IStorageProviderFactory _storageFactory;
        private readonly IFcmService _fcmService;
        private readonly AppDbContext _dbContext;
        private readonly ILogger<SongPipeline> _logger;

        public SongPipeline(
            IStorageProviderFactory storageFactory,
            IFcmService fcmService,
            AppDbContext dbContext,
            ILogger<SongPipeline> logger)
        {
            _storageFactory = storageFactory ?? throw new ArgumentNullException(nameof(storageFactory));
            _fcmService = fcmService ?? throw new ArgumentNullException(nameof(fcmService));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> ProcessAsync(PlaylistSong song, string localFilePath, string username, User user)
        {
            if (song is null) throw new ArgumentNullException(nameof(song));
            if (string.IsNullOrEmpty(localFilePath)) throw new ArgumentNullException(nameof(localFilePath));
            if (user is null) throw new ArgumentNullException(nameof(user));

            try
            {
                // ── 1. Verify local file ──────────────────────
                if (!File.Exists(localFilePath))
                {
                    _logger.LogError("Local file not found for song {SongId}: {FilePath}", song.Id, localFilePath);
                    return false;
                }

                // ── 2. Upload to storage ──────────────────────
                var fileName = Path.GetFileName(localFilePath);
                var storagePath = $"{username}/songs/{fileName}";

                _logger.LogInformation("Uploading song {SongId}: {LocalPath} -> {StoragePath}",
                    song.Id, localFilePath, storagePath);

                var storageProvider = _storageFactory.GetActiveProvider();

                var fileExists = await storageProvider.FileExistsAsync(storagePath);
                var uploadedPath = fileExists
                    ? storagePath
                    : await storageProvider.UploadFileAsync(localFilePath, storagePath);

                if (string.IsNullOrEmpty(uploadedPath))
                {
                    _logger.LogError("Storage upload returned null path for song {SongId}", song.Id);
                    return false;
                }
                song.StoragePath = uploadedPath;

                // ── 3. Generate signed download URL ──────────
                var downloadUrl = await GetOrGenerateDownloadUrlAsync(song);
                if (downloadUrl is null)
                {
                    _logger.LogError("Failed to generate download URL for song {SongId}", song.Id);
                    return false;
                }

                // ── 4. Persist to DB ─────────────────────────
                song.DownloadUrl = downloadUrl;
                song.Status = PlaylistSongStatus.Completed;
                song.DownloadedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Song {SongId} uploaded and saved", song.Id);

                // ── 5. Send FCM notification ──────────────────
                if (!string.IsNullOrEmpty(user.FCMToken))
                {
                    try
                    {
                        var messageId = await _fcmService.SendDownloadCompletedNotificationAsync(
                            user.FCMToken, song.Title, downloadUrl);

                        _logger.LogInformation(
                            messageId is not null
                                ? "FCM sent for song {SongId}, MessageId={MessageId}"
                                : "FCM failed for song {SongId}",
                            song.Id, messageId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "FCM exception for song {SongId}", song.Id);
                    }
                }
                else
                {
                    _logger.LogWarning("User {UserId} has no FCM token; skipping notification", user.Id);
                }

                // ── 6. Delete local file (skip for local storage) ──
                if (storageProvider.Name != "Local")
                {
                    try
                    {
                        File.Delete(localFilePath);
                        _logger.LogInformation("Local file deleted: {FilePath}", localFilePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete local file {FilePath}", localFilePath);
                    }
                }
                else
                {
                    _logger.LogInformation("Local storage — keeping file: {FilePath}", localFilePath);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pipeline failed for song {SongId}: {Message}", song.Id, ex.Message);
                return false;
            }
        }

        private async Task<string?> GetOrGenerateDownloadUrlAsync(PlaylistSong song)
        {
            var duration = TimeSpan.FromHours(48);

            // Return cached URL if still fresh (>5 min before expiry)
            if (!string.IsNullOrEmpty(song.DownloadUrl) &&
                song.DownloadUrlExpiry > DateTime.UtcNow.AddMinutes(5))
            {
                _logger.LogInformation("Returning cached download URL for song {SongId}", song.Id);
                return song.DownloadUrl;
            }

            if (string.IsNullOrEmpty(song.StoragePath))
            {
                _logger.LogWarning("Song {SongId} has no StoragePath set", song.Id);
                return null;
            }

            var provider = _storageFactory.GetActiveProvider();
            var url = await provider.GetDownloadUrlAsync(song.StoragePath, duration);

            if (url is null) return null;

            song.DownloadUrl = url;
            song.DownloadUrlExpiry = DateTime.UtcNow.Add(duration);

            _logger.LogInformation("Generated download URL for song {SongId} via {Provider}, valid until {Expiry}",
                song.Id, provider.Name, song.DownloadUrlExpiry);

            return url;
        }
    }
}