using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Linq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using YTdownloadBackend.Data;
using YTdownloadBackend.Models;
using YTdownloadBackend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
// Configure Swagger to accept a JWT bearer token
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter your token in the input below.\n\n" +
                      "You can paste either just the token or the full value prefixed with 'Bearer '.\n\n" +
                      "Example: \"eyJhbGci...\" or \"Bearer eyJhbGci...\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
                Scheme = "bearer",
                Name = "Authorization",
                In = ParameterLocation.Header,
            },
            new List<string>()
        }
    });
});
builder.Services.AddAuthorization();
builder.Services.AddHttpClient<IYouTubeService, YouTubeService>();
builder.Services.AddScoped<IYtDlpService, YtDlpService>();
builder.Services.AddScoped<PlaylistScannerService>();
builder.Services.AddSingleton<IFcmService, FcmService>();

// Register the manually-started download queue service as a singleton so it can be started from endpoints
builder.Services.AddSingleton<DownloadQueueService>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

FirebaseApp.Create(new AppOptions()
{
    Credential = GoogleCredential.FromFile("Keys\\ytdownloder-7aa70-firebase-adminsdk-fbsvc-565e663998.json")
});


// Add CORS policy to allow requests from Angular PWA
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularPWA", policy =>
    {
        policy.WithOrigins("http://localhost:4200", "https://localhost", "https://192.168.29.87:5062")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var jwtSecretKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
                  ?? builder.Configuration["Jwt:Secret"]
                  ?? throw new InvalidOperationException("JWT secret key not configured.");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,              // we are not validating issuer yet
            ValidateAudience = false,            // we are not validating audience yet
            ValidateLifetime = true,             // token must not be expired
            ValidateIssuerSigningKey = true,     // must validate signature
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey))
        };
    });

// Add Firebase Storage registration:
var firebaseBucketName = builder.Configuration["Firebase:StorageBucket"]
    ?? throw new InvalidOperationException("Firebase storage bucket not configured.");

builder.Services.AddSingleton<IFirebaseStorageService>(sp =>
    new FirebaseStorageService(
        firebaseBucketName,
        sp.GetRequiredService<ILogger<FirebaseStorageService>>()
    )
);

builder.Services.AddScoped<IFirebaseUrlService, FirebaseUrlService>();
builder.Services.AddScoped<ISongUploadService, SongUploadService>();


var app = builder.Build();

// Enable CORS policy - MUST come before Authentication/Authorization
app.UseCors("AllowAngularPWA");

app.UseAuthentication();
app.UseAuthorization();

// Only in Development environment
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();     // Enable /swagger
    app.UseSwaggerUI();   // Enable UI
}

app.UseHttpsRedirection();

// Ensure downloads directory exists on startup
var downloadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "downloads");
Directory.CreateDirectory(downloadsFolder);

app.MapGet("/api/helthCheck", async (ILogger<Program> logger, HttpContext http, IFcmService fcmService) =>
{
    // Try to get the JWT token from the Authorization header
    var authHeader = http.Request.Headers["Authorization"].FirstOrDefault();
    string? jwtToken = null;
    if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
    {
        jwtToken = authHeader.Substring("Bearer ".Length).Trim();
        logger.LogInformation("Health check called. JWT token: {JwtToken}", jwtToken);
    }
    else
    {
        logger.LogInformation("Health check called. No JWT token found in Authorization header.");
    }

    var payload = new Dictionary<string, string>{
        {"type","DOWNLOAD_SONG"},
        {"songId","123"},
        {"downloadUrl","https://your-backend/api/download/file/123"},
        {"title","My Song"}
    };
    string? messageId = await fcmService.SendDownloadNotificationAsync("da9vrVolSOiYsd0Scb0JVi:APA91bFcBE-HbbvLKkt1Jpu0ktcbv2M_alOim83MqRNg9mXwsANaBM9SyqBJt3amxcchsx0AHYJ9wA_YzWYyma7aCzLWDJC24bVy6MRHRJGY-oJRM4dq-DY", payload);

    return Results.Ok(new
    {
        message = "Authinticad",
    });
})
.RequireAuthorization()
.WithName("helthCheck");


