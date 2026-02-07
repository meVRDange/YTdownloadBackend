using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Logging;

namespace YTdownloadBackend.Services;

public interface IFcmService
{
    /// <summary>
    /// Send a data-only FCM message to a specific device token.
    /// Returns the message id on success, null on failure.
    /// </summary>
    Task<string?> SendDownloadNotificationAsync(string deviceToken, IReadOnlyDictionary<string, string> data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a download completed notification.
    /// </summary>
    Task<string?> SendDownloadCompletedNotificationAsync(string deviceToken, string songTitle, string downloadUrl, CancellationToken cancellationToken = default);
}

public class FcmService : IFcmService
{
    private readonly ILogger<FcmService> _logger;

    public FcmService(ILogger<FcmService> logger)
    {
        _logger = logger;
    }

    public async Task<string?> SendDownloadNotificationAsync(string deviceToken, IReadOnlyDictionary<string, string> data, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceToken))
            throw new ArgumentException("deviceToken is required", nameof(deviceToken));
        if (data is null || data.Count == 0)
            throw new ArgumentException("data payload is required", nameof(data));

        try
        {
            var message = new Message
            {
                Token = deviceToken,
                // Data-only payload — the client background handler will process this
                //Notification = new Notification
                //{
                //    Title = "Your song is ready",
                //    Body = $"is available"
                //},
                Data = data,
                Android = new AndroidConfig
                {
                    Priority = Priority.High,
                    // optional: set TTL, collapse key, etc.
                    TimeToLive = TimeSpan.FromDays(1)
                },
                Apns = new ApnsConfig
                {
                    // For iOS silent pushes, indicate content-available
                    Aps = new Aps
                    {
                        ContentAvailable = true
                    }
                }
            };

            string response = await FirebaseMessaging.DefaultInstance.SendAsync(message, cancellationToken);
            _logger.LogInformation("FCM message sent. MessageId={MessageId} DeviceToken={DeviceToken}", response, deviceToken);
            return response;
        }
        catch (FirebaseMessagingException ex)
        {
            _logger.LogError(ex, "FCM send failed (FirebaseMessagingException). ErrorCode={ErrorCode} DeviceToken={DeviceToken}", ex.ErrorCode, deviceToken);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FCM send failed (Exception). DeviceToken={DeviceToken}", deviceToken);
            return null;
        }
    }

    public async Task<string?> SendDownloadCompletedNotificationAsync(string deviceToken, string songTitle, string downloadUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceToken))
            throw new ArgumentException("deviceToken is required", nameof(deviceToken));
        if (string.IsNullOrWhiteSpace(downloadUrl))
            throw new ArgumentException("downloadUrl is required", nameof(downloadUrl));

        var data = new Dictionary<string, string>
        {
            { "type", "DOWNLOAD_COMPLETED" },
            { "songTitle", songTitle },
            { "downloadUrl", downloadUrl },
            { "timestamp", DateTime.UtcNow.ToString("O") }
        };

        return await SendDownloadNotificationAsync(deviceToken, data, cancellationToken);
    }
}