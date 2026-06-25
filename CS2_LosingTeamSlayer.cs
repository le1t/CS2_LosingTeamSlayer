﻿using System.Globalization;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;

namespace CS2LosingTeamSlayer;

/// <summary>
/// Конфигурация плагина CS2 LosingTeamSlayer
/// </summary>
public class CS2_LosingTeamSlayerConfig : BasePluginConfig
{
    [JsonPropertyName("css_losingteamslayer_enabled")]
    public int Enabled { get; set; } = 1;

    [JsonPropertyName("css_losingteamslayer_slaymessage")]
    public int SlayMessage { get; set; } = 1;

    [JsonPropertyName("css_losingteamslayer_delay")]
    public float Delay { get; set; } = 2.0f;
}

[MinimumApiVersion(369)]
public class CS2_LosingTeamSlayer : BasePlugin, IPluginConfig<CS2_LosingTeamSlayerConfig>
{
    public override string ModuleName => "CS2 LosingTeamSlayer";
    public override string ModuleVersion => "3.0";
    public override string ModuleAuthor => "Fixed by le1t1337 + AI DeepSeek + AI GitHub Copilot. Code logic by Lindgren, Grey83";

    public required CS2_LosingTeamSlayerConfig Config { get; set; }

    private readonly string[] _messages = new[]
    {
        "Разминируй бомбу или умри пытаясь!",
        "Всегда защищай бомбу!",
        "Не пускай КТ к заложникам!",
        "Заложи бомбу или умри пытаясь!",
        "Спаси заложников даже ценой своей жизни!"
    };

    public void OnConfigParsed(CS2_LosingTeamSlayerConfig config)
    {
        config.Enabled = Math.Clamp(config.Enabled, 0, 1);
        config.SlayMessage = Math.Clamp(config.SlayMessage, 0, 1);
        config.Delay = Math.Clamp(config.Delay, 0.0f, 10.0f);
        Config = config;
    }

    public override void Load(bool hotReload)
    {
        // Удаляем старый файл конфига, если есть
        string oldConfigPath = Path.Combine(Server.GameDirectory, "counterstrikesharp", "configs", "plugins", "LosingTeamSlayerPlugin.json");
        if (File.Exists(oldConfigPath))
        {
            try { File.Delete(oldConfigPath); } catch { }
        }

        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt); // защита террористов от взрыва

