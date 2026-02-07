using YTdownloadBackend.Data;
using YTdownloadBackend.Models;

namespace YTdownloadBackend.Services
{
    public interface ISongUploadService
    {
        /// <summary>
        /// Orchestrates: Upload to Firebase → Generate download URL → Send FCM → Clean local file
        /// </summary>
        Task<bool> ProcessDownloadedSongAsync(PlaylistSong song, string localFilePath, string username, User user);
    }

    public class SongUploadService : ISongUploadService
    {
        private readonly IFirebaseStorageService _storageService;
        private readonly IFirebaseUrlService _urlService;
        private readonly IFcmService _fcmService;
        private readonly AppDbContext _dbContext;
        private readonly ILogger<SongUploadService> _logger;

        public SongUploadService(
            IFirebaseStorageService storageService,
            IFirebaseUrlService urlService,
            IFcmService fcmService,
            AppDbContext dbContext,
            ILogger<SongUploadService> logger)
        {
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _urlService = urlService ?? throw new ArgumentNullException(nameof(urlService));
            _fcmService = fcmService ?? throw new ArgumentNullException(nameof(fcmService));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> ProcessDownloadedSongAsync(PlaylistSong song, string localFilePath, string username, User user)
        {
            if (song is null)
                throw new ArgumentNullException(nameof(song));
            if (string.IsNullOrEmpty(localFilePath))
                throw new ArgumentNullException(nameof(localFilePath));
            if (user is null)
                throw new ArgumentNullException(nameof(user));

            try
            {
                // STEP 1: Verify local file exists
                if (!File.Exists(localFilePath))
                {
                    _logger.LogError("Local file not found for song {SongId}: {FilePath}", song.Id, localFilePath);
                    return false;
                }

                // STEP 2: Generate Firebase Storage path
                var fileName = Path.GetFileName(localFilePath);
                var firebaseStoragePath = $"users/{user.Id}/songs/{fileName}";

                _logger.LogInformation("Starting Firebase Storage upload for song {SongId}: {LocalPath} -> {StoragePath}",
                    song.Id, localFilePath, firebaseStoragePath);

                var uploadedPath = string.Empty;
                
                // STEP 3: Check if file already exists in Firebase Storage
                var fileExists = await _storageService.FileExistsAsync(firebaseStoragePath);
                if (fileExists)
                {
                    _logger.LogInformation("File already exists in Firebase Storage: {StoragePath}, skipping upload", firebaseStoragePath);
                    uploadedPath = firebaseStoragePath;
                }
                else
                {
                    // STEP 3: Upload to Firebase Storage
                    try
                    {
                        uploadedPath = await _storageService.UploadFileAsync(localFilePath, firebaseStoragePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Firebase Storage upload failed for song {SongId}", ex.Message);
                        throw ex;
                    }
                }
                if (string.IsNullOrEmpty(uploadedPath))
                {
                    _logger.LogError("Firebase Storage upload returned null path for song {SongId}", song.Id);
                    return false;
                }
                song.FirebaseStoragePath = uploadedPath;
                song.DownloadUrlExpiry = DateTime.UtcNow.AddHours(48);

                // STEP 4: Generate download URL (valid for 48 hours)
                var downloadUrl = await _urlService.GetOrGenerateDownloadUrlAsync(song, TimeSpan.FromHours(48));
                if (downloadUrl is null)
                {
                    _logger.LogError("Failed to generate download URL for song {SongId}", song.Id);
                    return false;
                }

                // STEP 5: Update song record with Firebase Storage info
                song.FirebaseStoragePath = uploadedPath;
                song.FirebaseDownloadUrl = downloadUrl;
                song.DownloadUrlExpiry = DateTime.UtcNow.AddHours(48);
                song.Status = PlaylistSongStatus.Completed;
                song.DownloadedAt = DateTime.UtcNow;

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Song {SongId} uploaded to Firebase Storage and database updated", song.Id);

                // STEP 6: Send FCM notification with download link (if user has FCM token)
                if (!string.IsNullOrEmpty(user.FCMToken))
                {
                    try
                    {
                        var messageId = await _fcmService.SendDownloadCompletedNotificationAsync(
                            user.FCMToken,
                            song.Title,
                            downloadUrl
                        );

                        if (messageId is not null)
                        {
                            _logger.LogInformation("FCM notification sent for song {SongId}, MessageId={MessageId}",
                                song.Id, messageId);
                        }
                        else
                        {
                            _logger.LogWarning("FCM notification failed for song {SongId}", song.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception while sending FCM notification for song {SongId}", song.Id);
                    }
                }
                else
                {
                    _logger.LogWarning("User {UserId} has no FCM token; skipping notification for song {SongId}",
                        user.Id, song.Id);
                }

                // STEP 7: Delete local file to free up disk space
                try
                {
                    File.Delete(localFilePath);
                    _logger.LogInformation("Local file deleted: {FilePath}", localFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete local file {FilePath}: {Message}", localFilePath, ex.Message);
                    // Don't fail the entire operation if cleanup fails
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in ProcessDownloadedSongAsync for song {SongId}: {Message}",
                    song.Id, ex.Message);
                return false;
            }
        }
    }
}