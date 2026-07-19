# YTdownloadBackend — Architecture & Flow Documentation

## System Overview

YTdownloadBackend is a .NET 10.0 Minimal API that lets authenticated users save YouTube playlists, scan for new songs, download audio via yt-dlp, upload to Google Cloud Storage (GCS), and notify Android clients via FCM.

---

## Core Download Flow (Server → Android)

```mermaid
sequenceDiagram
    participant User as Android App
    participant API as Backend API
    participant DB as SQL Server
    participant YTDLP as yt-dlp
    participant GCS as Google Cloud Storage
    participant FCM as Firebase Cloud Messaging

    User->>API: POST /api/download {videoId}
    API->>DB: Update PlaylistSong status = Pending
    API->>API: Start DownloadQueueService (if not running)
    API-->>User: 202 Accepted {jobId, statusUrl}

    Note over API: DownloadQueueService WorkerLoop
    API->>DB: Query PlaylistSongs WHERE Status = Pending
    API->>DB: Update status = Processing
    API->>YTDLP: Download audio (MP3)
    YTDLP-->>API: Local file saved to /downloads/{username}/{title}.mp3

    alt Download succeeds
        API->>Storage: Upload MP3 → users/{userId}/songs/{title}.mp3
        Storage-->>API: Storage path confirmed
        API->>Storage: Generate signed URL (48h expiry)
        Storage-->>API: Signed download URL
        API->>DB: Update PlaylistSong (StoragePath, DownloadUrl, DownloadUrlExpiry, status = Completed)
        API->>FCM: Send data message {type: DOWNLOAD_COMPLETED, songTitle, downloadUrl}
        FCM-->>User: Silent push notification (data-only)
        API->>API: Delete local MP3 file
    else Download fails
        API->>DB: RetryCount++, status = Pending (retry) or Failed (≥3 retries)
    end
```

### Step-by-Step Breakdown

| Step | Component | Action |
|------|-----------|--------|
| 1 | Android App | User taps "Download" → `POST /api/download {videoId}` |
| 2 | API | Validates ownership (user → playlist → song), sets `Status = Pending`, enqueues |
| 3 | DownloadQueueService | Polls DB for `Pending` songs, sets `Status = Processing` |
| 4 | YtDlpService | Runs `yt-dlp --restrict-filenames -x --audio-format mp3 -o "{downloads}/{username}/%(title)s.%(ext)s" {videoId}` |
| 5 | SongUploadService | Verifies local file exists, checks storage for duplicates via IStorageProvider, uploads MP3 |
| 6 | DownloadUrlService | Generates signed URL (48h default) via IStorageProvider, caches in DB with expiry |
| 7 | SongUploadService | Updates DB: `Status = Completed`, `StoragePath`, `DownloadUrl`, `DownloadUrlExpiry` |
| 8 | FcmService | Sends **data-only** FCM message: `{type, songTitle, downloadUrl, timestamp}` |
| 9 | SongUploadService | Deletes local MP3 file |
| 10 | Android App | Receives FCM data message → WorkManager downloads MP3 from signed URL |

---

## Android Background Download Flow (Client-Side)

This is the **recommended** approach for the Android client to handle FCM-triggered downloads:

```mermaid
sequenceDiagram
    participant FCM as Firebase Cloud Messaging
    participant WM as WorkManager
    participant GCS as Google Cloud Storage
    participant Cache as getCacheDir()
    participant Room as Room Database
    participant UI as App UI (Foreground)

    FCM->>WM: Data message received {type: DOWNLOAD_COMPLETED, downloadUrl, songTitle}
    WM->>GCS: Download MP3 from signed URL
    GCS-->>WM: MP3 bytes
    WM->>Cache: Save to getCacheDir()/downloads/{songTitle}.mp3
    WM->>Room: Insert record: {songId, status: "DOWNLOADED_PENDING_PERSIST", cachePath}
    Note over WM: Background work complete

    Note over UI: User opens app
    UI->>Room: Query songs WHERE status = "DOWNLOADED_PENDING_PERSIST"
    UI->>Cache: Read MP3 from cacheDir
    UI->>UI: Move to getFilesDir() or MediaStore (permanent storage)
    UI->>Room: Update status = "PERSISTED"
```

### Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| **Data-only FCM message** (not notification) | Silent push triggers WorkManager without showing UI |
| **Download to `getCacheDir()`** | Background workers CAN write to cache; cannot write to app-specific persistent storage in background |
| **Room DB for tracking** | Lightweight SQLite on Android; tracks "downloaded but not yet persisted" state |
| **Move on app open** | Foreground app has full storage access; moves from cache → permanent location |
| **NOT IndexedDB** | IndexedDB is a browser API, not available in native Android; bad for large binary files |

### Signed URL Expiry Risk

| Risk | Mitigation |
|------|------------|
| 48h signed URL may expire before Android downloads | **Option A**: Extend to 7 days (GCS supports up to 7d) |
| | **Option B**: Add `GET /api/songs/{id}/refresh-url` endpoint that generates a fresh URL |
| | **Option C**: Android checks URL freshness before download; if expired, calls refresh endpoint |

**Recommended**: Option A (extend to 7 days) + Option B (refresh endpoint as fallback)

---

## Authentication Flow

```mermaid
sequenceDiagram
    participant App as Android App
    participant API as Backend API
    participant DB as SQL Server

    App->>API: POST /api/auth/signup {username, password}
    API->>DB: BCrypt.HashPassword(password) → Store User
    API-->>App: 200 OK {message}

    App->>API: POST /api/auth/login {username, password}
    API->>DB: Find user, BCrypt.Verify(password, hash)
    API->>API: Generate JWT (ClaimTypes.Name = username, Claim "UserId" = id, Expires = 30 days)
    API-->>App: 200 OK {token}

    App->>API: POST /api/auth/saveFcmToken {FCMToken} (Bearer token)
    API->>DB: Update User.FCMToken
    API-->>App: 200 OK

    Note over App: All subsequent requests include Bearer token
```

### Current Auth Issues (to be fixed in Phase 1)

- JWT secret is **hardcoded** in `appsettings.json` — must move to environment variable only
- Token expiry is **30 days** — too long; should be reduced (e.g., 1-7 days with refresh tokens)
- No `ValidateIssuer` / `ValidateAudience` — token accepted from any source
- Some endpoints check `http.User.Identity?.Name` manually instead of using `IAuthorizationService`

---

## Service Architecture

```mermaid
graph TD
    subgraph API Layer
        P[Program.cs - Minimal API Endpoints]
    end

    subgraph Services
        DQS[DownloadQueueService<br/>Singleton, manual start]
        SUS[SongUploadService<br/>Scoped]
        DUS[DownloadUrlService<br/>Scoped]
        FSP[FirebaseStorageProvider<br/>Singleton]
        SPF[StorageProviderFactory<br/>Singleton]
        FCM[FcmService<br/>Singleton]
        YTS[YouTubeService<br/>Scoped via AddHttpClient]
        YDS[YtDlpService<br/>Scoped]
        PSS[PlaylistScannerService<br/>Scoped]
        RS[RepositoryService<br/>Scoped]
        AS[AuthorizationService<br/>Scoped]
    end

    subgraph Data
        DB[AppDbContext<br/>SQL Server]
    end

    subgraph External
        GCS[Google Cloud Storage]
        FBA[Firebase Admin SDK]
        YTA[YouTube Data API v3]
        YDL[yt-dlp CLI]
    end

    P --> DQS
    P --> SUS
    P --> AS
    P --> PSS
    P --> YTS
    P --> FCM

    DQS --> RS
    DQS --> YDS
    DQS --> SUS
    DQS --> DB

    SUS --> SPF
    SUS --> DUS
    SUS --> FCM
    SUS --> DB

    DUS --> SPF
    DUS --> DB

    SPF --> FSP
    FSP --> GCS
    FCM --> FBA
    YTS --> YTA
    YDS --> YDL
```

---

