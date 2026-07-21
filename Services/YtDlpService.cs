using System;
using System.Diagnostics;
using System.Linq;

namespace YTdownloadBackend.Services
{

    public interface IYtDlpService
    {
        Task<string?> DownloadAudioAsync(string videoId, string username);
    }

    public class YtDlpService : IYtDlpService
    {
        private readonly string _ytDlpPath;
        private readonly string _downloadsFolder;
        private readonly string _versionFile;
        private readonly string _cookiesFile;
        private readonly string _denoPath;
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(24);

        public YtDlpService()
        {
            string ytDlpFileName = "yt-dlp.exe";

            _ytDlpPath = Path.Combine(Directory.GetCurrentDirectory(), "yt-dlp", ytDlpFileName);
            _downloadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "downloads");
            _versionFile = Path.Combine(Directory.GetCurrentDirectory(), "yt-dlp.lastcheck");
            _cookiesFile = Path.Combine(Directory.GetCurrentDirectory(), "yt-dlp", "cookies.txt");

            // Find deno for JS challenge solving
            _denoPath = FindDeno();

            Directory.CreateDirectory(_downloadsFolder);
        }

        private static string FindDeno()
        {
            // 1. Try PATH first
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "deno",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);
                if (proc?.ExitCode == 0)
                    return "deno";
            }
            catch { }

            // 2. Search WinGet packages folder for deno
            try
            {
                var wingetDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\WinGet\Packages");
                if (Directory.Exists(wingetDir))
                {
                    var denoDir = Directory.GetDirectories(wingetDir, "DenoLand.Deno_*").FirstOrDefault();
                    if (denoDir != null)
                    {
                        var denoExe = Path.Combine(denoDir, "deno.exe");
                        if (File.Exists(denoExe))
                            return denoExe;
                    }
                }
            }
            catch { }

            // 3. Try user .deno folder
            var userDeno = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @".deno\bin\deno.exe");
            if (File.Exists(userDeno))
                return userDeno;

            return "deno"; // fallback
        }


        public async Task<string?> DownloadAudioAsync(string videoId, string username)
        {
            await EnsureYtDlpUpToDateAsync();

            var result = await RunYtDlpAsync(videoId, username);

            if (result.StdErr.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("⚠️ yt-dlp reported an ERROR. Trying to update and retry...");
                await UpdateYtDlpAsync();

                result = await RunYtDlpAsync(videoId, username);
            }

            bool success = result.ExitCode == 0 && !result.StdErr.Contains("ERROR", StringComparison.OrdinalIgnoreCase);

            if (success)
            {
                Console.WriteLine($"✅ Downloaded successfully: {videoId}");
                return result.Filename;
            }
            else
            {
                Console.WriteLine($"❌ Download failed: {videoId}\n{result.StdErr}");
                return null;
            }
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


        private async Task<(int ExitCode, string StdOut, string StdErr, string? Filename)> RunYtDlpAsync(string videoId, string username)
        {
            string outputTemplate = Path.Combine(_downloadsFolder, username, "%(title)s.%(ext)s");
            string cookiesArg = File.Exists(_cookiesFile) ? $"--cookies \"{_cookiesFile}\" " : "";
            string args = $"--js-runtimes \"{_denoPath}\" --restrict-filenames {cookiesArg}--extract-audio --audio-format mp3 -o \"{outputTemplate}\" https://www.youtube.com/watch?v={videoId}";
            var result = await RunProcessAsync(_ytDlpPath, args);
            
            // Parse filename from StdOut (assuming yt-dlp outputs something like "[download] Destination: /path/to/file.mp3")
            string? filename = null;
            var lines = result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.Contains("Destination:", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split("Destination:", StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1)
                    {
                        filename = parts[1].Trim().Replace(".webm", ".mp3");
                        break;
                    }
                } else if(line.Contains("already been downloaded", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(" ", StringSplitOptions.RemoveEmptyEntries);
                        filename = parts[1].Trim().Replace(".webm", ".mp3");
                        break;
                    
                }
            }
            
            return (result.ExitCode, result.StdOut, result.StdErr, filename);
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
    }
}
