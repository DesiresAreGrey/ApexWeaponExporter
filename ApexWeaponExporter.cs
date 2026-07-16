using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ApexWeaponExporter;

AppDomain.CurrentDomain.UnhandledException += (_, e) => {
    Console.WriteLine($"Unhandled exception: {e.ExceptionObject}");
    Utils.PressAnyKeyToExit(1);
};

string? rsxVersion = await Utils.GetLatestVersion("r-ex/rsx");
if (rsxVersion is not null && (!File.Exists("Tools\\rsx_nogui.exe") || Tools.Versions.RSXVersion != rsxVersion)) {
    Console.WriteLine($"Downloading RSX {rsxVersion}...");
    Utils.SaveFileFromZip(await Utils.DownloadGithubRelease("r-ex/rsx", v => Path.GetExtension(v.name) == ".zip"),
        f => Path.GetFileName(f.FullName).EndsWith("rsx_nogui.exe"), "Tools\\rsx_nogui.exe");
    Tools.Versions.RSXVersion = rsxVersion;
}
string? depotDownloaderVersion = (await Utils.GetLatestVersion("SteamRE/DepotDownloader"))?.Replace("DepotDownloader_", "");
if (depotDownloaderVersion is not null && (!File.Exists("Tools\\DepotDownloader.exe") || Tools.Versions.DepotDownloaderVersion != depotDownloaderVersion)) {
    Console.WriteLine($"Downloading DepotDownloader {depotDownloaderVersion}...");
    Utils.SaveFileFromZip(await Utils.DownloadGithubRelease("SteamRE/DepotDownloader", v => v.name.EndsWith("-windows-x64.zip")),
        f => Path.GetFileName(f.FullName).EndsWith("DepotDownloader.exe"), "Tools\\DepotDownloader.exe");
    Tools.Versions.DepotDownloaderVersion = depotDownloaderVersion;
}

Console.Write("Season json path (can omit .json extension) [Blank for season.json]: ");
string seasonInfoPath = Console.ReadLine()?.BlankNullable ?? "season.json";

if (!seasonInfoPath.EndsWith(".json"))
    seasonInfoPath += ".json";
if (!File.Exists(seasonInfoPath) && File.Exists(Path.Combine("Old", seasonInfoPath)))
    seasonInfoPath = Path.Combine("Old", seasonInfoPath);
if (!File.Exists(seasonInfoPath)) {
    Console.WriteLine($"Season json not found: {seasonInfoPath}");
    Utils.PressAnyKeyToExit(1);
    return;
}

Console.Write("Export or list weapon definitions (export/list/savelist), add +redl to redownload [Blank for export]: ");
string? input = Console.ReadLine()?.Trim().BlankNullable;
bool redownload = input?.EndsWith("+redl") == true;
if (redownload)
    input = input?.Replace("+redl", "");
input = input?.Trim().BlankNullable ?? "export";

JsonDocument seasonJson = JsonDocument.Parse(File.ReadAllText(seasonInfoPath));

string? manifestId;
if (seasonJson.RootElement.TryGetProperty("SteamManifestID", out JsonElement manifestIdElement) && manifestIdElement.TryGetInt64(out long manifestIdLong)) {
    manifestId = manifestIdLong.ToString();
    Console.WriteLine($"Using Manifest ID (https://steamdb.info/depot/1172471/manifests/): {manifestId}");
}
else {
    Console.Write("Manifest ID (https://steamdb.info/depot/1172471/manifests/) [Blank for latest]: ");
    string? manifestIdInput = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(manifestIdInput)) {
        HttpResponseMessage response = await Utils.HttpClient.GetAsync("https://api.steamcmd.net/v1/info/1172470");
        response.EnsureSuccessStatusCode();

        Stream stream = await response.Content.ReadAsStreamAsync();
        JsonDocument json = await JsonDocument.ParseAsync(stream);
        try {
            manifestId = json.RootElement
                .GetProperty("data").GetProperty("1172470").GetProperty("depots").GetProperty("1172471")
                .GetProperty("manifests").GetProperty("public").GetProperty("gid").GetString();

            Console.WriteLine($"Using latest manifest ID: {manifestId}");
        }
        catch (Exception ex) {
            Console.WriteLine($"Failed to get latest manifest ID: {ex.Message}");
            Utils.PressAnyKeyToExit(1);
            return;
        }
    }
    else if (ulong.TryParse(manifestIdInput, out _)) {
        manifestId = manifestIdInput;
        Console.WriteLine($"Using manifest ID: {manifestId}");
    }
    else {
        Console.WriteLine("Invalid manifest ID");
        Utils.PressAnyKeyToExit(1);
        return;
    }
}
if (string.IsNullOrEmpty(manifestId)) {
    Console.WriteLine("Invalid manifest ID");
    Utils.PressAnyKeyToExit(1);
    return;
}

