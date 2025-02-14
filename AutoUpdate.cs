using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Config;
using System.Text.Json.Serialization;
using System.Net.Http;
using System.Threading.Tasks;
using System;

namespace AutoUpdate;

public class AutoUpdateConfig : BasePluginConfig
{
    [JsonPropertyName("CheckIntervalMinutes")]
    public int CheckIntervalMinutes { get; set; } = 30; // Check for updates every 30 minutes

    [JsonPropertyName("AutoRestartEnabled")]
    public bool AutoRestartEnabled { get; set; } = true; 

    [JsonPropertyName("AutoRestartTime")]
    public string AutoRestartTime { get; set; } = "01:00:00"; 
}

public class AutoUpdate : BasePlugin, IPluginConfig<AutoUpdateConfig>
{
    public override string ModuleName => "AutoUpdate";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "M1k@c";

    public required AutoUpdateConfig Config { get; set; }
    private readonly HttpClient httpClient = new();
    private string lastKnownBuildId = "";

    public void OnConfigParsed(AutoUpdateConfig config)
    {
        Config = config;
    }

    public override void Load(bool hotReload)
    {
        Console.WriteLine("[AutoUpdate] Plugin loaded successfully!");

        // Run an update check immediately, then schedule periodic checks
        CheckForCs2Update();
        AddTimer(Config.CheckIntervalMinutes * 60, () => CheckForCs2Update(), TimerFlags.REPEAT);

        // Schedule Auto Restart if enabled
        if (Config.AutoRestartEnabled)
        {
            ScheduleAutoRestart();
        }
    }

    /// Checks for CS2 update and restarts if needed
    private async void CheckForCs2Update()
    {
        string apiUrl = "https://api.steamcmd.net/v1/info/730";

        try
        {
            var response = await httpClient.GetStringAsync(apiUrl);
            if (string.IsNullOrEmpty(response))
                return;

            string currentBuildId = ParseBuildId(response);
            if (!string.IsNullOrEmpty(currentBuildId) && currentBuildId != lastKnownBuildId)
            {
                lastKnownBuildId = currentBuildId;
                NotifyPlayersAndRestart();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AutoUpdate] Failed to check for update: {ex.Message}");
        }
    }

    /// Parses build ID from Steam API response
    private string ParseBuildId(string jsonResponse)
    {
        int startIndex = jsonResponse.IndexOf("\"buildid\":\"") + 10;
        if (startIndex < 10) return "";

        int endIndex = jsonResponse.IndexOf("\"", startIndex);
        if (endIndex == -1) return "";

        return jsonResponse.Substring(startIndex, endIndex - startIndex);
    }

    /// Notifies players and schedules server restart due to an update
    private void NotifyPlayersAndRestart()
    {
        Console.WriteLine("[AutoRestart] New CS2 update detected! Restarting server in 20 seconds...");

        // Notify all players about the upcoming restart
        foreach (var player in Utilities.GetPlayers())
        {
            if (player.IsValid)
            {
                player.PrintToChat("[AutoRestart] Server will restart in 20 seconds due to a new CS2 update!");
            }
        }

        // Countdown before restarting
        for (int i = 20; i > 0; i -= 5)
        {
            int timeLeft = i;
            AddTimer(20 - timeLeft, () =>
            {
                foreach (var player in Utilities.GetPlayers())
                {
                    if (player.IsValid)
                    {
                        player.PrintToChat($"[AutoRestart] Restarting in {timeLeft} seconds!");
                    }
                }
            });
        }

        // Restart server after 20 seconds
        AddTimer(20, () =>
        {
            Console.WriteLine("[AutoRestart] FORCING SERVER RESTART NOW!");
            ForceServerCrash();
        });
    }

    /// Schedules the server to restart at a configured time
    private void ScheduleAutoRestart()
    {
        if (!TimeSpan.TryParse(Config.AutoRestartTime, out TimeSpan restartTime))
        {
            Console.WriteLine($"[AutoRestart] Invalid AutoRestartTime format: {Config.AutoRestartTime}. Expected format: HH:mm:ss");
            return;
        }

        TimeSpan currentTime = DateTime.Now.TimeOfDay;
        TimeSpan delay = restartTime > currentTime
            ? restartTime - currentTime
            : restartTime.Add(TimeSpan.FromDays(1)) - currentTime;

        Console.WriteLine($"[AutoRestart] Server will restart at {restartTime} ({delay.TotalSeconds} seconds from now).");

        AddTimer((float)delay.TotalSeconds, () =>
        {
            Console.WriteLine("[AutoRestart] Scheduled restart time reached! Restarting server...");
            NotifyPlayersAndRestart();
        });
    }

    /// Forces the server to crash and restart
    private void ForceServerCrash()
    {
        Console.WriteLine("[AutoCrashTest] FORCING SERVER RESTART!");
        CrashRecursive();
    }

    private void CrashRecursive()
    {
        CrashRecursive();
    }
}
