using Microsoft.EntityFrameworkCore;
using YTdownloadBackend.Data;
using YTdownloadBackend.Models;

namespace YTdownloadBackend.Services
{
    public interface IAuthorizationService
    {
        /// <summary>
        /// Common authorization check for endpoints that require user authentication.
        /// Validates the Authorization header, retrieves the user from the database, and returns the User object.
        /// Throws UnauthorizedAccessException if validation fails at any step.
        /// </summary>
        Task<User> CommonAuthCheck(HttpContext http, AppDbContext db, ILogger<Program> logger);
    }
    public class AuthorizationService : IAuthorizationService
    {
        public async Task<User> CommonAuthCheck(HttpContext http, AppDbContext db, ILogger<Program> logger)
        {
            var username = http.User.Identity?.Name;

            if (username is null)
            {
                logger.LogWarning("Unauthorized access attempt to savePlaylist.");
                throw new UnauthorizedAccessException("Invalid Authorization header format");
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user is null)
            {
                logger.LogWarning("User not found in DB: {Username}", username);
                throw new UnauthorizedAccessException("Invalid Authorization header format");
            }
            return user;
        }
    }
}