## API Endpoints Summary

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| GET | `/health` | Anonymous | Health check |
| GET | `/api/healthCheck` | Bearer | Debug: sends FCM test message (uses configured token) |
| GET | `/api/uploadTest` | Bearer | Debug: tests upload pipeline with hardcoded file ⚠️ |
| POST | `/api/savePlaylist` | Bearer | Save/replace user's YouTube playlist |
| GET | `/api/getPlaylist` | Bearer | Get user's saved playlist |
| GET | `/api/getSongs` | Bearer | Scan for new songs + list all songs in playlist |
| POST | `/api/download` | Bearer | Enqueue a song for download |
| POST | `/api/auth/signup` | Anonymous | Create new user account |
| POST | `/api/auth/login` | Anonymous | Authenticate and get JWT |
| POST | `/api/auth/saveFcmToken` | Bearer | Save device FCM token for push notifications |

---

## Data Model

```mermaid
erDiagram
    User ||--o| Playlist : has
    Playlist ||--o{ PlaylistSong : contains
    User {
        int Id PK
        string Username UK
        string PasswordHash
        string FCMToken
    }
    Playlist {
        int Id PK
        string PlaylistId UK
        string PlaylistTitle
        int UserId FK
    }
    PlaylistSong {
        int Id PK
        string PlaylistId FK
        string VideoId
        string Title
        long DurationSeconds
        string ThumbnailUrl
        PlaylistSongStatus Status
        int RetryCount
        DateTime DownloadedAt
        DateTime LastChecked
        string StoragePath
        string DownloadUrl
        DateTime DownloadUrlExpiry
    }
```

### Known Data Issues

- `PlaylistSong.PlaylistId` is a **string FK** to `Playlist.PlaylistId` (not `Playlist.Id`) — migration shows `PlaylistId1` as nullable
- `PlaylistSongStatus` enum has gaps: `Pending=1, Processing=3, Completed=4, Failed=5` (missing 2)
- No `OnModelCreating` override for Fluent API configuration

---

## Configuration (appsettings.json)

| Key | Current Value | Issue |
|-----|---------------|-------|
| `Jwt:Secret` | Hardcoded 74-char hex string | ⚠️ Must be env-var only |
| `YouTube:ApiKey` | Blank placeholder | Needs real key or env var |
| `Firebase:StorageBucket` | `"ytdownloder"` | Typo (should be "ytdownloader"?) |
| `ConnectionStrings:DefaultConnection` | Local SQL Express | OK for dev |
| Firebase key file path | Hardcoded in Program.cs | Should be in config |

---

## Refactoring Plan

### Phase 1: Security & Correctness Fixes
1. Move JWT Secret to environment variables only (remove from appsettings.json)
2. Remove hardcoded FCM token from `/api/healthCheck` endpoint
3. Move Firebase key file path to configuration
4. Fix `throw ex` → `throw` in SongUploadService.cs (preserves stack trace)
5. Gate debug endpoints (`/api/healthCheck`, `/api/uploadTest`) behind `#if DEBUG`
6. Add `RequireAuthorization()` consistently to all protected endpoints
7. Reduce JWT token expiry from 30 days to 7 days

### Phase 2: Structural Refactoring
8. Extract endpoints from Program.cs into separate files (Endpoints/ folder)
9. Create `ServiceCollectionExtensions.cs` for DI registration
10. Create separate test project, move test packages out of production
11. Remove `AWSSDK.S3` (completely unused)
12. Remove dead code: `RabbitMqPublisher`, `WeatherForecast` record, commented-out code blocks
13. Fix doubled namespaces in Models and Services

### Phase 3: Service Layer Improvements
14. Convert `DownloadQueueService` to proper `BackgroundService` / `IHostedService`
15. Replace `Console.WriteLine` with `ILogger` in `YtDlpService`, `PlaylistScannerService`
16. Create `IRepositoryService` interface for `RepositoryService`
17. Standardize auth pattern (use `IAuthorizationService` everywhere)
18. Fix `PlaylistSongStatus` enum gaps (sequential: 0,1,2,3)
19. Fix `YtDlpService` constructor (remove hardcoded path, use config)
20. Fix typos: `helthCheck` → `healthCheck`, `Authinticad` → `Authenticated`

### Phase 4: Data Layer Fixes
21. Add `OnModelCreating` with Fluent API for FK relationships
22. Align EF Core package versions with .NET 10 target
23. Fix `PlaylistSong → Playlist` FK relationship (required, cascade delete)
