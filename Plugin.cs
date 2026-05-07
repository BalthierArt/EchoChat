using System;
using System.Collections.Generic;
using System.Linq;
using ChatEcho.Windows;
using Dalamud.Game.Command;
#if DALAMUD_API_15
using Dalamud.Game.Chat;
#endif
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using WorldRow = Lumina.Excel.Sheets.World;

namespace ChatEcho;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IChatGui                chatGui;
    private readonly ICommandManager         commandManager;
    private readonly IPluginLog              log;
    private readonly IPlayerState            playerState;
    private readonly IObjectTable            objectTable;
    private readonly ITextureProvider        textureProvider;
    private readonly ITargetManager          targetManager;
    private readonly IDataManager            dataManager;
    private readonly IPartyList              partyList;
    private List<string>? worldNames;

    private const string CommandName = "/chatecho";
    public readonly WindowSystem WindowSystem = new("ChatEcho");

    public Configuration  Configuration { get; private set; }
    public ChatEchoWindow EchoWindow    { get; private set; }
    public ConfigWindow   ConfigWindow  { get; private set; }
    public DebuffHelperWindow DebuffHelperWindow { get; private set; }
    public CastHelperWindow CastHelperWindow { get; private set; }

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        IChatGui                chatGui,
        ICommandManager         commandManager,
        IPluginLog              log,
        IPlayerState            playerState,
        IObjectTable            objectTable,
        ITextureProvider        textureProvider,
        ITargetManager          targetManager,
        IDataManager            dataManager,
        IPartyList              partyList)
    {
        this.pluginInterface = pluginInterface;
        this.chatGui         = chatGui;
        this.commandManager  = commandManager;
        this.log             = log;
        this.playerState     = playerState;
        this.objectTable     = objectTable;
        this.textureProvider = textureProvider;
        this.targetManager   = targetManager;
        this.dataManager     = dataManager;
        this.partyList       = partyList;

        Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(pluginInterface);

        ConfigWindow = new ConfigWindow(this);
        EchoWindow   = new ChatEchoWindow(this);
        DebuffHelperWindow = new DebuffHelperWindow(this, objectTable, textureProvider, log);
        CastHelperWindow = new CastHelperWindow(this, objectTable, targetManager, dataManager, textureProvider, log);
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(EchoWindow);
        WindowSystem.AddWindow(DebuffHelperWindow);
        WindowSystem.AddWindow(CastHelperWindow);

        commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open settings. Args: on | off | test | boss"
        });

        pluginInterface.UiBuilder.Draw         += DrawUi;
        pluginInterface.UiBuilder.OpenMainUi   += OpenConfigUi;
        pluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        chatGui.ChatMessage                    += OnChatMessage;

        log.Information("Chat Echo loaded.");
    }

#if DALAMUD_API_15
    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (!Configuration.Enabled && !Configuration.CastHelperEnabled) return;

        var formattedMessage = FormatSenderlessSystemMessage(message.LogKind, message.Message);
        if (Configuration.CastHelperEnabled)
            CastHelperWindow.OnChatMessage(message.LogKind, formattedMessage);

        if (!Configuration.Enabled) return;

        AddEchoMessage(message.LogKind, message.Sender.TextValue, formattedMessage);
    }
#else
    private void OnChatMessage(
        XivChatType  type,
        int          timestamp,
        ref SeString sender,
        ref SeString message,
        ref bool     isHandled)
    {
        if (!Configuration.Enabled && !Configuration.CastHelperEnabled) return;

        var formattedMessage = FormatSenderlessSystemMessage(type, message);
        if (Configuration.CastHelperEnabled)
            CastHelperWindow.OnChatMessage(type, formattedMessage);

        if (!Configuration.Enabled) return;

        AddEchoMessage(type, sender.TextValue, formattedMessage);
    }
