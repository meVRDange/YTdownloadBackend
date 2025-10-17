using System;
using System.Diagnostics;

namespace YTdownloadBackend.Services
{

    public interface IYtDlpService
    {
        Task<bool> DownloadAudioAsync(string videoId);
    }

    public class YtDlpService : IYtDlpService
    {
        private readonly string _ytDlpPath;
        private readonly string _downloadsFolder;
        private readonly string _versionFile;
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(24);

        public YtDlpService()
        {
            string ytDlpFileName = "yt-dlp" + (OperatingSystem.IsWindows() ? ".exe" : "");
            _ytDlpPath = Path.Combine(Directory.GetCurrentDirectory(), "yt-dlp", ytDlpFileName);
            _downloadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "downloads");
            _versionFile = Path.Combine(Directory.GetCurrentDirectory(), "yt-dlp.lastcheck");

            Directory.CreateDirectory(_downloadsFolder);
        }


        public async Task<bool> DownloadAudioAsync(string videoId)
        {
            await EnsureYtDlpUpToDateAsync();

            var result = await RunYtDlpAsync(videoId);

            if (result.StdErr.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("⚠️ yt-dlp reported an ERROR. Trying to update and retry...");
                await UpdateYtDlpAsync();

                result = await RunYtDlpAsync(videoId);
            }

            bool success = result.ExitCode == 0 && !result.StdErr.Contains("ERROR", StringComparison.OrdinalIgnoreCase);

            if (success)
                Console.WriteLine($"✅ Downloaded successfully: {videoId}");
            else
                Console.WriteLine($"❌ Download failed: {videoId}\n{result.StdErr}");

            return success;
        }


        private async Task EnsureYtDlpUpToDateAsync()
        {
            try
            {
                if (File.Exists(_versionFile))
                {
                    var lastCheck = DateTime.Parse(File.ReadAllText(_versionFile));
                    if (DateTime.Now - lastCheck < _checkInterval)
                        return; // Skip check — done recently
                }

                Console.WriteLine("🔎 Checking yt-dlp version...");
                await UpdateYtDlpAsync();
                File.WriteAllText(_versionFile, DateTime.Now.ToString("s"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Version check failed: {ex.Message}");
            }
        }


        private async Task UpdateYtDlpAsync()
        {
            //if (!File.Exists(_ytDlpPath))
            //{
            //    Console.WriteLine("yt-dlp binary missing — downloading fresh...");
            //    await DownloadLatestYtDlpAsync();
            //    return;
            //}

            await RunProcessAsync(_ytDlpPath, "-U");
        }

        //private async Task DownloadLatestYtDlpAsync()
        //{
        //    var url = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/" +
        //              (OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp");

        //    using var http = new HttpClient();
        //    var data = await http.GetByteArrayAsync(url);
        //    await File.WriteAllBytesAsync(_ytDlpPath, data);

        //    if (!OperatingSystem.IsWindows())
        //        Process.Start("chmod", $"+x {_ytDlpPath}");

        //    Console.WriteLine("✅ yt-dlp downloaded successfully.");
        //}


        private async Task<(int ExitCode, string StdOut, string StdErr)> RunYtDlpAsync(string videoId)
        {
            string outputTemplate = Path.Combine(_downloadsFolder, "%(title)s.%(ext)s");
            string args = $"--extract-audio --audio-format mp3 -o \"{outputTemplate}\" https://www.youtube.com/watch?v={videoId}";
            return await RunProcessAsync(_ytDlpPath, args);
        }



        private async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(string file, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = Process.Start(psi);
            string stdOut = await process.StandardOutput.ReadToEndAsync();
            string stdErr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return (process.ExitCode, stdOut, stdErr);
        }

        //public async Task<bool> DownloadAudioAsync(string videoId)
        //{
        //    try { 
        //    string ytDlpFileName = "yt-dlp";
        //    if (OperatingSystem.IsWindows()) ytDlpFileName += ".exe";

        //    string ytDlpPath = Path.Combine(Directory.GetCurrentDirectory(), "yt-dlp", ytDlpFileName);
        //    if (!File.Exists(ytDlpPath))
        //    {
        //        Console.WriteLine($"yt-dlp not found at {ytDlpPath}");
        //        return false;
        //    }

        //    string downloadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "downloads");
        //    Directory.CreateDirectory(downloadsFolder);

        //    string outputTemplate = Path.Combine(downloadsFolder, "%(title)s.%(ext)s");

        //    var psi = new ProcessStartInfo
        //    {
        //        FileName = ytDlpPath,
        //        Arguments = $"--extract-audio --audio-format mp3 -o \"{outputTemplate}\" https://www.youtube.com/watch?v={videoId}",
        //        RedirectStandardOutput = true,
        //        RedirectStandardError = true,
        //        UseShellExecute = false,
        //        CreateNoWindow = false
        //    };


        //        var process = Process.Start(psi);
        //        if (process == null) return false;

        //        string stdOut = await process.StandardOutput.ReadToEndAsync();
        //        string stdErr = await process.StandardError.ReadToEndAsync();

        //        await File.AppendAllTextAsync("yt-dlp-logs.txt",
        //            $"[{DateTime.Now}] {videoId}\n{stdOut}\n{stdErr}\n\n");

        //        await process.WaitForExitAsync();
        //        return process.ExitCode == 0;
        //    } catch(Exception ex)
        //    {
        //        return false;
        //    }
        //}
    }
}