app.MapGet("/api/uploadTest", async (ILogger<Program> logger, HttpContext http, IFcmService fcmService, AppDbContext db, ISongUploadService uploadService) =>
{
    var username = http.User.Identity?.Name;
    logger.LogInformation("savePlaylist called by user: {Username}", username);

    if (username is null)
    {
        logger.LogWarning("Unauthorized access attempt to savePlaylist.");
        return Results.Unauthorized();
    }

    User user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    PlaylistSong playlistSong= new PlaylistSong { PlaylistId = "TEST_PLAYLIST", VideoId = "TEST_VIDEO", Title = "Test Song" };
    string expectedPath = Path.Combine(Directory.GetCurrentDirectory(), "downloads","sa", "august.mp3");
    bool uploadSuccess = await uploadService.ProcessDownloadedSongAsync(playlistSong, expectedPath, username, user);
    return Results.Ok(new
    {
        message = "Authinticad",
    });
})
.RequireAuthorization()
.WithName("uploadTest");

app.MapPost("/api/savePlaylist", async (PlaylistRequest request, AppDbContext db, HttpContext http, IYouTubeService ytService, ILogger<Program> logger) =>
{
    var username = http.User.Identity?.Name;
    logger.LogInformation("savePlaylist called by user: {Username}", username);

    if (username is null)
    {
        logger.LogWarning("Unauthorized access attempt to savePlaylist.");
        return Results.Unauthorized();
    }

    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (user is null)
    {
        logger.LogWarning("User not found in DB: {Username}", username);
        return Results.Unauthorized();
    }

    logger.LogInformation("User found: {Username} (Id: {UserId})", user.Username, user.Id);

    var existing = await db.Playlists.FirstOrDefaultAsync(p => p.UserId == user.Id);
    logger.LogInformation("Received playlist URL: {PlaylistUrl}", request.PlaylistUrl);

    var match = Regex.Match(request.PlaylistUrl, @"[?&]list=([^&]+)");
    if (!match.Success)
    {
        logger.LogWarning("Invalid playlist URL: {PlaylistUrl}", request.PlaylistUrl);
        return Results.BadRequest("Invalid playlist URL");
    }

    string playlistId = match.Groups[1].Value;
    logger.LogInformation("Extracted playlistId: {PlaylistId}", playlistId);

    var playlistTitle = !string.IsNullOrWhiteSpace(request.CustomName)
        ? request.CustomName
        : await ytService.GetPlaylistTitleAsync(playlistId)
            ?? $"Playlist_{DateTime.UtcNow:yyyyMMddHHmmss}";

    logger.LogInformation("Playlist title resolved: {PlaylistTitle}", playlistTitle);

    if (existing != null)
    {
        logger.LogInformation("User already has a playlist saved: {ExistingPlaylistId}", existing.PlaylistId);

        if (existing.PlaylistId == playlistId)
        {
            logger.LogInformation("Playlist already exists for user: {Username}", username);
            return Results.Ok(new
            {
                message = $"✅ You already have this playlist saved: {playlistTitle}"
            });
        }
        else
        {
            logger.LogInformation("Replacing existing playlist {OldPlaylistId} with new {NewPlaylistId}", existing.PlaylistId, playlistId);
            db.Playlists.Remove(existing);

            db.Playlists.Add(new Playlist
            {
                PlaylistId = playlistId,
                PlaylistTitle = playlistTitle,
                UserId = user.Id
            });

            await db.SaveChangesAsync();

            logger.LogInformation("Existing playlist replaced for user: {Username}", username);
            return Results.Ok(new
            {
                message = $"♻️ Existing playlist replaced with: {playlistTitle}"
            });
        }
    }

    //  Save playlist to database
    var playlist = new Playlist
    {
        User = user,
        PlaylistId = playlistId,
        PlaylistTitle = playlistTitle,
    };

    db.Playlists.Add(playlist);
    await db.SaveChangesAsync();

    logger.LogInformation("New playlist saved for user: {Username}, PlaylistId: {PlaylistId}, Title: {PlaylistTitle}", username, playlistId, playlistTitle);

    return Results.Ok(new
    {
        message = "Playlist saved successfully",
        playlistId = playlist.Id,
        playlistTitle = playlist.PlaylistTitle
    });
})
.RequireAuthorization()
.WithOpenApi();


