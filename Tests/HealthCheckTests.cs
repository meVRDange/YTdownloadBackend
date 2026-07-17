using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
// Removed Moq usage; using a simple fake implementation instead.
using Xunit;
using YTdownloadBackend;
using YTdownloadBackend.Services; // assuming IFcmService is in Services namespace
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading;

namespace YTdownloadBackend.Tests
{
    public class HealthCheckTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly string _jwtToken;

        public HealthCheckTests(WebApplicationFactory<Program> factory)
        {
            // Set required environment variables for the test host
            // Use a 32‑character secret (256‑bit) to satisfy HS256 requirements.
            Environment.SetEnvironmentVariable("JWT_SECRET_KEY", "test-secret-key-1234567890abcdef");
            Environment.SetEnvironmentVariable("FIREBASE_FCM_TOKEN", "dummy-token");

            // Create a simple fake IFcmService implementation to avoid real Firebase calls
            var fakeFcmService = new FakeFcmService();

            // Create a factory that replaces the IFcmService registration with the fake implementation
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Remove existing registration if present
                    var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IFcmService));
                    if (descriptor != null) services.Remove(descriptor);
                    // Register fake implementation
                    services.AddSingleton<IFcmService>(fakeFcmService);
                });
            });

            // Generate a JWT token using the same secret key
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-secret-key-1234567890abcdef"));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var tokenHandler = new JwtSecurityTokenHandler();
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "testuser") }),
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = creds
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            _jwtToken = tokenHandler.WriteToken(token);
        }

        [Fact]
        public async Task HealthCheck_ReturnsOk_WithMessageId()
        {
            // Arrange
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _jwtToken);

            // Act
            var response = await client.GetAsync("/api/healthCheck");

            // Assert
            response.EnsureSuccessStatusCode(); // Status Code 200-299
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("Authenticated", content);
            Assert.Contains("mockMessageId", content);
        }
    }

    // Simple fake implementation of IFcmService used for testing.
    public class FakeFcmService : IFcmService
    {
        public Task<string?> SendDownloadNotificationAsync(string deviceToken, IReadOnlyDictionary<string, string> data, CancellationToken cancellationToken = default)
        {
            // Return a deterministic mock message ID.
            return Task.FromResult<string?>("mockMessageId");
        }

        public Task<string?> SendDownloadCompletedNotificationAsync(string deviceToken, string songTitle, string downloadUrl, CancellationToken cancellationToken = default)
        {
            // For the purposes of this test, behave the same as SendDownloadNotificationAsync.
            return Task.FromResult<string?>("mockMessageId");
        }
    }
}