        AddCommand("css_losingteamslayer_enabled", "Включить/выключить плагин (0/1)", OnEnabledCommand);
        AddCommand("css_losingteamslayer_slaymessage", "Включить/выключить вывод сообщений о казни (0/1)", OnSlayMessageCommand);
        AddCommand("css_losingteamslayer_delay", "Установить задержку перед казнью в секундах (0.0-10.0)", OnDelayCommand);
    }

    // ==================== РУЧНОЙ ОБХОД СЛОТОВ ====================
    /// <summary>
    /// Возвращает список всех подключённых игроков (включая ботов) путём прямого перебора слотов 0..63.
    /// Обходит искажённый Server.MaxPlayers и конфликты со сторонними плагинами (например, DoubleJump).
    /// </summary>
    private List<CCSPlayerController> GetAllPlayers()
    {
        var result = new List<CCSPlayerController>();
        for (int slot = 0; slot < 64; slot++)
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player != null && player.IsValid && player.PlayerPawn.IsValid)
            {
                result.Add(player);
            }
        }
        return result;
    }
    // =================================================================

    private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        if (Config.Enabled == 0)
            return HookResult.Continue;

        var victim = @event.Userid;
        if (victim == null || !victim.IsValid || victim.Team != CsTeam.Terrorist)
            return HookResult.Continue;

        var attacker = @event.Attacker;
        if (attacker == null || !attacker.IsValid)
        {
            int damage = @event.DmgHealth;
            if (damage > 0 && victim.PlayerPawn.IsValid && victim.PlayerPawn.Value != null)
            {
                int currentHealth = victim.Health;
                int newHealth = Math.Min(currentHealth + damage, 100);
                victim.Health = newHealth;
                victim.PlayerPawn.Value.Health = newHealth;
            }
        }

        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        if (Config.Enabled == 0)
            return HookResult.Continue;

        CsTeam losingTeam;
        int messageIndex;

        switch (@event.Reason)
        {
            case 1: // Target Bombed
                losingTeam = CsTeam.CounterTerrorist;
                messageIndex = 0;
                break;
            case 7: // Bomb Defused
                losingTeam = CsTeam.Terrorist;
                messageIndex = 1;
                break;
            case 11: // All Hostages Rescued
                losingTeam = CsTeam.Terrorist;
                messageIndex = 2;
                break;
            case 12: // Target Saved
                losingTeam = CsTeam.Terrorist;
                messageIndex = 3;
                break;
            case 13: // Hostages Not Rescued
                losingTeam = CsTeam.CounterTerrorist;
                messageIndex = 4;
                break;
            default:
                return HookResult.Continue;
        }

        var team = losingTeam;
        var messageText = _messages[messageIndex];

        AddTimer(Config.Delay, () =>
        {
            if (Config.Enabled == 0) return;

            var players = GetAllPlayers();

            foreach (var player in players)
            {
                if (player.Team != team)
                    continue;

                if (player.PlayerPawn.Value?.LifeState == (byte)LifeState_t.LIFE_ALIVE)
                {
                    player.CommitSuicide(false, false);
                }
            }

            if (Config.SlayMessage == 1)
            {
                string teamTag = (team == CsTeam.Terrorist) ? "T" : "CT";
                char teamColor = (team == CsTeam.Terrorist) ? ChatColors.LightRed : ChatColors.Blue;

                string formattedMessage = $"{ChatColors.Orange}[LosingTeamSlayer] {teamColor}{teamTag}:{ChatColors.Orange} {messageText}{ChatColors.Default}";

                foreach (var player in players)
                {
                    if (player.IsValid && !player.IsBot)
                    {
                        player.PrintToChat($" {formattedMessage}");
                    }
                }
            }
        });

        return HookResult.Continue;
    }

    // -------------------- Команды --------------------

    private void OnEnabledCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand($"""
                [CS2 LosingTeamSlayer] Настройка: css_losingteamslayer_enabled
                Описание: Включение/выключение всего плагина.
                Допустимые значения: 0 (отключён), 1 (включён).
                Текущее значение: {Config.Enabled}
                Использование: css_losingteamslayer_enabled <0/1>
                Пример: css_losingteamslayer_enabled 1
                """);
            return;
        }

        string arg = command.GetArg(1);
        if (int.TryParse(arg, out int value) && (value == 0 || value == 1))
        {
            int old = Config.Enabled;
            Config.Enabled = value;
            SaveConfig();
            command.ReplyToCommand($"[CS2 LosingTeamSlayer] Параметр enabled изменён с {old} на {value}.");
        }
        else
            command.ReplyToCommand("[CS2 LosingTeamSlayer] Ошибка: значение должно быть 0 или 1.");
    }

    private void OnSlayMessageCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand($"""
                [CS2 LosingTeamSlayer] Настройка: css_losingteamslayer_slaymessage
                Описание: Выводить ли сообщение о причине казни в чат всем игрокам после казни.
                Допустимые значения: 0 (не выводить), 1 (выводить).
                Текущее значение: {Config.SlayMessage}
                Использование: css_losingteamslayer_slaymessage <0/1>
                Пример: css_losingteamslayer_slaymessage 1
                """);
            return;
        }

        string arg = command.GetArg(1);
        if (int.TryParse(arg, out int value) && (value == 0 || value == 1))
        {
            int old = Config.SlayMessage;
            Config.SlayMessage = value;
            SaveConfig();
            command.ReplyToCommand($"[CS2 LosingTeamSlayer] Параметр slaymessage изменён с {old} на {value}.");
        }
        else
            command.ReplyToCommand("[CS2 LosingTeamSlayer] Ошибка: значение должно быть 0 или 1.");
    }

    private void OnDelayCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand($"""
                [CS2 LosingTeamSlayer] Настройка: css_losingteamslayer_delay
                Описание: Задержка перед казнью проигравшей команды (в секундах).
                Допустимый диапазон: от 0.0 до 10.0.
                Текущее значение: {Config.Delay:F1}
                Использование: css_losingteamslayer_delay <число>
                Пример: css_losingteamslayer_delay 3.5
                """);
            return;
        }

        string arg = command.GetArg(1).Replace(',', '.');
        if (float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            float old = Config.Delay;
            Config.Delay = Math.Clamp(value, 0.0f, 10.0f);
            if (Config.Delay != value)
                command.ReplyToCommand($"[CS2 LosingTeamSlayer] Значение скорректировано до {Config.Delay:F1} (допустимый диапазон 0.0-10.0).");
            SaveConfig();
            command.ReplyToCommand($"[CS2 LosingTeamSlayer] Параметр delay изменён с {old:F1} на {Config.Delay:F1}.");
        }
        else
            command.ReplyToCommand("[CS2 LosingTeamSlayer] Ошибка: необходимо ввести число с точкой (например, 2.5).");
    }

    private void SaveConfig()
    {
        try
        {
            string configPath = Path.Combine(Server.GameDirectory, "counterstrikesharp", "configs", "plugins", "CS2_LosingTeamSlayer", "CS2_LosingTeamSlayer.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            string json = System.Text.Json.JsonSerializer.Serialize(Config, options);
            File.WriteAllText(configPath, json);
        }
        catch { }
    }

    public override void Unload(bool hotReload)
    {
        // Никаких ресурсов освобождать не требуется
    }
}