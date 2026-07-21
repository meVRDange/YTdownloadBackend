# YTdownloadBackend API Documentation

**Base URL:** `https://api.vdange.site` (production) / `http://localhost:5000` (dev)

---

## Authentication

All endpoints except `/health` and `/api/auth/login` require a JWT Bearer token.

```
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
```

- Token expires after **30 days**
- Obtain token via `POST /api/auth/login`
- Token contains claims: `Name` (username) and `UserId`

---

## Endpoints

### 1. Health Check

```
GET /health
```

**Auth:** None

**Response `200:`**
```json
{ "status": "healthy", "timestamp": "2026-07-19T12:00:00Z" }
```

---

### 2. Login

```
POST /api/auth/login
```

**Auth:** None  
**Rate limit:** 5 attempts/min per IP

**Request:**
```json
{
  "username": "john",
  "password": "mypassword"
}
```

**Response `200:`**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIs..."
}
```

**Response `401:`** — Bad credentials

---

### 3. Save FCM Token

Register the device for push notifications.

```
POST /api/auth/saveFcmToken
Authorization: Bearer <token>
```

**Request:**
```json
{
  "fcmToken": "dKxFj9:APA91b..."
}
```

**Response `200:`**
```json
{ "message": "FCM token saved successfully" }
```

---

### 4. Save Playlist

Save or replace the user's YouTube playlist.

```
POST /api/savePlaylist
Authorization: Bearer <token>
```

**Request:**
```json
{
  "playlistUrl": "https://www.youtube.com/playlist?list=PLabc123...",
  "customName": "My Favorite Songs"   // optional, falls back to YouTube title
}
```

**Response `200` (new playlist):**
```json
{
  "message": "Playlist saved successfully",
  "playlistId": 1,
  "playlistTitle": "My Favorite Songs"
}
```

**Response `200` (already saved):**
```json
{
  "message": "✅ You already have this playlist saved: My Favorite Songs"
}
```

**Response `200` (replaced):**
```json
{
  "message": "♻️ Existing playlist replaced with: New Playlist"
}
```

---

### 5. Get Playlist

Get the user's saved playlist metadata.

```
GET /api/getPlaylist
Authorization: Bearer <token>
```

**Response `200:`**
```json
{
  "id": 1,
  "playlistTitle": "My Favorite Songs"
}
```

**Response `404:`** — No playlist saved yet

---

### 6. Sync Playlist

Scan YouTube for new songs in the playlist AND start downloading any pending songs.  
**Call this when the app opens or user pulls to refresh.**

```
POST /api/sync
Authorization: Bearer <token>
```

**Request:** (no body)

**Response `200:`**
```json
{
  "message": "Sync started",
  "playlistId": "PLabc123..."
}
```

**Response `404:`**
```json
{ "message": "No playlist saved yet" }
```

**What happens behind the scenes:**
1. YouTube API is queried for all videos in the playlist
2. New videos are inserted into DB with status `Pending`
3. Background download queue starts processing pending songs
4. Downloaded songs go through: upload → URL generation → FCM notification

---

### 7. Get Songs

Returns all songs in the user's playlist with their download status.  
**Poll this after calling `/api/sync` to track download progress.**

```
GET /api/getSongs
Authorization: Bearer <token>
```

**Response `200:`**
```json
{
  "songs": [
    {
      "id": 42,
      "videoId": "dQw4w9WgXcQ",
      "title": "Rick Astley - Never Gonna Give You Up",
      "durationSeconds": 212,
      "thumbnailUrl": "https://i.ytimg.com/vi/dQw4w9WgXcQ/default.jpg",
      "isDownloaded": true,
      "downloadedAt": "2026-07-19T10:30:00Z"
    },
    {
      "id": 43,
      "videoId": "abc123def45",
      "title": "Some New Song",
      "durationSeconds": 180,
      "thumbnailUrl": "https://i.ytimg.com/vi/abc123def45/default.jpg",
      "isDownloaded": false,
      "downloadedAt": null
    }
  ],
  "playlistId": "PLabc123...",
  "playlistTitle": "My Favorite Songs"
}
```

**Response `200` (empty):**
```json
{
  "songs": [],
  "message": "No songs found in the playlist"
}
```

**Status values:** `Pending` → `Processing` → `Completed` | `Failed`

---

### 8. Download Single Song

Queue a specific song for download.

```
POST /api/download
Authorization: Bearer <token>
```

**Query parameter:** `videoId` — the YouTube video ID

**Example:** `POST /api/download?videoId=dQw4w9WgXcQ`

**Response `202:`** — Queued
```json
{
  "jobId": 42,
  "status": "Pending"
}
```

**Response `200:`** — Already downloaded
```json
{
  "videoId": 42,
  "status": "Completed",
  "downloadUrl": "/api/download/file/42"
}
```

**Response `404:`** — Song not found in playlist

**Note:** The actual signed download URL arrives via **FCM push notification**, not in the HTTP response. The `/api/download/file/{id}` path in the response is legacy and non-functional.

---

## App Integration Flow

### On First Launch
```
1. POST /api/auth/login          → get JWT token, store it
2. POST /api/auth/saveFcmToken   → register device for push
3. POST /api/savePlaylist        → user pastes their YouTube playlist URL
4. POST /api/sync                → scan + start downloading
5. Poll GET /api/getSongs        → show progress to user
```

### On Subsequent Launches
```
1. POST /api/auth/login          → get fresh JWT token (or reuse stored)
2. POST /api/sync                → check for new songs + resume downloads
3. Poll GET /api/getSongs        → show updated list + progress
```

### Download Flow
```
1. POST /api/sync                (or POST /api/download for single song)
2. Server downloads via yt-dlp   (song.Status changes: Pending → Processing)
3. Server uploads to cloud storage
4. Server generates signed download URL (48h validity)
5. Server sends FCM push:  { "title": "...", "downloadUrl": "https://..." }
6. App receives FCM → download the file from downloadUrl
7. Song.Status becomes Completed
```

### Polling Strategy
- After calling `/api/sync`, poll `/api/getSongs` every **2–3 seconds**
- Stop polling when all songs are `Completed` or `Failed`
- `isDownloaded` field is `true` when `status == "Completed"`

---

## Error Responses

| Code | Meaning |
|------|---------|
| `401` | Missing/invalid JWT token |
| `404` | Resource not found |
| `429` | Rate limit exceeded (120 req/min global, 5 req/min login) |
| `500` | Internal server error |

---

## Android Integration Notes

### Storing the Token
```kotlin
// Use EncryptedSharedPreferences for the JWT token
// Include in all requests:
//   connection.setRequestProperty("Authorization", "Bearer $token")
```

### Handling FCM
- The server sends a data-only FCM message with these keys:
  - `type`: `"DOWNLOAD_SONG"`
  - `title`: song title
  - `downloadUrl`: signed GCS download URL (valid 48 hours)
- Download the file from `downloadUrl` and save to device storage

### Base URL
- Development: `http://192.168.29.110:5000` (your laptop's local IP)
- Production: `https://api.vdange.site`
