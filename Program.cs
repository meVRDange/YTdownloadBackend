using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using YTdownloadBackend.Data;
using YTdownloadBackend.Models;
using YTdownloadBackend.Services;


var builder = WebApplication.CreateBuilder(args);


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();    
builder.Services.AddAuthorization();
builder.Services.AddHttpClient<IYouTubeService, YouTubeService>();
builder.Services.AddScoped<IYtDlpService, YtDlpService>();
builder.Services.AddScoped<PlaylistScannerService>();


builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

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

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();


// Only in Development environment
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();     // Enable /swagger
    app.UseSwaggerUI();   // Enable UI
}

app.UseHttpsRedirection();

app.MapGet("/helthCheck", () =>
{
    return Results.Ok(new
    {
        message = "Authinticad",
    });
})
.RequireAuthorization()
.WithName("helthCheck");



app.MapPost("/savePlaylist", async ( PlaylistRequest request, AppDbContext db, HttpContext http, IYouTubeService ytService) =>
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


    // Check if user already has a playlist
    var existing = await db.Playlists.FirstOrDefaultAsync(p => p.UserId == user.Id);

    //  Extract playlist ID from URL using regex
    var match = Regex.Match(request.PlaylistUrl, @"[?&]list=([^&]+)");
    if (!match.Success)
        return Results.BadRequest("Invalid playlist URL");

    string playlistId = match.Groups[1].Value;



    var playlistTitle = !string.IsNullOrWhiteSpace(request.CustomName)
       ? request.CustomName
       : await ytService.GetPlaylistTitleAsync(playlistId)
           ?? $"Playlist_{DateTime.UtcNow:yyyyMMddHHmmss}";

    if (existing != null)
    {
        if (existing.PlaylistId == playlistId)
        {
            return Results.Ok(new
            {
                message = $"✅ You already have this playlist saved: {playlistTitle}"
            });
        }
        else
        {
            // Overwrite existing playlist
            db.Playlists.Remove(existing);

            db.Playlists.Add(new Playlist
            {
                PlaylistId = playlistId,
                PlaylistTitle = playlistTitle,
                UserId = user.Id
            });

            await db.SaveChangesAsync();

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

app.MapPost("/scan/{playlistId}", async (HttpContext http, string playlistId, PlaylistScannerService scanner) =>
{
    var username = http.User.Identity?.Name;
    if (username is null)
    {
        return Results.Unauthorized();
    }
    await scanner.ScanForNewAsync(playlistId, username);
    return Results.Ok("Scan completed.");
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
.WithName("auth/Login")
.WithOpenApi(); // appear in Swagger



app.Run();
public partial class Program { }

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
