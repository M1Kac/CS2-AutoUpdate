using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Config;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using System.Text.Json.Serialization;
using System.Net.Http;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Localization;

namespace AutoUpdate
{
    public class AutoUpdateConfig : BasePluginConfig
    {
        [JsonPropertyName("CheckIntervalMinutes")]
        public int CheckIntervalMinutes { get; set; } = 20;

        [JsonPropertyName("AutoRestartEnabled")]
        public bool AutoRestartEnabled { get; set; } = true;

        [JsonPropertyName("EnableManualRestart")]
        public bool EnableManualRestart { get; set; } = true;

        [JsonPropertyName("Flag")]
        public string Flag { get; set; } = "@css/root";

        [JsonPropertyName("AutoRestartTime")]
        public string AutoRestartTime { get; set; } = "01:00:00";
    }

    public class AutoUpdate : BasePlugin, IPluginConfig<AutoUpdateConfig>
    {
        public override string ModuleName => "AutoUpdate";
        public override string ModuleVersion => "1.0.1";
        public override string ModuleAuthor => "M1k@c";

        public required AutoUpdateConfig Config { get; set; }
        private readonly HttpClient httpClient = new();
        private string lastKnownBuildId = "";
        private static IStringLocalizer? _Localizer;

        public void OnConfigParsed(AutoUpdateConfig config)
        {
            Config = config;
        }

        public override void Load(bool hotReload)
        {
            Console.WriteLine("[AutoUpdate] Plugin loaded successfully!");

            _Localizer = Localizer;

            AddCommand("css_update", "Check for latest CS2 update", (player, info) => CheckUpdateCommand(player));
            AddCommand("css_crash", "Restart the server immediately", (player, info) => RestartCommand(player));

            CheckForCs2Update();
            AddTimer(Config.CheckIntervalMinutes * 60, () => CheckForCs2Update(), TimerFlags.REPEAT);

            if (Config.AutoRestartEnabled)
            {
                ScheduleAutoRestart();
            }
        }

        private bool HasAdminPermission(CCSPlayerController? player)
        {
            return player != null && AdminManager.PlayerHasPermissions(player, Config.Flag);
        }

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
                    Console.WriteLine($"[AutoUpdate] New CS2 update detected: Build {currentBuildId}");
                    NotifyPlayersAndRestart();
                }
                else
                {
                    Console.WriteLine("[AutoUpdate] No new update detected.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AutoUpdate] Failed to check for update: {ex.Message}");
            }
        }

        private string ParseBuildId(string jsonResponse)
        {
            int startIndex = jsonResponse.IndexOf("\"buildid\":\"") + 10;
            if (startIndex < 10) return "";

            int endIndex = jsonResponse.IndexOf("\"", startIndex);
            if (endIndex == -1) return "";

            return jsonResponse.Substring(startIndex, endIndex - startIndex);
        }

        private void NotifyPlayersAndRestart()
        {
            Console.WriteLine("[AutoRestart] New CS2 update detected! Restarting server in 20 seconds...");

            Server.NextFrame(() =>
            {
                foreach (var player in Utilities.GetPlayers())
                {
                    if (player?.IsValid == true)
                    {
                        player.PrintToChat(_Localizer?.ForPlayer(player, "update_found") ?? "Update found!");
                    }
                }
            });

            AddTimer(20, RestartServer);
        }

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

            AddTimer((float)delay.TotalSeconds, RestartServer);
        }

        private void RestartServer()
        {
            Console.WriteLine("[AutoRestart] Restarting server via command...");
            Server.ExecuteCommand("quit");
        }

        private async void CheckUpdateCommand(CCSPlayerController? player)
        {
            if (!HasAdminPermission(player))
            {
                Console.WriteLine("[AutoUpdate] Player has no admin permission for update check.");
                return;
            }

            Console.WriteLine("[AutoUpdate] Admin executed 'css_update' command.");

            string apiUrl = "https://api.steamcmd.net/v1/info/730";
            try
            {
                var response = await httpClient.GetStringAsync(apiUrl);
                string latestBuildId = ParseBuildId(response);

                Server.NextFrame(() =>
                {
                    if (player?.IsValid == true)
                    {
                        if (latestBuildId == lastKnownBuildId)
                        {
                            player.PrintToChat(_Localizer?.ForPlayer(player, "server_up_to_date") ?? "Your server is running the latest version.");
                        }
                        else
                        {
                            player.PrintToChat(_Localizer?.ForPlayer(player, "new_update_available", latestBuildId) ?? $"A new update is available! Build: {latestBuildId}");
                            NotifyPlayersAndRestart();
                        }
                    }
                });
            }
            catch
            {
                Server.NextFrame(() =>
                {
                    if (player?.IsValid == true)
                        player.PrintToChat(_Localizer?.ForPlayer(player, "update_check_failed") ?? "Failed to check for updates.");
                });
            }
        }

        private void RestartCommand(CCSPlayerController? player)
        {
            if (!HasAdminPermission(player))
            {
                Console.WriteLine("[AutoUpdate] Player has no admin permission for restart.");
                return;
            }

            if (!Config.EnableManualRestart)
            {
                Server.NextFrame(() =>
                {
                    if (player?.IsValid == true)
                        player.PrintToChat(_Localizer?.ForPlayer(player, "manual_restart_disabled") ?? "Manual restart is disabled.");
                });
                return;
            }

            Console.WriteLine("[AutoUpdate] Admin executed 'css_crash' command.");
            Server.NextFrame(() =>
            {
                if (player?.IsValid == true)
                    player.PrintToChat(_Localizer?.ForPlayer(player, "restarting_now") ?? "Restarting server now...");
            });
            RestartServer();
        }
    }
}
