using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using YTdownloadBackend.Data;
using YTdownloadBackend.Models;


var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddOpenApi();        // Minimal API OpenAPI
builder.Services.AddSwaggerGen();     // Swagger UI
builder.Services.AddAuthorization();
builder.Services.AddHttpClient<IYouTubeService, YouTubeService>();


builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

var jwtSecretKey = "724619304f63dc196741ba61c3bed39cb96ed83e83858685264416d7824ddc87ee94e17d"; // at least 16 characters

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
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

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();


// Only in Development environment
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();     // Minimal API docs
    app.UseSwagger();     // Enable /swagger
    app.UseSwaggerUI();   // Enable UI
}

app.UseHttpsRedirection();

// Your existing endpoint
var summaries = new[]
{
    "Freezing","Bracing","Chilly","Cool","Mild","Warm","Balmy","Hot","Sweltering","Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast(
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        )
    ).ToArray();
    return forecast;
})
.RequireAuthorization()
.WithName("GetWeatherForecast");



app.MapPost("/savePlaylist", async ( PlaylistRequest request, AppDbContext db, HttpContext http) =>
{
    //  Get the logged-in user name from JWT
    var username = http.User.Identity?.Name;
    if (username is null){
        return Results.Unauthorized();
    }

    //  Find the user in DB
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (user is null) { 
        return Results.Unauthorized();
    }

    //  Extract playlist ID from URL using regex
    var match = Regex.Match(request.PlaylistUrl, @"[?&]list=([^&]+)");
    if (!match.Success)
        return Results.BadRequest("Invalid playlist URL");

    string playlistId = match.Groups[1].Value;


    var playlistName = !string.IsNullOrWhiteSpace(request.CustomName)
       ? request.CustomName
       : await ytService.GetPlaylistTitleAsync(playlistId)
           ?? $"Playlist_{DateTime.UtcNow:yyyyMMddHHmmss}";


    //  Save playlist to database
    var playlist = new Playlist
    {
        User = user,
        PlaylistId = playlistId,
        PlaylistName = request.CustomName ?? $"Playlist_{DateTime.UtcNow:yyyyMMddHHmmss}",
    };

    db.Playlists.Add(playlist);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        message = "Playlist saved successfully",
        playlistId = playlist.Id
    });
})
.RequireAuthorization()
.WithOpenApi();



app.MapGet("/playlists", async (AppDbContext db, HttpContext http) =>
{
    var username = http.User.Identity?.Name;
    if (username is null)
        return Results.Unauthorized();

    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (user is null)
        return Results.Unauthorized();

    var playlists = await db.Playlists
                            .Where(p => p.UserId == user.Id)
                            .Select(p => new {
                                p.Id,
                                p.PlaylistId
                            })
                            .ToListAsync();

    return Results.Ok(playlists);
})
.RequireAuthorization()
.WithOpenApi();




app.MapPost("/download/{videoId}", async (string videoId) =>
{
    string ytDlpFileName = "yt-dlp";
    if (OperatingSystem.IsWindows()) ytDlpFileName += ".exe";

    string ytDlpPath = Path.Combine(Directory.GetCurrentDirectory(), "yt-dlp", ytDlpFileName);
    if (!File.Exists(ytDlpPath))
        return Results.Problem($"yt-dlp executable not found at {ytDlpPath}");

    string downloadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "downloads");
    Directory.CreateDirectory(downloadsFolder);

    // Template: <title>.mp3
    string outputTemplate = Path.Combine(downloadsFolder, "%(title)s.%(ext)s");

    var psi = new ProcessStartInfo
    {
        FileName = ytDlpPath,
        Arguments = $"--extract-audio --audio-format mp3 -o \"{outputTemplate}\" https://www.youtube.com/watch?v={videoId}",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    var process = Process.Start(psi);
    if (process == null) return Results.Problem("Failed to start yt-dlp");

    string stdOut = await process.StandardOutput.ReadToEndAsync();
    string stdErr = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    return Results.Ok(new
    {
        VideoId = videoId,
        ExitCode = process.ExitCode,
        StdOut = stdOut,
        StdErr = stdErr
    });
})
.RequireAuthorization();


app.MapPost("/signup", async (AppDbContext db, SignupRequest request) =>
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



app.MapPost("/login", async (AppDbContext db, LoginRequest request) =>
{
    // Find user in database
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);

    if (user == null ) { 
        return Results.Unauthorized();
    }

    // Verify password using BCrypt
    bool isPasswordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
    if (!isPasswordValid)
    {
        return Results.Unauthorized();
    }

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
})
.WithName("Login")
.WithOpenApi(); // appear in Swagger



app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
