using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using YTdownloadBackend.Data;
using YTdownloadBackend.Models;
using YTdownloadBackend.Services;
using YTdownloadBackend.Services.Storage;

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
// Register authorization service for endpoint injection
builder.Services.AddScoped<IAuthorizationService, AuthorizationService>();
builder.Services.AddScoped<RepositoryService>();
// Register the manually-started download queue service as a singleton so it can be started from endpoints
builder.Services.AddSingleton<DownloadQueueService>();
builder.Services.AddScoped<ISongPipeline, SongPipeline>();

// Configure storage provider options from the "Storage" config section
builder.Services.Configure<StorageProviderOptions>(builder.Configuration.GetSection("Storage"));

// Register storage provider and factory
builder.Services.AddSingleton<IStorageProvider, FirebaseStorageProvider>();
builder.Services.AddSingleton<IStorageProvider, LocalStorageProvider>();
builder.Services.AddSingleton<IStorageProviderFactory, StorageProviderFactory>();


builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// Firebase: default app uses GOOGLE_APPLICATION_CREDENTIALS (avid-life for Auth etc.)
// FCM uses a separate named app pointing to ytdownloder project
var firebaseCredentialsPath = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS")
    ?? Environment.GetEnvironmentVariable("FIREBASE_CREDENTIALS_PATH")
    ?? "Keys/ytdownloder-7aa70-firebase-adminsdk-fbsvc-565e663998.json";

FirebaseApp.Create(new AppOptions()
{
    Credential = GoogleCredential.FromFile(firebaseCredentialsPath)
});

// Create a named app for FCM using the ytdownloder service account
var fcmCredentialsPath = "Keys/ytdownloder-7aa70-firebase-adminsdk-fbsvc-565e663998.json";
if (File.Exists(fcmCredentialsPath))
{
    FirebaseApp.Create(new AppOptions()
    {
        Credential = GoogleCredential.FromFile(fcmCredentialsPath)
    }, "fcm-app");
}


// Add CORS policy to allow requests from Angular PWA
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularPWA", policy =>
    {
        policy.WithOrigins(
            "http://localhost:4200",
            "https://api.vdange.site",
            "https://vdange.site",
            "https://www.vdange.site",
            "https://192.168.29.110")
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

// ── Rate Limiting ──────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Login endpoint: 5 attempts per minute per connection
    options.AddPolicy("LoginRateLimit", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // Global: 120 requests per minute per connection (applied everywhere by default)
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // On rejected, write a clean JSON response
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            error = "Too many requests. Please slow down and try again later."
        });
        await context.HttpContext.Response.WriteAsync(result, cancellationToken);
    };
});

var app = builder.Build();

// Enable CORS policy - MUST come before Authentication/Authorization
app.UseCors("AllowAngularPWA");

app.UseRateLimiter();

// ── Security Headers ───────────────────────────────────────────
app.Use(async (ctx, next) =>
{
    var headers = ctx.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["X-XSS-Protection"] = "0";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none'";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

    // HSTS: only when running over HTTPS (not in local dev)
    if (ctx.Request.IsHttps)
    {
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    }

    await next();
});

app.UseAuthentication();
app.UseAuthorization();

// ── Local Storage: Serve downloads/ with JWT ownership check ──
// Only authenticated users can access their own files.
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/downloads", StringComparison.OrdinalIgnoreCase),
    downloadsApp =>
    {
        downloadsApp.Use(async (ctx, next) =>
        {
            // Path format: /downloads/{username}/{file...}
            var path = ctx.Request.Path.Value ?? "";
            var segments = path.TrimStart('/').Split('/', 3); // ["downloads", "username", "rest"]

            if (segments.Length < 2 ||
                !string.Equals(segments[0], "downloads", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = 400;
                return;
            }

            // Extract claimed username from JWT
            var usernameClaim = ctx.User?.FindFirst(ClaimTypes.Name)?.Value
                ?? ctx.User?.FindFirst("username")?.Value;

            if (string.IsNullOrEmpty(usernameClaim))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Authentication required.");
                return;
            }

            // Verify ownership: the URL path must start with the user's username
            if (segments.Length < 2 ||
                !string.Equals(segments[1], usernameClaim, StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsync("Access denied. You can only access your own files.");
                return;
            }

            await next();
        });

        downloadsApp.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
                Path.Combine(Directory.GetCurrentDirectory(), "downloads")),
            RequestPath = "/downloads",
            ServeUnknownFileTypes = true,
            DefaultContentType = "audio/mpeg"
        });
    });