app.MapGet("/api/getPlaylist", async (AppDbContext db, HttpContext http) =>
{
    var username = http.User.Identity?.Name;
    if (username is null)
        return Results.Unauthorized();

    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (user is null)
        return Results.Unauthorized();

    var playlist = await db.Playlists
        .Where(p => p.UserId == user.Id)
        .Select(p => new {
            p.Id,
            p.PlaylistTitle
        })
        .FirstOrDefaultAsync();

    if (playlist == null)
        return Results.NotFound();

    return Results.Ok(playlist);
})
.RequireAuthorization()
.WithOpenApi();

app.MapGet("/api/getSongs", async (HttpContext http, ILogger<Program> logger, AppDbContext db,PlaylistScannerService scanner, DownloadQueueService queueService) =>
{
    var username = http.User.Identity?.Name;
    if (username is null)
    {
        logger.LogWarning("Unauthorized access to playlist songs");
        return Results.Unauthorized();
    }

    logger.LogInformation("Fetching songs for user {Username}", username);


    // Get the user from the database
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (user is null)
    {
        logger.LogWarning("User not found in DB: {Username}", username);
        return Results.Unauthorized();
    }

    // Get the user's playlist
    var playlist = await db.Playlists.FirstOrDefaultAsync(p => p.UserId == user.Id);
    if (playlist is null)
    {
        logger.LogInformation("No playlist found for user {Username}", username);
        return Results.Ok(new { songs = new List<object>(), message = "No playlist saved yet" });
    }
    logger.LogInformation("Found playlist {PlaylistId} for user {Username}", playlist.PlaylistId, username);

    await scanner.ScanForNewAddedSongsAsync(playlist.PlaylistId, username);


    // Get all songs for the user's playlist
    var songs = await db.PlaylistSongs
        .Where(s => s.PlaylistId == playlist.PlaylistId)
        .Select(s => new {
            s.Id,
            s.VideoId,
            s.Title,
            s.DurationSeconds,
            s.ThumbnailUrl,
            IsDownloaded = s.Status == PlaylistSongStatus.Completed,
            s.DownloadedAt
        })
        .ToListAsync();

    // Start the download queue service if it's not running
    //if (!queueService.IsRunning && songs.Count > 0)
    //{
    //    // Start the worker using the first song id as the trigger. The service will ensure a DownloadTask exists.
    //    queueService.StartIfNotRunning(songs[0].Id);
    //}
    if (songs.Count == 0)
    {
        logger.LogInformation("No songs found in playlist {PlaylistId} for user {Username}", playlist.PlaylistId, username);
        return Results.Ok(new { songs = new List<object>(), message = "No songs found in the playlist" });
    }
    logger.LogInformation("Found {Count} songs for user {Username}", songs.Count, username);
    return Results.Ok(new { songs, playlistId = playlist.PlaylistId, playlistTitle = playlist.PlaylistTitle });
})
.RequireAuthorization()
.WithOpenApi();

