using ChatEcho.Windows;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace ChatEcho;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IChatGui                chatGui;
    private readonly ICommandManager         commandManager;
    private readonly IPluginLog              log;

    private const string CommandName = "/chatecho";
    public readonly WindowSystem WindowSystem = new("ChatEcho");

    public Configuration  Configuration { get; private set; }
    public ChatEchoWindow EchoWindow    { get; private set; }
    public ConfigWindow   ConfigWindow  { get; private set; }

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        IChatGui                chatGui,
        ICommandManager         commandManager,
        IPluginLog              log)
    {
        this.pluginInterface = pluginInterface;
        this.chatGui         = chatGui;
        this.commandManager  = commandManager;
        this.log             = log;

        Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(pluginInterface);

        ConfigWindow = new ConfigWindow(this);
        EchoWindow   = new ChatEchoWindow(this);
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(EchoWindow);

        commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open settings. Args: on | off | test"
        });

        pluginInterface.UiBuilder.Draw         += DrawUi;
        pluginInterface.UiBuilder.OpenMainUi   += OpenConfigUi;
        pluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        chatGui.ChatMessage                    += OnChatMessage;

        log.Information("Chat Echo loaded.");
    }

    private void OnChatMessage(
        XivChatType  type,
        int          timestamp,
        ref SeString sender,
        ref SeString message,
        ref bool     isHandled)
    {
        if (!Configuration.Enabled) return;

        var key = ChannelDefs.KeyFor(type);
        if (key == null) return;

        var def = ChannelDefs.ByKey(key);
        var ch  = Configuration.Get(key, def?.DefaultColor ?? new System.Numerics.Vector4(1,1,1,1));
        if (!ch.Enabled) return;

        EchoWindow.AddMessage(type, sender.TextValue, message.TextValue);
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
            default:     ConfigWindow.IsOpen = !ConfigWindow.IsOpen; break;
        }
    }

    private System.Threading.CancellationTokenSource? testCts;

    public void RunTestMessages()
    {
        // Cancel any already-running test sequence
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
