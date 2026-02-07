using Microsoft.EntityFrameworkCore;
using YTdownloadBackend.Data;
using YTdownloadBackend.Models;

namespace YTdownloadBackend.Services
{
    // A manually-started queue processor. It polls the DownloadTasks table and processes
    // pending tasks until there are none left. It is intentionally NOT registered as
    // a hosted service — callers must start it with StartIfNotRunningAsync.
    public class DownloadQueueService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<DownloadQueueService> _logger;
        private readonly TimeSpan _pollDelay = TimeSpan.FromSeconds(1);

        private readonly object _startLock = new();
        private Task? _workerTask;
        private bool _running;

        public DownloadQueueService(IServiceProvider services, ILogger<DownloadQueueService> logger)
        {
            _services = services;
            _logger = logger;
        }

        public bool IsRunning
        {
            get
            {
                lock (_startLock)
                {
                    return _running;
                }
            }
        }

        // Start the background loop if it's not already running. This method returns quickly.
        // callerSongId: optional song id that triggered the start. We ensure there's a DownloadTask
        // for that song so the queue processor has work to do.
        public void StartIfNotRunning(String username)
        {
            lock (_startLock)
            {
                if (_running)
                {
                    _logger.LogDebug("DownloadQueueService already running — start request ignored.");
                    return;
                }

                _running = true;
                _workerTask = Task.Run(() => WorkerLoopAsync(username));
            }
        }

        /// <summary>
        /// Sanitizes a filename by replacing characters that are not alphanumeric, dot (.), or hyphen (-) with underscores.
        /// This approximates the behavior of yt-dlp's --restrict-filenames option to ensure safe file naming.
        /// </summary>
        /// <param name="filename">The original filename string to sanitize.</param>
        /// <returns>A sanitized filename string with invalid characters replaced by underscores.</returns>

        private async Task WorkerLoopAsync(string username)
        {
            _logger.LogInformation("DownloadQueueService starting");

            try
            {
                // Process tasks until none pending
                while (true)
                {             
                    try
                    {
                        using var scope = _services.CreateScope();
                        RepositoryService repositoryService = scope.ServiceProvider.GetRequiredService<RepositoryService>();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var downloader = scope.ServiceProvider.GetRequiredService<IYtDlpService>();
                        var scopedLogger = scope.ServiceProvider.GetRequiredService<ILogger<DownloadQueueService>>();
                        var uploadService = scope.ServiceProvider.GetRequiredService<ISongUploadService>();

                        List<PlaylistSong>? taskItemList = await repositoryService.getPlaylistPendingSongs();

                        if (taskItemList.Count == 0)
                        {
                            // no more pending tasks -> stop the worker
                            _logger.LogInformation("No pending download tasks found. DownloadQueueService stopping.");
                            break;
                        }

                        foreach (var taskItem in taskItemList)
                        {
                            
                            repositoryService.UpdatePlaylistSongStatus(taskItem, PlaylistSongStatus.Processing);

                            scopedLogger.LogInformation($"Processing download task: VideoTitle={taskItem.Title}, VideoId={taskItem.VideoId}");

                            string sanitizedTitle= "";
                            try
                            {
                                sanitizedTitle = await downloader.DownloadAudioAsync(taskItem.VideoId, username) ?? string.Empty;
                            }
                            catch (Exception ex)
                            {
                                scopedLogger.LogError(ex, "Downloader threw for VideoId={VideoId}", taskItem.VideoId);
                                //taskItem.LastError = ex.Message;
                            }

                            var expectedPath = Path.Combine(Directory.GetCurrentDirectory(), "downloads", $"{username}", $"{sanitizedTitle}");

                            if ( File.Exists(expectedPath))
                            {
                                // Download successful, now upload to Firebase
                                scopedLogger.LogInformation("Download completed for VideoId={VideoId}, attempting Firebase upload", taskItem.VideoId);
                                
                                try
                                {
                                    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
                                    if (user != null)
                                    {
                                        bool uploadSuccess = await uploadService.ProcessDownloadedSongAsync(taskItem, expectedPath, username, user);
                                        if (uploadSuccess)
                                        {
                                            taskItem.Status = PlaylistSongStatus.Completed;
                                            scopedLogger.LogInformation("Song {SongId} successfully uploaded to Firebase", taskItem.Id);
                                        }
                                        else
                                        {
                                            taskItem.RetryCount = Math.Min(taskItem.RetryCount + 1, int.MaxValue);
                                            if (taskItem.RetryCount >= 3)
                                            {
                                                taskItem.Status = PlaylistSongStatus.Failed;
                                                scopedLogger.LogWarning("Firebase upload permanently failed for SongId={SongId} after {Retries} attempts", taskItem.Id, taskItem.RetryCount);
                                            }
                                            else
                                            {
                                                taskItem.Status = PlaylistSongStatus.Pending; // requeue
                                                scopedLogger.LogWarning("Firebase upload failed for SongId={SongId}. Will retry (attempt {Attempt}).", taskItem.Id, taskItem.RetryCount);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        scopedLogger.LogError("User {Username} not found. Cannot upload to Firebase for SongId={SongId}", username, taskItem.Id);
                                        taskItem.Status = PlaylistSongStatus.Failed;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    scopedLogger.LogError(ex, "Exception during Firebase upload for SongId={SongId}", taskItem.Id);
                                    taskItem.RetryCount = Math.Min(taskItem.RetryCount + 1, int.MaxValue);
                                    if (taskItem.RetryCount >= 3)
                                    {
                                        taskItem.Status = PlaylistSongStatus.Failed;
                                    }
                                    else
                                    {
                                        taskItem.Status = PlaylistSongStatus.Pending;
                                    }
                                }

                                taskItem.LastChecked = DateTime.UtcNow;
                                await db.SaveChangesAsync();
                            }
                            else
                            {
                                taskItem.RetryCount = Math.Min(taskItem.RetryCount + 1, int.MaxValue);
                                taskItem.LastChecked = DateTime.UtcNow;

                                if (taskItem.RetryCount >= 3)
                                {
                                    taskItem.Status = PlaylistSongStatus.Failed;
                                    await db.SaveChangesAsync();
                                    scopedLogger.LogWarning("Download permanently failed for VideoId={VideoId} after {Retries} attempts", taskItem.VideoId, taskItem.RetryCount);
                                }
                                else
                                {
                                    taskItem.Status = PlaylistSongStatus.Pending; // requeue
                                    await db.SaveChangesAsync();
                                    scopedLogger.LogWarning("Download failed for VideoId={VideoId}. Will retry (attempt {Attempt}).", taskItem.VideoId, taskItem.RetryCount);
                                }
                            }
                        }
                        
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unhandled error in DownloadQueueService loop. Sleeping briefly before retry.");
                        try { await Task.Delay(TimeSpan.FromSeconds(5)); } catch { /* ignore */ }
                    }

                    // small delay between iterations to avoid tight loop
                    try { await Task.Delay(_pollDelay); } catch { }
                }
            }
            finally
            {
                lock (_startLock)
                {
                    _running = false;
                    _workerTask = null;
                }

                _logger.LogInformation("DownloadQueueService stopped.");
            }
        }
    }
}