Directory.CreateDirectory("Tools\\depot");
if (File.Exists("Tools\\depot\\filelist.txt"))
    File.Delete("Tools\\depot\\filelist.txt");
File.WriteAllText("Tools\\depot\\filelist.txt", """
    paks/Win64/patch_master.rpak
    paks/Win64/common.rpak
    regex:^paks/Win64/common(?:\(\d+\))?\.rpak$
    """
);

if (redownload && Directory.Exists($"Tools\\depot\\{manifestId}")) {
    Console.WriteLine($"Redownloading manifest {manifestId}...");
    if (!Utils.TryClearDirectory($"Tools\\depot\\{manifestId}", true))
        Utils.PressAnyKeyToExit(1);
}

if (!Directory.Exists($"Tools\\depot\\{manifestId}")) {
    Tools.RunDepotDownloader(manifestId);
}

string commonRpakPath = $"{Environment.CurrentDirectory}\\Tools\\depot\\{manifestId}\\paks\\Win64\\common.rpak";
if (!File.Exists(commonRpakPath)) {
    Console.WriteLine($"common.rpak not found: {commonRpakPath}");
    Utils.PressAnyKeyToExit(1);
}

if (input == "list" || input == "savelist") {
    if (Directory.Exists("Tools\\exported_files")) {
        Console.WriteLine("Clearing exported_files...");
        if (!Utils.TryClearDirectory("Tools\\exported_files"))
            Utils.PressAnyKeyToExit(1);
    }
    Console.WriteLine($"Extracting weapon definitions from {commonRpakPath}...");
    Tools.RunRSX(commonRpakPath);

    string[] weaponDefs = [.. Directory.GetFiles("Tools\\exported_files\\weapon", "*.txt").Select(d => Path.GetFileNameWithoutExtension(d) ?? d.Replace(".txt", ""))];
    if (!Utils.TryClearDirectory("Tools\\exported_files"))
        Utils.PressAnyKeyToExit(1, "Failed to clear exported_files directory.");

    if (input == "savelist") {
        if (File.Exists("weapon_definitions.txt"))
            File.Delete("weapon_definitions.txt");
        File.WriteAllLines("weapon_definitions.txt", weaponDefs);
    }
    else {
        Console.WriteLine("Weapon Definitions:");
        weaponDefs.ToList().ForEach(Console.WriteLine);
        Console.WriteLine("Press enter to exit...");
        Console.ReadLine();
        return;
    }
    return;
}
else if (input == "export") {
    if (Directory.Exists("Tools\\exported_files")) {
        Console.WriteLine("Clearing exported_files...");
        if (!Utils.TryClearDirectory("Tools\\exported_files"))
            Utils.PressAnyKeyToExit(1, "Failed to clear exported_files directory.");
    }
    Console.WriteLine($"Extracting weapon definitions from {commonRpakPath}...");

    Tools.RunRSX(commonRpakPath);

    SeasonInfo? season = JsonSerializer.Deserialize(File.ReadAllText(seasonInfoPath), SeasonJsonContext.Default.SeasonInfo);
    if (season is null) {
        Console.WriteLine("Failed to load season json");
        Utils.PressAnyKeyToExit(1);
        return;
    }

    string seasonOutputDir = "Output\\" + season.ID;

    if (!Utils.TryClearDirectory(seasonOutputDir))
        Utils.PressAnyKeyToExit(1);
    Directory.CreateDirectory($"{seasonOutputDir}\\patterns");
    Directory.CreateDirectory($"{seasonOutputDir}\\weapons");

    List<string> missingWeaponDefs = [];
    List<string> invalidWeaponMods = [];
    foreach (KeyValuePair<string, WeaponInfo> weapon in season.Weapons) {
        if (string.IsNullOrEmpty(weapon.Value.Asset))
            Utils.PressAnyKeyToExit(1, $"Weapon {weapon.Key} has no asset defined.");
        
        string weaponDefPath = $"Tools\\exported_files\\weapon\\{weapon.Value.Asset}.txt";
        if (File.Exists(weaponDefPath)) {
            List<string> lines = [.. File.ReadAllLines(weaponDefPath).Where(line => !line.TrimStart().StartsWith("//"))];

            string[] usedMods = weapon.Value.Modes?.SelectMany(m => m.Mods).Distinct().ToArray() ?? [];
            if (usedMods.Length > 0) {
                string[] modLines = [.. lines[lines.FindIndex(line => line.Trim().StartsWith("\"Mods\""))..]];

                IEnumerable<string> invalidMods = usedMods.Where(mod => !modLines.Any(line => line.Contains($"\"{mod}\"")));
                if (invalidMods.Any())
                    invalidWeaponMods.Add($"{weapon.Key} ({weapon.Value.Asset}): {string.Join(", ", invalidMods)}");
            }
            
        }
        else {
            missingWeaponDefs.Add($"{weapon.Key} ({weapon.Value.Asset})");
        }
    }
    if (missingWeaponDefs.Count > 0) {
        Console.WriteLine("Invalid weapon definitions:");
        missingWeaponDefs.ForEach(d => Console.WriteLine(" " + d));
    }
    if (invalidWeaponMods.Count > 0) {
        Console.WriteLine("Invalid weapon mods:");
        invalidWeaponMods.ForEach(d => Console.WriteLine(" " + d));
    }
    if (missingWeaponDefs.Count > 0 || invalidWeaponMods.Count > 0)
        Utils.PressAnyKeyToExit(1);

    foreach (KeyValuePair<string, WeaponInfo> weapon in season.Weapons) {
        string weaponDefPath = $"Tools\\exported_files\\weapon\\{weapon.Value.Asset}.txt";
        List<string> lines = [.. File.ReadAllLines(weaponDefPath).Where(line => !line.TrimStart().StartsWith("//"))];

        string path = $"{seasonOutputDir}\\weapons\\{weapon.Key}.vdf";
        Console.WriteLine($"Saving {path} ({weapon.Value.Asset})");
        File.WriteAllLines(path, lines);
    }

    string[] patternDefs = ["viewkick_patterns", "blast_patterns"];
    foreach (string patternDef in patternDefs) {
        string[] lines = [.. File.ReadAllLines($"Tools\\exported_files\\weapon\\{patternDef}.txt").Where(line => !line.TrimStart().StartsWith("//"))];
        
        Console.WriteLine($"Saving {seasonOutputDir}\\patterns\\{patternDef}.vdf");
        File.WriteAllLines($"{seasonOutputDir}\\patterns\\{patternDef}.vdf", lines);
    }

    season.Weapons.Values.ToList().ForEach(w => w.Asset = null);
    File.WriteAllText($"{seasonOutputDir}\\manifest.json", JsonSerializer.Serialize(season, SeasonJsonContext.Default.SeasonInfo));

    Utils.PressAnyKeyToExit();
}