#endif

    private void AddEchoMessage(XivChatType type, string sender, string message)
    {
        if (IsGameLogEffect(type) && !ShouldShowGameLogEffect(message))
            return;

        var key = ChannelDefs.KeyFor(type);
        if (key == null) return;

        var def = ChannelDefs.ByKey(key);
        var ch  = Configuration.Get(key, def?.DefaultColor ?? new System.Numerics.Vector4(1,1,1,1));
        if (!ch.Enabled) return;

        if (IsGameLogEffect(type))
            message = TrimLeadingGameLogMarker(message);

        EchoWindow.AddMessage(type, sender, message);
    }

    private static bool IsGameLogEffect(XivChatType type)
    {
        var id = (ushort)type;
        return id >= 46 && id <= 49;
    }

    private string FormatSenderlessSystemMessage(XivChatType type, SeString message)
    {
        var text = message.TextValue;
        var key = ChannelDefs.KeyFor(type);
        var def = key != null ? ChannelDefs.ByKey(key) : null;
        if (def?.HasSender != false || !IsLikelyDeathNotice(text))
            return text;

        return InsertMissingWorldSeparator(text);
    }

    private static bool IsLikelyDeathNotice(string text)
    {
        return text.Contains("defeated", StringComparison.OrdinalIgnoreCase)
            || text.Contains("KO'd", StringComparison.OrdinalIgnoreCase)
            || text.Contains("knocked out", StringComparison.OrdinalIgnoreCase)
            || text.Contains("has fallen", StringComparison.OrdinalIgnoreCase);
    }

    private string InsertMissingWorldSeparator(string text)
    {
        foreach (var world in GetWorldNames())
        {
            var searchFrom = 1;
            while (searchFrom < text.Length)
            {
                var index = text.IndexOf(world, searchFrom, StringComparison.Ordinal);
                if (index < 1)
                    break;

                var before = text[index - 1];
                var afterIndex = index + world.Length;
                var after = afterIndex >= text.Length ? '\0' : text[afterIndex];
                if (char.IsLetter(before) && (after == '\0' || char.IsWhiteSpace(after) || char.IsPunctuation(after)))
                    return text[..index].TrimEnd() + " - " + text[index..];

                searchFrom = index + 1;
            }
        }

        return text;
    }

    private List<string> GetWorldNames()
    {
        if (worldNames != null)
            return worldNames;

        worldNames = dataManager.GetExcelSheet<WorldRow>()
            .Select(row => row.Name.ExtractText())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(name => name.Length)
            .ToList();

        return worldNames;
    }

    private bool ShouldShowGameLogEffect(string message)
    {
        if (Configuration.GameLogEffectScope == GameLogEffectScope.All)
            return true;

        var trimmed = TrimLeadingGameLogMarker(message);
        if (ContainsStandaloneYou(trimmed))
            return true;

        if (Configuration.GameLogEffectScope == GameLogEffectScope.OnlyUser)
            return false;

        return IsPartyMemberEffect(trimmed);
    }

    private static bool ContainsStandaloneYou(string message)
    {
        const string needle = "You";
        var index = message.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(message[index - 1]);
            var afterIndex = index + needle.Length;
            var after = afterIndex >= message.Length || !char.IsLetterOrDigit(message[afterIndex]);
            if (before && after)
                return true;

            index = message.IndexOf(needle, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private bool IsPartyMemberEffect(string message)
    {
        var localName = objectTable.LocalPlayer?.Name.TextValue;
        if (ContainsStandaloneName(message, localName))
            return true;

        for (var i = 0; i < partyList.Length; i++)
        {
            var member = partyList[i];
            var memberName = member?.GameObject?.Name.TextValue;
            if (string.IsNullOrWhiteSpace(memberName))
                memberName = member?.Name.ToString();

            if (ContainsStandaloneName(message, memberName))
                return true;
        }

        return false;
    }

    private static bool ContainsStandaloneName(string message, string? name)
    {
        if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(name))
            return false;

        name = name.Trim();
        var index = message.IndexOf(name, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(message[index - 1]);
            var afterIndex = index + name.Length;
            var after = afterIndex >= message.Length || !char.IsLetterOrDigit(message[afterIndex]);
            if (before && after)
                return true;

            index = message.IndexOf(name, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string TrimLeadingGameLogMarker(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return message;

        var trimmed = message.TrimStart();
        const string effectMarker = "effect of ";
        var effectIndex = trimmed.IndexOf(effectMarker, StringComparison.OrdinalIgnoreCase);
        if (effectIndex >= 0)
        {
            var markerStart = effectIndex + effectMarker.Length;
            while (markerStart < trimmed.Length && char.IsWhiteSpace(trimmed[markerStart]))
                markerStart++;

            if (markerStart < trimmed.Length && !char.IsLetterOrDigit(trimmed[markerStart]))
                return trimmed.Remove(markerStart, 1).TrimStart();
        }

        if (trimmed.Length <= 1 || char.IsLetterOrDigit(trimmed[0]))
            return trimmed;

        return trimmed[1..].TrimStart();
    }

    private void DrawUi()       => WindowSystem.Draw();
    private void OpenConfigUi() => ConfigWindow.IsOpen = true;

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "on":   Configuration.Enabled = true;  Configuration.Save(); chatGui.Print("[Chat Echo] Enabled.");  break;
            case "off":  Configuration.Enabled = false; Configuration.Save(); chatGui.Print("[Chat Echo] Disabled."); break;
            case "test": RunTestMessages(); break;
            case "boss":
                Configuration.CastHelperEnabled = !Configuration.CastHelperEnabled;
                Configuration.Save();
                chatGui.Print(Configuration.CastHelperEnabled ? "[Chat Echo] Boss Helper enabled." : "[Chat Echo] Boss Helper disabled.");
                break;
            default:     ConfigWindow.IsOpen = !ConfigWindow.IsOpen; break;
        }
    }

    private System.Threading.CancellationTokenSource? testCts;

    public void RunTestMessages()
    {
        testCts?.Cancel();
        testCts = new System.Threading.CancellationTokenSource();
        var token = testCts.Token;

        var tests = new[]
        {
            (XivChatType.Party,    "Moenbryda Vrai",       "Tank swap on 3!"),
            (XivChatType.Party,    "Y'shtola Rhul",        "Stack on A marker NOW"),
            (XivChatType.Alliance, "Alphinaud Leveilleur", "Group 1 left, Group 2 right"),
            (XivChatType.Party,    "Estinien Wyrmblood",   "Spread for tethers!"),
            (XivChatType.Party,    "Thancred Waters",      "LB3 after the stack"),
            (XivChatType.Alliance, "Alisaie Leveilleur",   "mechanics are for cars amirite"),
            (XivChatType.Party,    "G'raha Tia",           "dodge out NOW"),
            (XivChatType.Party,    "Urianger Augurelt",    "Forsooth I shall heal when I feel like it"),
            (XivChatType.Alliance, "Krile Baldesion",      "GGs everyone great run!"),
            (XivChatType.Party,    "Lyna",                 "who pulled without ready check again lol"),
        };

        System.Threading.Tasks.Task.Run(async () =>
        {
            foreach (var (type, sender, text) in tests)
            {
                if (token.IsCancellationRequested) break;
                EchoWindow.AddMessage(type, sender, text);
                await System.Threading.Tasks.Task.Delay(700, token).ConfigureAwait(false);
            }
        }, token);
    }

    public void Dispose()
    {
        testCts?.Cancel();
        testCts?.Dispose();
        chatGui.ChatMessage                    -= OnChatMessage;
        pluginInterface.UiBuilder.Draw         -= DrawUi;
        pluginInterface.UiBuilder.OpenMainUi   -= OpenConfigUi;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        commandManager.RemoveHandler(CommandName);
        WindowSystem.RemoveAllWindows();
        log.Information("Chat Echo unloaded.");
    }
}