app.MapPost("/api/download", async (string videoId, HttpContext http, AppDbContext db, ILogger<Program> logger, DownloadQueueService queueService) =>
{
    var username = http.User.Identity?.Name;
    if (username is null)
    { 
        logger.LogWarning("Unauthorized access to playlist songs");
        return Results.Unauthorized();
    }

    var song = await db.PlaylistSongs.FirstOrDefaultAsync(s => s.VideoId == videoId);
    if (song is null)
    {
        return Results.NotFound();
    }

    // verify ownership
    var userId = await db.Users.Where(u => u.Username == username).Select(u => u.Id).FirstOrDefaultAsync();
    if (userId == 0)
    {
        return Results.Unauthorized();
    }

    var playlist = await db.Playlists.FirstOrDefaultAsync(p => p.PlaylistId == song.PlaylistId && p.UserId == userId);
    if (playlist is null)
    {
        return Results.Unauthorized();
    }

    if (song.Status == PlaylistSongStatus.Completed)
    {
        var downloadUrl = $"/api/download/file/{song.Id}";
        return Results.Ok(new { videoId = song.Id, status = song.Status, downloadUrl });
    }

    // mark pending and enqueue
    song.Status = PlaylistSongStatus.Pending;
    song.RetryCount = 0;
    song.LastChecked = DateTime.UtcNow;
    song.RetryCount = 0;
    await db.SaveChangesAsync();

    var jobUrl = $"/api/download/status/{song.Id}";
    var downloadUrlPreview = $"/api/download/file/{song.Id}";
    logger.LogInformation("Enqueued download job for videoId={videoId}", song.Id);

    // Start the download queue service if it's not running.
    if (!queueService.IsRunning)
    {
        queueService.StartIfNotRunning(username);
    }

    return Results.Accepted(jobUrl, new { jobId = song.Id, status = song.Status, downloadUrl = downloadUrlPreview });
})
.RequireAuthorization()
.WithOpenApi();

app.MapPost("/api/auth/signup", async (AppDbContext db, SignupRequest request) =>
{
    // Check if user already exists
    var existingUser = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
    if (existingUser != null)
    {
        return Results.BadRequest(new { message = "Username already exists" });
    }

    // Hash the password before storing
    string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);


    var user = new User
    {
        Username = request.Username,
        PasswordHash = passwordHash
    };

    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Ok(new { message = "User created successfully" });
})
.WithName("Signup")
.WithOpenApi(); // appear in Swagger

app.MapPost("/api/auth/login", async (AppDbContext db, LoginRequest request, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("Login attempt for username: {Username}", request.Username);
        // Find user in database
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);

        if (user == null ) { 
            logger.LogWarning("Login failed: user not found for username: {Username}", request.Username);
            return Results.Unauthorized();
        }
        //log the username from db
        logger.LogInformation("User found in DB: {Username}", user.Username);
        // Verify password using BCrypt
        bool isPasswordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
        if (!isPasswordValid)
        {
            logger.LogWarning("Login failed: invalid password for username: {Username}", request.Username);
            return Results.Unauthorized();
        }

        logger.LogInformation("Login successful for username: {Username}", user.Username);
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(jwtSecretKey);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim("UserId", user.Id.ToString())
            }),
            Expires = DateTime.UtcNow.AddDays(30), // long-lived token
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var jwtToken = tokenHandler.WriteToken(token);

        return Results.Ok(new { token = jwtToken });
    } catch(Exception ex)
    {
        return Results.StatusCode(500);
    }
})
.WithName("api/auth/Login")
.WithOpenApi(); // appear in Swagger

app.MapPost("/api/auth/saveFcmToken", async (AppDbContext db, HttpContext http, SaveFcmTokenRequest request, ILogger<Program> logger) =>
{
    var username = http.User.Identity?.Name;
    if (username is null)
    {
        logger.LogWarning("Unauthorized access attempt to saveFcmToken.");
        return Results.Unauthorized();
    }

    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (user is null)
    {
        logger.LogWarning("User not found in DB: {Username}", username);
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(request.FCMToken))
    {
        logger.LogWarning("Empty FCM token provided by user: {Username}", username);
        return Results.BadRequest(new { message = "FCM token cannot be empty" });
    }

    user.FCMToken = request.FCMToken;
    await db.SaveChangesAsync();

    logger.LogInformation("FCM token saved for user: {Username}", username);
    return Results.Ok(new { message = "FCM token saved successfully" });
})
.RequireAuthorization()
.WithName("SaveFcmToken")
.WithOpenApi();



app.Run();
public partial class Program { }

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