namespace ApexWeaponExporter {
    public class Tools {
        public static DownloadedVersions Versions { get; } = DownloadedVersions.Load();

        public static void RunRSX(string commonRpakPath) {
            Process rsx = new() {
                StartInfo = new() {
                    FileName = "Tools\\rsx_nogui.exe",
                    Arguments = $"\"{commonRpakPath}\" -export --exporttypes wepn",
                    WorkingDirectory = "Tools",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };

            static void OutputHandler(object sender, DataReceivedEventArgs e) {
                if (!string.IsNullOrEmpty(e.Data) && !e.Data.Contains("loaded with no vertex data"))
                    Console.WriteLine("[rsx] " + e.Data);
            }

            rsx.OutputDataReceived += OutputHandler;
            rsx.ErrorDataReceived += OutputHandler;

            rsx.Start();
            rsx.BeginOutputReadLine();
            rsx.BeginErrorReadLine();
            rsx.WaitForExit();
        }

        public static void RunDepotDownloader(string manifestId) {
            string authArg = "-qr -remember-password";
            if (File.Exists("Tools\\username.txt")) {
                string username = File.ReadAllText("Tools\\username.txt").Trim();
                if (!string.IsNullOrEmpty(username))
                    authArg = $"-username {username} -remember-password";
            }

            Process depotDownloader = new() {
                StartInfo = new() {
                    FileName = "Tools\\DepotDownloader.exe",
                    WorkingDirectory = "Tools",
                    Arguments = $"{authArg} -app 1172470 -depot 1172471 -filelist depot\\filelist.txt -manifest {manifestId} -dir depot\\{manifestId}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };

            static void OutputHandler(object sender, DataReceivedEventArgs e) {
                if (!string.IsNullOrEmpty(e.Data)) {
                    Console.WriteLine(e.Data);

                    Match usernameRegex = Regex.Match(e.Data, @"-username\s+(?<username>\S+)\s+-remember-password");
                    if (usernameRegex.Success)
                        File.WriteAllText("Tools\\username.txt", usernameRegex.Groups["username"].Value);

                    if (e.Data.Contains("Failed to authenticate with Steam"))
                        File.Delete("Tools\\username.txt");
                }
            }

            depotDownloader.OutputDataReceived += OutputHandler;
            depotDownloader.ErrorDataReceived += OutputHandler;

            depotDownloader.Start();
            depotDownloader.BeginOutputReadLine();
            depotDownloader.BeginErrorReadLine();
            depotDownloader.WaitForExit();
        }

        public class DownloadedVersions {
            public string? RSXVersion { 
                get; set {
                    field = value;
                    Save();
                } 
            }
            public string? DepotDownloaderVersion { 
                get; set {
                    field = value;
                    Save();
                } 
            }

            public static DownloadedVersions Load() {
                if (!File.Exists("Tools\\versions.json"))
                    return new();

                return JsonSerializer.Deserialize(File.ReadAllText("Tools\\versions.json"), VersionsJsonContext.Default.DownloadedVersions) ?? new();
            }

            public void Save() {
                File.WriteAllText("Tools\\versions.json", JsonSerializer.Serialize(this, VersionsJsonContext.Default.DownloadedVersions));
            }
        }
        
    }

    public static class Utils {
        public static HttpClient HttpClient { get; } = new() {
            Timeout = TimeSpan.FromSeconds(30),
            DefaultRequestHeaders = {
                { "User-Agent", "SeasonExport" }
            }
        };

        public static void PressAnyKeyToExit(int exitCode = 0, string? message = null) {
            if (!Console.IsInputRedirected) {
                if (message is not null)
                    Console.WriteLine(message);
                Console.Write("Press any key to exit...");
                Console.ReadKey();
            }
            Environment.Exit(exitCode);
        }

        public static bool TryClearDirectory(string path, bool remove = false) {
            try {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
                if (!remove)
                    Directory.CreateDirectory(path);
                return true;
            }
            catch (Exception ex) {
                Console.WriteLine($"Failed to clear directory {path}: {ex.Message}");
                return false;
            }
        }

        public static async Task<string?> GetLatestVersion(string repo) {
            try {
                using JsonDocument json = JsonDocument.Parse(await HttpClient.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest"));

                return json.RootElement.GetProperty("tag_name").GetString()!;
            }
            catch {
                Console.WriteLine($"Unable to check {repo} for updates");
                return null;
            }
        }

        public static async Task<byte[]> DownloadGithubRelease(string repo, Func<(string name, string url), bool> predicate) {
            using JsonDocument json = JsonDocument.Parse(await HttpClient.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest"));

            string url = json.RootElement.GetProperty("assets").EnumerateArray()
                .Select(a => (name: a.GetProperty("name").GetString()!, url: a.GetProperty("browser_download_url").GetString()!))
                .First(predicate)
                .url;
            
            return await DownloadWithProgress(url);
        }

        public static void SaveFileFromZip(byte[] zipData, Func<ZipArchiveEntry, bool> predicate, string extractPath) {
            using MemoryStream zipStream = new(zipData);
            using ZipArchive archive = new(zipStream);
            if (!Directory.Exists(Path.GetDirectoryName(extractPath)))
                Directory.CreateDirectory(Path.GetDirectoryName(extractPath)!);
            archive.Entries.First(predicate).ExtractToFile(extractPath, true);
        }

        public static async Task<byte[]> DownloadWithProgress(string url) {
            using HttpResponseMessage response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            long? total = response.Content.Headers.ContentLength;
            await using Stream input = await response.Content.ReadAsStreamAsync();
            using MemoryStream output = new();

            byte[] buffer = new byte[81920];
            long downloaded = 0;

            for (int read; (read = await input.ReadAsync(buffer)) > 0;) {
                await output.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                Console.Write($"\r{downloaded * 100 / total}% ({downloaded / 1048576d:F1}/{total / 1048576d:F1} MB)\x1b[K");
            }
            Console.Write($"\rDownloaded ({downloaded / 1048576d:F1} MB)\x1b[K");

            Console.WriteLine();
            return output.ToArray();
        }

        extension(string val) {
            public string? EmptyNullable => string.IsNullOrEmpty(val) ? null : val;
            public string? BlankNullable => string.IsNullOrWhiteSpace(val) ? null : val;
        }
    }

    public sealed class SeasonInfo {
        public required string Name { get; init; }
        public required string FullName { get; init; }
        public required int Season { get; init; }
        public required int Split { get; init; }
        public required string Title { get; init; }

        public required string ID { get; init; }
        public required DateOnly ReleaseDate { get; init; }

        public required Dictionary<string, WeaponInfo> Weapons { get; init; }
    }

    public sealed class WeaponInfo {
        public required string Name { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Asset { get; set; }

        public bool CoreWeapon { get; init; } = true;

        public WeaponMode[]? Modes { get => field ?? []; init; }
    }

    public sealed class WeaponMode {
        public required string Name { get; init; }

        public string[] Mods { get; init; } = [];

        public required WeaponModeType Type { get; init; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter<WeaponModeType>))]
    public enum WeaponModeType {
        Base,
        FiringMode,
        Akimbo,
        Hopup,
        HopupAkimbo,
        Energized,
    }

    [JsonSerializable(typeof(SeasonInfo))]
    public partial class SeasonJsonContext : JsonSerializerContext;
    [JsonSerializable(typeof(Tools.DownloadedVersions))]
    public partial class VersionsJsonContext : JsonSerializerContext;
}