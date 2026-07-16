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

// Update/download rsx/DepotDownloader
string? rsxVersion = await Utils.GetLatestVersion("r-ex/rsx");
if (rsxVersion is not null && (!File.Exists("Tools\\rsx_nogui.exe") || Tools.Versions.RSXVersion != rsxVersion)) {
    Console.WriteLine($"Downloading rsx {rsxVersion}...");
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

// Get season manifest
Console.Write("Season manifest path (can omit .json extension) [Blank for Manifests\\current.json]: ");
string seasonManifestPath = Console.ReadLine()?.BlankNullable ?? "Manifests\\current.json";

// Add .json if missing
if (!seasonManifestPath.EndsWith(".json"))
    seasonManifestPath += ".json";

// shorthand for manifests folder
if (!File.Exists(seasonManifestPath) && File.Exists(Path.Combine("Manifests", seasonManifestPath)))
    seasonManifestPath = Path.Combine("Manifests", seasonManifestPath);
    
if (!File.Exists(seasonManifestPath)) {
    Console.WriteLine($"Season manifest not found: {seasonManifestPath}");
    Utils.PressAnyKeyToExit(1);
    return;
}

Console.Write("Export or list weapon definitions (export/list/savelist), add +redl to redownload [Blank for export]: ");
string? command = Console.ReadLine()?.Trim().BlankNullable;
bool redownload = command?.EndsWith("+redl") == true;
if (redownload)
    command = command?.Replace("+redl", "");
command = command?.Trim().BlankNullable ?? "export";

JsonDocument seasonManifest = JsonDocument.Parse(File.ReadAllText(seasonManifestPath));

string? steamManifestId;

// check manifest for steam manifest id
if (seasonManifest.RootElement.TryGetProperty("SteamManifestID", out JsonElement steamManifestIdElement) && steamManifestIdElement.TryGetInt64(out long manifestIdLong)) {
    steamManifestId = manifestIdLong.ToString();
    Console.WriteLine($"Using Manifest ID (https://steamdb.info/depot/1172471/manifests/): {steamManifestId}");
}
else {
    // prompt for steam manifest id
    Console.Write("Manifest ID (https://steamdb.info/depot/1172471/manifests/) [Blank for latest]: ");
    string? steamManifestIdInput = Console.ReadLine()?.Trim();

    // use api to get latest steam manifest id
    if (string.IsNullOrEmpty(steamManifestIdInput)) {
        HttpResponseMessage response = await Utils.HttpClient.GetAsync("https://api.steamcmd.net/v1/info/1172470");
        response.EnsureSuccessStatusCode();

        Stream stream = await response.Content.ReadAsStreamAsync();
        JsonDocument json = await JsonDocument.ParseAsync(stream);
        try {
            steamManifestId = json.RootElement
                .GetProperty("data").GetProperty("1172470").GetProperty("depots").GetProperty("1172471")
                .GetProperty("manifests").GetProperty("public").GetProperty("gid").GetString();

            Console.WriteLine($"Using latest manifest ID: {steamManifestId}");
        }
        catch (Exception ex) {
            Utils.PressAnyKeyToExit(1, $"Failed to get latest manifest ID: {ex.Message}");
            return;
        }
    }
    else if (ulong.TryParse(steamManifestIdInput, out _)) { // check if id is a number (i think its supposed to be a number)
        steamManifestId = steamManifestIdInput;
        Console.WriteLine($"Using manifest ID: {steamManifestId}");
    }
    else {
        Utils.PressAnyKeyToExit(1, "Invalid manifest ID");
        return;
    }
}
if (string.IsNullOrEmpty(steamManifestId)) {
    Utils.PressAnyKeyToExit(1, "Invalid manifest ID");
    return;
}

Directory.CreateDirectory("Tools\\downloads");

// create filelist for depot downloader (only patch master and common rpak(s) are needed)
"""
paks/Win64/patch_master.rpak
paks/Win64/common.rpak
regex:^paks/Win64/common(?:\(\d+\))?\.rpak$
""".SaveToFile("Tools\\downloads\\filelist.txt");

// delete existing download
if (redownload && Directory.Exists($"Tools\\downloads\\{steamManifestId}")) {
    Console.WriteLine($"Redownloading paks from manifest {steamManifestId}...");
    if (!Utils.TryClearDirectory($"Tools\\downloads\\{steamManifestId}", true))
        Utils.PressAnyKeyToExit(1);
}

// download paks if not already downloaded
if (!Directory.Exists($"Tools\\downloads\\{steamManifestId}"))
    Tools.RunDepotDownloader(steamManifestId);

string commonRpakPath = $"{Environment.CurrentDirectory}\\Tools\\downloads\\{steamManifestId}\\paks\\Win64\\common.rpak";

if (!File.Exists(commonRpakPath))
    Utils.PressAnyKeyToExit(1, $"common.rpak not found: {commonRpakPath}");

if (command == "list" || command == "savelist") { // list weapon defs
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

    if (command == "savelist") {
        weaponDefs.SaveToFile($"Output\\weapon_definitions_{seasonManifest.RootElement.GetProperty("ID").GetString()}_{steamManifestId}.txt");
    }
    else {
        Console.WriteLine("Weapon Definitions:");
        weaponDefs.ToList().ForEach(Console.WriteLine);
    }
}
else if (command == "export") { // export weapon defs
    if (Directory.Exists("Tools\\exported_files")) {
        Console.WriteLine("Clearing exported_files...");
        if (!Utils.TryClearDirectory("Tools\\exported_files"))
            Utils.PressAnyKeyToExit(1, "Failed to clear exported_files directory.");
    }
    Console.WriteLine($"Extracting weapon definitions from {commonRpakPath}...");

    Tools.RunRSX(commonRpakPath);

    // load season manifest
    SeasonInfo? season = JsonSerializer.Deserialize(File.ReadAllText(seasonManifestPath), SeasonJsonContext.Default.SeasonInfo);
    if (season is null) {
        Console.WriteLine("Failed to load season manifest");
        Utils.PressAnyKeyToExit(1);
        return;
    }

    // season id shouldnt have slashes cause itd mess up the folder structure
    if (season.ID.Contains('\\') || season.ID.Contains('/'))
        Utils.PressAnyKeyToExit(1, $"Invalid season ID: {season.ID}");
    
    string seasonOutputDir = "Output\\" + season.ID;

    if (!Utils.TryClearDirectory(seasonOutputDir))
        Utils.PressAnyKeyToExit(1, $"Failed to clear {seasonOutputDir}");
    Directory.CreateDirectory($"{seasonOutputDir}\\patterns");
    Directory.CreateDirectory($"{seasonOutputDir}\\weapons");

    List<string> missingAssets = [];
    List<string> missingWeaponDefs = [];
    List<string> invalidWeaponMods = [];
    foreach (KeyValuePair<string, WeaponInfo> weapon in season.Weapons) {
        // check that all weapon defs in manifest exist
        if (string.IsNullOrEmpty(weapon.Value.Asset))
            missingAssets.Add(weapon.Key);
        
        string weaponDefPath = $"Tools\\exported_files\\weapon\\{weapon.Value.Asset}.txt";
        if (File.Exists(weaponDefPath)) {
            // check that there are no invalid mods
            List<string> lines = [.. File.ReadAllLines(weaponDefPath).Where(line => !line.TrimStart().StartsWith("//"))];

            // list of all mods used by the weapon modes
            string[] usedMods = weapon.Value.Modes?.SelectMany(m => m.Mods).Distinct().ToArray() ?? [];
            if (usedMods.Length > 0) {
                // check if the mods section has all the mods listed in the manifest
                List<string> modLines = lines[lines.FindIndex(line => line.Trim().StartsWith("\"Mods\""))..];

                invalidWeaponMods.AddRange(
                    usedMods.Where(mod => !modLines.Any(line => line.Contains($"\"{mod}\""))).Select(m => $"{weapon.Key}: {m}")
                );
            }
            
            if (weapon.Value.Modes?.Any(m => m.Type != WeaponModeType.Base && m.Name is null) == true)
                invalidWeaponMods.Add(weapon.Key);
        }
        else {
            missingWeaponDefs.Add(weapon.Key);
        }
    }

    // show all errors and exit if any
    if (missingAssets.Count > 0) {
        Console.WriteLine("Missing weapon assets in manifest:");
        missingAssets.ForEach(d => Console.WriteLine(" " + d));
    }
    if (missingWeaponDefs.Count > 0) {
        Console.WriteLine("Invalid weapon definitions:");
        missingWeaponDefs.ForEach(d => Console.WriteLine(" " + d));
    }
    if (invalidWeaponMods.Count > 0) {
        Console.WriteLine("Invalid weapon mods:");
        invalidWeaponMods.ForEach(d => Console.WriteLine(" " + d));
    }
    if (missingAssets.Count + missingWeaponDefs.Count + invalidWeaponMods.Count > 0)
        Utils.PressAnyKeyToExit(1);

    // now export after everything is checked
    foreach (KeyValuePair<string, WeaponInfo> weapon in season.Weapons) {
        string weaponDefPath = $"Tools\\exported_files\\weapon\\{weapon.Value.Asset}.txt";
        List<string> lines = [.. File.ReadAllLines(weaponDefPath).Where(line => !line.TrimStart().StartsWith("//"))]; // remove comments

        // its in vdf (Valve Data Format) from what i can tell but idk if the file extension should be .vdf or .txt
        string path = $"{seasonOutputDir}\\weapons\\{weapon.Key}.vdf";
        Console.WriteLine($"Saving {path} ({weapon.Value.Asset})");
        lines.SaveToFile(path);
    }

    // export blast/spread patterns and viewkick (recoil) patterns
    string[] patternDefs = ["blast_patterns", "viewkick_patterns"];
    foreach (string patternDef in patternDefs) {
        string[] lines = [.. File.ReadAllLines($"Tools\\exported_files\\weapon\\{patternDef}.txt").Where(line => !line.TrimStart().StartsWith("//"))];
        
        Console.WriteLine($"Saving {seasonOutputDir}\\patterns\\{patternDef}.vdf");
        lines.SaveToFile($"{seasonOutputDir}\\patterns\\{patternDef}.vdf");
    }

    // remove asset paths from manifest since theyre only needed for the export process or whatever
    foreach (WeaponInfo weapon in season.Weapons.Values) weapon.Asset = null;

    // save season manifest with all needed info (no asset names, setting CoreWeapon to true if not included in the human readable/input manifest)
    JsonSerializer.Serialize(season, SeasonJsonContext.Default.SeasonInfo).SaveToFile($"{seasonOutputDir}\\manifest.json");
}

Utils.PressAnyKeyToExit();

namespace ApexWeaponExporter {
    public class Tools {
        public static DownloadedVersions Versions { get; } = DownloadedVersions.Load();

        public static void RunRSX(string commonRpakPath) {
            Process rsx = new() {
                StartInfo = new() {
                    FileName = "Tools\\rsx_nogui.exe",
                    Arguments = $"\"{commonRpakPath}\" -export --exportdir exported_files --exporttypes wepn",
                    WorkingDirectory = "Tools",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };

            static void OutputHandler(object sender, DataReceivedEventArgs e) {
                // hide the 'loaded with no vertex data' lines cause we dont need starpaks and its model data
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
            // check for saved username if already logged in once or prompt qr code login if not
            string authArg = "-qr";
            if (File.Exists("Tools\\username.txt")) {
                string username = File.ReadAllText("Tools\\username.txt").Trim();
                if (!string.IsNullOrEmpty(username))
                    authArg = $"-username {username} -remember-password";
            }

            Process depotDownloader = new() {
                StartInfo = new() {
                    FileName = "Tools\\DepotDownloader.exe",
                    WorkingDirectory = "Tools",
                    Arguments = $"{authArg} -app 1172470 -depot 1172471 -filelist downloads\\filelist.txt -manifest {manifestId} -dir downloads\\{manifestId}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };

            // im not sure if stderr is used but ill cover it just incase
            depotDownloader.ErrorDataReceived += (_, e) => {
                if (!string.IsNullOrEmpty(e.Data)) {
                    Console.WriteLine("[DepotDownloader] " + e.Data);

                    if (e.Data.Contains("Failed to authenticate with Steam")) {
                        File.Delete("Tools\\username.txt");
                        if (!depotDownloader.HasExited)
                            depotDownloader.Kill(true);
                        Utils.PressAnyKeyToExit(1, "DepotDownloader failed to authenticate with Steam, restart to prompt QR login");
                    }
                }
            };

            depotDownloader.Start();
            depotDownloader.BeginErrorReadLine();

            // read stdout stream because the "enter account password" prompt doesnt use a newline so the normal OutputDataReceived event doesnt read it
            string line = string.Empty;
            while (!depotDownloader.StandardOutput.EndOfStream) {
                if (line.Length == 0)
                    Console.Write("[DepotDownloader] ");
                char character = (char)depotDownloader.StandardOutput.Read();
                line += character;
                Console.Write(character);

                if (line.Contains("Failed to authenticate with Steam") || line.Contains("Enter account password for")) {
                    File.Delete("Tools\\username.txt");
                    if (!depotDownloader.HasExited)
                        depotDownloader.Kill(true);
                    Utils.PressAnyKeyToExit(1, "\rDepotDownloader failed to authenticate with Steam, restart to prompt QR login");
                }

                if (character == '\n') {
                    if (!string.IsNullOrEmpty(line)) {
                        Match usernameRegex = Regex.Match(line, @"-username\s+(?<username>\S+)\s+-remember-password");
                        if (usernameRegex.Success)
                            usernameRegex.Groups["username"].Value.Trim().SaveToFile("Tools\\username.txt");
                    }
                    line = string.Empty;
                }
            }

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

            public void Save() => JsonSerializer.Serialize(this, VersionsJsonContext.Default.DownloadedVersions).SaveToFile("Tools\\versions.json");
        }
        
    }

    public static class Utils {
        public static HttpClient HttpClient { get; } = new() {
            Timeout = TimeSpan.FromSeconds(30),
            DefaultRequestHeaders = {
                { "User-Agent", "ApexWeaponExporter" }
            }
        };

        public static void PressAnyKeyToExit(int exitCode = 0, string? message = null) {
            if (message is not null)
                    Console.WriteLine(message);
            if (!Console.IsInputRedirected) {
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
                .First(predicate) // get the first asset that matches the filter thing
                .url;
            
            return await DownloadWithProgress(url);
        }

        public static void SaveFileFromZip(byte[] zipData, Func<ZipArchiveEntry, bool> predicate, string extractPath) {
            using MemoryStream zipStream = new(zipData);
            using ZipArchive archive = new(zipStream);
            if (!Directory.Exists(Path.GetDirectoryName(extractPath)))
                Directory.CreateDirectory(Path.GetDirectoryName(extractPath)!);
            archive.Entries.First(predicate).ExtractToFile(extractPath, true); // only save the first file that matches the filter thing
        }

        public static async Task<byte[]> DownloadWithProgress(string url) {
            using HttpResponseMessage response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;
            await using Stream input = await response.Content.ReadAsStreamAsync();
            using MemoryStream output = new();

            byte[] buffer = new byte[81920];
            long downloaded = 0;

            for (int read; (read = await input.ReadAsync(buffer)) > 0;) {
                await output.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                if (Console.IsOutputRedirected)
                    Console.WriteLine($"{downloaded * 100 / total}% ({downloaded / 1048576d:F1}/{total / 1048576d:F1} MB)");
                else
                    Console.Write($"\r{downloaded * 100 / total}% ({downloaded / 1048576d:F1}/{total / 1048576d:F1} MB)\x1b[K");
            }
            
            Console.Write($"\rDownloaded ({downloaded / 1048576d:F1} MB)\x1b[K");

            Console.WriteLine();
            return output.ToArray();
        }

        extension(string val) {
            public string? EmptyNullable => string.IsNullOrEmpty(val) ? null : val;
            public string? BlankNullable => string.IsNullOrWhiteSpace(val) ? null : val;

            public void SaveToFile(string path) {
                if (!Directory.Exists(Path.GetDirectoryName(path)))
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, val);
            }
        }

        extension(IEnumerable<string> val) {
            public void SaveToFile(string path) {
                if (!Directory.Exists(Path.GetDirectoryName(path)))
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllLines(path, val);
            }
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
        public string? Name { get => field?.BlankNullable; init; }

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