// Only in Development environment
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();     // Enable /swagger
    app.UseSwaggerUI();   // Enable UI
}

// Temporarily disabled for phone testing on local network
// app.UseHttpsRedirection();

// Ensure downloads directory exists on startup
var downloadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "downloads");
Directory.CreateDirectory(downloadsFolder);

app.MapGet("/health", 
() => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .DisableRateLimiting()
    .WithName("Health")
    .WithOpenApi()
    .AllowAnonymous();


app.MapPost("/api/savePlaylist", async (PlaylistRequest request, AppDbContext db, HttpContext http, IYouTubeService ytService, ILogger<Program> logger, IAuthorizationService AuthService) =>
{
    if (request is null) return Results.BadRequest("Request body is required.");
    try
    {
        User user;
        try
        {
            user = await AuthService.CommonAuthCheck(http, db, logger);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlaylistUrl))
        {
            logger.LogWarning("Empty playlist URL provided by user: {Username}", user.Username);
            return Results.BadRequest("Playlist URL cannot be empty");
        }

        logger.LogInformation("Received playlist URL: {PlaylistUrl}", request.PlaylistUrl);
        logger.LogInformation("savePlaylist called by user: {Username} (Id: {UserId})", user.Username, user.Id);

        var existing = await db.Playlists.FirstOrDefaultAsync(p => p.UserId == user.Id);

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
                logger.LogInformation("Playlist already exists for user: {Username}", user.Username);
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

                logger.LogInformation("Existing playlist replaced for user: {Username}", user.Username);
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

        logger.LogInformation("New playlist saved for user: {Username}, PlaylistId: {PlaylistId}, Title: {PlaylistTitle}", user.Username, playlistId, playlistTitle);

        return Results.Ok(new
        {
            message = "Playlist saved successfully",
            playlistId = playlist.Id,
            playlistTitle = playlist.PlaylistTitle
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error in savePlaylist endpoint");
                return Results.Problem(statusCode: 500, title: "Internal Server Error", detail: "An error occurred while saving the playlist");
    }
})
.RequireAuthorization()
.WithOpenApi();


app.MapGet("/api/getPlaylist", async (AppDbContext db, HttpContext http, IAuthorizationService AuthService, ILogger<Program> logger) =>
{
    User user;
    try
    {
        user = await AuthService.CommonAuthCheck(http, db, logger);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }

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

app.MapPost("/api/sync", async (AppDbContext db, HttpContext http, IAuthorizationService AuthService, ILogger<Program> logger, PlaylistScannerService scanner, DownloadQueueService queueService) =>
{
    User user;
    try
    {
        user = await AuthService.CommonAuthCheck(http, db, logger);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }

    var playlist = await db.Playlists.FirstOrDefaultAsync(p => p.UserId == user.Id);
    if (playlist is null)
    {
        return Results.NotFound(new { message = "No playlist saved yet" });
    }

    logger.LogInformation("Syncing playlist {PlaylistId} for user {Username}", playlist.PlaylistId, user.Username);

    // Step 1: Scan YouTube for new songs
    await scanner.ScanForNewAddedSongsAsync(playlist.PlaylistId, user.Username);

    // Step 2: Start the download queue to process any pending songs
    if (!queueService.IsRunning)
    {
        queueService.StartIfNotRunning(user.Username);
        logger.LogInformation("Download queue started for user {Username}", user.Username);
    }

    return Results.Ok(new { message = "Sync started", playlistId = playlist.PlaylistId });
})
.RequireAuthorization()
.WithOpenApi();

app.MapGet("/api/getSongs", async (HttpContext http, ILogger<Program> logger, AppDbContext db, IAuthorizationService AuthService) =>
{
    User user;
    try
    {
        user = await AuthService.CommonAuthCheck(http, db, logger);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }

    logger.LogInformation("Fetching songs for user {Username}", user.Username);

    // Get the user's playlist
    var playlist = await db.Playlists.FirstOrDefaultAsync(p => p.UserId == user.Id);
    if (playlist is null)
    {
        logger.LogInformation("No playlist found for user {Username}", user.Username);
        return Results.Ok(new { songs = new List<object>(), message = "No playlist saved yet" });
    }
    logger.LogInformation("Found playlist {PlaylistId} for user {Username}", playlist.PlaylistId, user.Username);

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

    if (songs.Count == 0)
    {
        logger.LogInformation("No songs found in playlist {PlaylistId} for user {Username}", playlist.PlaylistId, user.Username);
        return Results.Ok(new { songs = new List<object>(), message = "No songs found in the playlist" });
    }
    logger.LogInformation("Found {Count} songs for user {Username}", songs.Count, user.Username);
    return Results.Ok(new { songs, playlistId = playlist.PlaylistId, playlistTitle = playlist.PlaylistTitle });
})
.RequireAuthorization()
.WithOpenApi();

app.MapPost("/api/download", async (string videoId, HttpContext http, AppDbContext db, ILogger<Program> logger, DownloadQueueService queueService, IAuthorizationService AuthService) =>
{
    User user;
    try
    {
        user = await AuthService.CommonAuthCheck(http, db, logger);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }

    logger.LogInformation("Download requested by user: {Username}", user.Username);

    var song = await db.PlaylistSongs.FirstOrDefaultAsync(s => s.VideoId == videoId);
    if (song is null)
    {
        return Results.NotFound();
    }

    // verify ownership
    var playlist = await db.Playlists.FirstOrDefaultAsync(p => p.PlaylistId == song.PlaylistId && p.UserId == user.Id);
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
    await db.SaveChangesAsync();

    logger.LogInformation("Enqueued download job for videoId={videoId}", song.Id);

    // Start the download queue service if it's not running.
    if (!queueService.IsRunning)
    {
        queueService.StartIfNotRunning(user.Username);
    }
    return Results.Accepted(null, new { jobId = song.Id, status = song.Status });
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
    } catch(Exception)
    {
        return Results.StatusCode(500);
    }
})
.RequireRateLimiting("LoginRateLimit")
.WithName("api/auth/Login")
.WithOpenApi(); // appear in Swagger

app.MapPost("/api/auth/saveFcmToken", async (AppDbContext db, HttpContext http, SaveFcmTokenRequest request, ILogger<Program> logger, IAuthorizationService AuthService) =>
{
    User user;
    try
    {
        user = await AuthService.CommonAuthCheck(http, db, logger);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(request.FCMToken))
    {
        logger.LogWarning("Empty FCM token provided by user: {Username}", user.Username);
        return Results.BadRequest(new { message = "FCM token cannot be empty" });
    }

    user.FCMToken = request.FCMToken;
    await db.SaveChangesAsync();

    logger.LogInformation("FCM token saved for user: {Username}", user.Username);
    return Results.Ok(new { message = "FCM token saved successfully" });
})
.RequireAuthorization()
.WithName("SaveFcmToken")
.WithOpenApi();

app.MapPost("/api/auth/fcmTest", async (AppDbContext db, HttpContext http, IFcmService fcmService, ILogger<Program> logger, IAuthorizationService AuthService, FcmTestRequest request) =>
{
    User user;
    try
    {
        user = await AuthService.CommonAuthCheck(http, db, logger);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(user.FCMToken))
    {
        return Results.BadRequest(new { message = "No FCM token saved for this user. Save a token via /api/auth/saveFcmToken first." });
    }

    var songTitle = !string.IsNullOrWhiteSpace(request.SongTitle) ? request.SongTitle : "Tu Hai Kahan (SlowReverb)  AUR  Beatblex Music.mp3";
    var downloadUrl = !string.IsNullOrWhiteSpace(request.DownloadUrl) ? request.DownloadUrl : "https://drive.google.com/file/d/1qrAC-UJzBHggkpXkC0NchwgxGekSvJhR/view?usp=sharing";

    var messageId = await fcmService.SendDownloadCompletedNotificationAsync(user.FCMToken, songTitle, downloadUrl);

    if (messageId == null)
    {
        logger.LogWarning("FCM test notification failed for user: {Username}", user.Username);
        return Results.Json(new { success = false, message = "Failed to send test notification. Check server logs for details." }, statusCode: 500);
    }

    logger.LogInformation("FCM test notification sent. MessageId={MessageId} SongTitle={SongTitle} User={Username}", messageId, songTitle, user.Username);
    return Results.Ok(new { success = true, message = "Test notification sent successfully!", messageId, songTitle, downloadUrl });
})
.RequireAuthorization()
.WithName("FcmTest")
.WithOpenApi();


app.Run();
public partial class Program { }
