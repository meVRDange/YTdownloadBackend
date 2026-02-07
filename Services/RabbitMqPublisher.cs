using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RabbitMQ.Client;

namespace YTdownloadBackend.Services
{
    // Restore VideoId — consumer/hosted service expects it.
    public record DownloadJob(int SongId, string VideoId, string Username);

    public interface IRabbitMqPublisher
    {
        Task EnqueueDownloadAsync(DownloadJob job);
    }

    public class RabbitMqPublisher : IRabbitMqPublisher, IDisposable
    {
        private readonly IConnection _connection;
        private readonly string _queueName = "yt_download_jobs";

        public RabbitMqPublisher(IConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        public Task EnqueueDownloadAsync(DownloadJob job)
        {
            if (job is null) throw new ArgumentNullException(nameof(job));

            try
            {
                // Use the async channel creation method from IConnection
                // (Assuming you have access to the RabbitMQ.Client library's IModel/IChannel)
                // If you need to use the synchronous API, you must use the concrete Connection type, not IConnection.
                // Here, we use CreateChannelAsync and await it.
                return EnqueueDownloadInternalAsync(job);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to enqueue download job to RabbitMQ.", ex);
            }
        }

        private async Task EnqueueDownloadInternalAsync(DownloadJob job)
        {
            // Create a channel using the async API
            var channel = await _connection.CreateChannelAsync();

            try
            {
                // Use the async QueueDeclareAsync method instead of QueueDeclare
                await channel.QueueDeclareAsync(queue: _queueName,
                                               durable: true,
                                               exclusive: false,
                                               autoDelete: false,
                                               arguments: null);

                var json = JsonSerializer.Serialize(job);
                var body = Encoding.UTF8.GetBytes(json);

                var props = new BasicProperties
                {
                    Persistent = true,
                    ContentType = "application/json",
                    MessageId = Guid.NewGuid().ToString(),
                    Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                };

                // ConfirmSelect and WaitForConfirms are not available on IChannel.
                // You may need to remove these or use publisher confirms via events if needed.

                // Use the async publish method
                await channel.BasicPublishAsync(
                    exchange: "",
                    routingKey: _queueName,
                    mandatory: false,
                    basicProperties: props,
                    body: body
                );
            }
            finally
            {
                await channel.DisposeAsync();
            }
        }

        public void Dispose()
        {
            // Connection lifetime is managed by DI in Program.cs. Do not close it here.
        }
    }
}