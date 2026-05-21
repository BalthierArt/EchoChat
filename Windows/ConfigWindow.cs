using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace ChatEcho.Windows;

public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;
    private int selectedView;
    private int lastSelectedView = -1;
    private string? headerHelpText;
    private string? nextHeaderHelpText;
    private string newPriorityWord = string.Empty;
    private static readonly (string Label, FontAwesomeIcon Icon)[] Views =
    [
        ("General", FontAwesomeIcon.Cog),
        ("Display", FontAwesomeIcon.Desktop),
        ("Channels", FontAwesomeIcon.Comments),
        ("Debuff Helper", FontAwesomeIcon.Heartbeat),
        ("Boss Helper", FontAwesomeIcon.ExclamationTriangle),
        ("Priority", FontAwesomeIcon.Star),
    ];
    private static readonly Vector2 NavBarSize = new(190, 0);
    private static readonly Vector2 WindowPadding = new(10, 10);
    private static readonly Vector2 FramePadding = new(7, 5);
    private static readonly Vector2 ItemSpacing = new(9, 6);
    private static readonly Vector4 Primary = new(0.32f, 0.58f, 0.95f, 0.86f);
    private static readonly Vector4 PrimaryAccent = new(0.45f, 0.70f, 1f, 0.96f);
    private static readonly Vector4 Warn = new(0.72f, 0.86f, 1f, 1f);
    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 TextMuted = new(0.62f, 0.66f, 0.72f, 1f);
    private static readonly uint Panel = ImGui.ColorConvertFloat4ToU32(new Vector4(0.1294f, 0.1333f, 0.1764f, 1f));
    private static readonly uint PanelElevated = ImGui.ColorConvertFloat4ToU32(Primary);
    private static readonly uint Background = ImGui.ColorConvertFloat4ToU32(new Vector4(0.0431f, 0.0549f, 0.0588f, 0.95f));

    private static readonly (int from, int to, string label, FontAwesomeIcon icon)[] Sections =
    {
        (0,  2,  "Combat / Raid", FontAwesomeIcon.ShieldAlt),
        (3,  9,  "Social", FontAwesomeIcon.Users),
        (10, 11, "Linkshells", FontAwesomeIcon.Link),
        (12, 15, "System", FontAwesomeIcon.Server),
        (16, 19, "Game Log", FontAwesomeIcon.ListAlt),
    };

    public ConfigWindow(Plugin plugin) : base("Chat Echo — Settings")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(620, 420),
            MaximumSize = ImGui.GetIO().DisplaySize
        };
        Size = new Vector2(740, 500);
    }

    private static bool CE4(string label, Vector4 cur, out Vector4 result)
    {
        var c = cur;
        bool ch = ImGui.ColorEdit4(label, ref c, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaPreview);
        result = c;
        return ch;
    }

    public override void Draw()
    {
        var cfg = plugin.Configuration;
        nextHeaderHelpText = null;

        PushSettingsStyle();
        try
        {
            DrawStatusHeader(cfg);
            ImGui.Spacing();
            DrawNavigation();
            ImGui.SameLine();

            if (ImGui.BeginChild("###ChatEchoContent", Vector2.Zero, true, ImGuiWindowFlags.None))
            {
                if (lastSelectedView != selectedView)
                {
                    headerHelpText = null;
                    lastSelectedView = selectedView;
                }

                DrawSelectedPage(cfg);
                ImGui.EndChild();
            }
        }
        finally
        {
            PopSettingsStyle();
        }

        headerHelpText = nextHeaderHelpText;
    }

    private void DrawStatusHeader(Configuration cfg)
    {
        var en = cfg.Enabled;
        if (ImGui.Checkbox("##en", ref en)) { cfg.Enabled = en; cfg.Save(); }
        ImGui.SameLine();

        Vector4 statusColor;
        string  statusText;
        if (!en)
        {
            statusColor = new Vector4(0.55f, 0.55f, 0.55f, 1f);
            statusText  = "Chat Echo is DISABLED";
        }
        else if (cfg.PriorityOnly && cfg.EnablePriority)
        {
            statusColor = new Vector4(1f, 0.3f, 0.3f, 1f);
            statusText  = "Chat Echo  —  Priority Only ENABLED";
        }
        else
        {
            statusColor = new Vector4(0.4f, 1f, 0.4f, 1f);
            statusText  = "Chat Echo is ENABLED";
        }

        ImGui.TextColored(statusColor, statusText);
        if (cfg.DebuffHelperEnabled)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 0.78f, 0.2f, 1f), "Debuff Helper ENABLED");
        }
        if (cfg.CastHelperEnabled)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.45f, 0.82f, 1f, 1f), "Boss Helper ENABLED");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Toggle the overlay.\n/chatecho on  |  /chatecho off");

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
    }

    private void DrawNavigation()
    {
        var buttonSize = new Vector2(NavBarSize.X - WindowPadding.X * 2f, 30f);

        if (!ImGui.BeginChild("###ChatEchoNav", NavBarSize, true, ImGuiWindowFlags.NoScrollbar))
            return;

        CenteredTitle(FontAwesomeIcon.CommentDots, "CHAT ECHO", 1.08f);
        TextCentered("Categories", TextMuted);
        ImGui.Spacing();

        for (var i = 0; i < Views.Length; i++)
        {
            if (NavButton(Views[i], selectedView == i, buttonSize))
                selectedView = i;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (NavButton((cfgButtonLabel(), plugin.Configuration.Enabled ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play), false, buttonSize))
        {
            var cfg = plugin.Configuration;
            cfg.Enabled = !cfg.Enabled;
            cfg.Save();
        }

        ImGui.EndChild();

        string cfgButtonLabel() => plugin.Configuration.Enabled ? "Pause Chat Echo" : "Resume Chat Echo";
    }

    private void DrawSelectedPage(Configuration cfg)
    {
        DrawPageHeader(Views[Math.Clamp(selectedView, 0, Views.Length - 1)], DefaultHelpFor(selectedView, cfg));

        switch (selectedView)
        {
            case 1:
                DrawDisplayTab(cfg);
                break;
            case 2:
                DrawChannelsTab(cfg);
                break;
            case 3:
                DrawDebuffHelperTab(cfg);
                break;
            case 4:
                DrawCastHelperTab(cfg);
                break;
            case 5:
                DrawPriorityTab(cfg);
                break;
            default:
                DrawGeneralTab(cfg);
                break;
        }
    }

    private void DrawPageHeader((string Label, FontAwesomeIcon Icon) view, string defaultHelp)
    {
        ContentBox($"Header{view.Label}", PanelElevated, true, () =>
        {
            CenteredTitle(view.Icon, view.Label.ToUpperInvariant(), 1.35f);
            TextCentered(GetHeaderHelp(defaultHelp), White);
        });
    }

    private static string DefaultHelpFor(int view, Configuration cfg)
        => view switch
        {
            1 => "Change banner appearance, background, text effects, and name formatting.",
            2 => DefaultChannelHelp(cfg.ColorMode),
            3 => "Configure the movable Debuff Helper window and its text styling.",
            4 => "Configure the movable Boss Helper cast window and supported mechanic alerts.",
            5 => "Highlight important words or show only messages containing priority words.",
            _ => "Core Chat Echo behavior, banner sizing, position, lock state, and test messages.",
        };

    private static string DefaultChannelHelp(ColorMode mode)
        => mode switch
        {
            ColorMode.Split => "Split example: player name can be gold while the message text is white.",
            ColorMode.Solid => "Solid example: every channel, name, and message uses the same color.",
            ColorMode.Gradient => "Gradient example: a name can fade blue to white, and the message can fade green to yellow.",
            _ => "Per-channel example: Party can be blue, Tell can be pink, and NPC Dialogue can be yellow.",
        };

    private static void ContentBox(string id, Vector4 backgroundColor, bool includeEndPadding, Action drawContent)
    {
        ContentBox(id, ImGui.ColorConvertFloat4ToU32(backgroundColor), includeEndPadding, drawContent);
    }

    private static void ContentBox(string id, uint backgroundColor, bool includeEndPadding, Action drawContent)
    {
        var draw = ImGui.GetWindowDrawList();
        var padding = WindowPadding;
        var startCursor = ImGui.GetCursorPos();
        var startScreen = ImGui.GetCursorScreenPos();

        draw.ChannelsSplit(2);
        draw.ChannelsSetCurrent(1);

        ImGui.SetCursorPos(startCursor + padding);
        ImGui.BeginGroup();
        drawContent();
        ImGui.EndGroup();

        var contentSize = ImGui.GetItemRectSize();
        var boxSize = new Vector2(ImGui.GetWindowWidth() - padding.X * 2f, contentSize.Y + padding.Y * 2f);
        draw.ChannelsSetCurrent(0);
        draw.AddRectFilled(startScreen, startScreen + boxSize, backgroundColor, 8f);
        draw.ChannelsMerge();

        ImGui.SetCursorPosY(startCursor.Y + boxSize.Y + (includeEndPadding ? padding.Y : 0f));

        _ = id;
    }

    private void SetHeaderHelpOnHover(string? helpText)
    {
        if (!string.IsNullOrWhiteSpace(helpText) && ImGui.IsItemHovered())
            nextHeaderHelpText = helpText;
    }

    private string GetHeaderHelp(string defaultText)
        => string.IsNullOrWhiteSpace(headerHelpText) ? defaultText : headerHelpText;

    private static void CenteredTitle(string text, float scale = 1f)
    {
        ImGui.SetWindowFontScale(scale);
        var size = ImGui.CalcTextSize(text);
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), (ImGui.GetWindowWidth() - size.X) * 0.5f));
        ImGui.TextUnformatted(text);
        ImGui.SetWindowFontScale(1f);
    }

    private static void CenteredTitle(FontAwesomeIcon icon, string text, float scale = 1f)
    {
        ImGui.SetWindowFontScale(scale);
        var iconText = icon.ToIconString();
        var iconSize = ImGui.CalcTextSize(iconText);
        var textSize = ImGui.CalcTextSize(text);
        var spacing = ImGui.GetStyle().ItemInnerSpacing.X;
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), (ImGui.GetWindowWidth() - iconSize.X - spacing - textSize.X) * 0.5f));
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextColored(White, iconText);
        ImGui.SameLine(0, spacing);
        ImGui.TextColored(White, text);
        ImGui.SetWindowFontScale(1f);
    }

    private static void TextCentered(string text, Vector4 color)
    {
        var size = ImGui.CalcTextSize(text);
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), (ImGui.GetWindowWidth() - size.X) * 0.5f));
        ImGui.TextColored(color, text);
    }

    private static bool NavButton((string Label, FontAwesomeIcon Icon) view, bool selected, Vector2 size)
    {
        if (selected)
            ImGui.PushStyleColor(ImGuiCol.Button, Primary);

        ImGui.SetCursorPosX(10f);
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextColored(selected ? White : TextMuted, view.Icon.ToIconString());
        ImGui.SameLine();
        var clicked = ImGui.Button($"{view.Label}##nav{view.Label}", new Vector2(size.X - 28f, size.Y));

        if (selected)
            ImGui.PopStyleColor();

        return clicked;
    }

    private static void SectionHeader(FontAwesomeIcon icon, string text)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextColored(White, icon.ToIconString());
        ImGui.SameLine();
        ImGui.TextColored(Warn, text);
    }

    private static void InlineIcon(FontAwesomeIcon icon)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextColored(White, icon.ToIconString());
        ImGui.SameLine();
    }

    private static void PushSettingsStyle()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, FramePadding);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, ItemSpacing);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, WindowPadding);
        ImGui.PushStyleColor(ImGuiCol.Border, Panel);
        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.ColorConvertU32ToFloat4(Panel));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Primary);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, PrimaryAccent);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Panel);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Background);
    }

    private static void PopSettingsStyle()
    {
        ImGui.PopStyleColor(6);
        ImGui.PopStyleVar(6);
    }

    private void DrawGeneralTab(Configuration cfg)
    {
        ImGui.Spacing();

        var dur = cfg.DisplayDuration;
        InlineIcon(FontAwesomeIcon.Clock);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("Display Duration (s)", ref dur, 0.5f, 15f, "%.1f s"))
        { cfg.DisplayDuration = dur; cfg.Save(); }
        SetHeaderHelpOnHover("How long a chat banner stays visible before it fades away.");

        var maxMsg = cfg.MaxMessages;
        InlineIcon(FontAwesomeIcon.ListAlt);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderInt("Max Messages", ref maxMsg, 1, 10))
        { cfg.MaxMessages = maxMsg; cfg.Save(); }
        SetHeaderHelpOnHover("How many chat messages can be shown on screen at the same time.");

        var fs = cfg.FontSize;
        InlineIcon(FontAwesomeIcon.Font);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("Font Size", ref fs, 10f, 72f, "%.0f px"))
        { cfg.FontSize = fs; cfg.Save(); }
        SetHeaderHelpOnHover("Makes the main chat banner text bigger or smaller.");

        var wrapWidth = cfg.WrapWidth;
        InlineIcon(FontAwesomeIcon.AlignLeft);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("Text wrap width", ref wrapWidth, 250f, 1200f, "%.0f px"))
        { cfg.WrapWidth = wrapWidth; cfg.Save(); }
        SetHeaderHelpOnHover("Controls when long chat messages wrap onto the next line.");

        var showLast = cfg.ShowLastFaded;
        InlineIcon(FontAwesomeIcon.Eye);
        if (ImGui.Checkbox("Show last message faded after expiry", ref showLast))
        { cfg.ShowLastFaded = showLast; cfg.Save(); }
        SetHeaderHelpOnHover("Keeps the last message faintly visible so you can glance back at it.");

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

        var locked = cfg.Locked;
        InlineIcon(FontAwesomeIcon.Lock);
        if (ImGui.Checkbox("Lock banner position (click-through)", ref locked))
        { cfg.Locked = locked; cfg.Save(); }
        SetHeaderHelpOnHover("Locked means clicks pass through the banner. Unlock it when you want to drag it somewhere else.");

        ImGui.Spacing();
        InlineIcon(FontAwesomeIcon.MapMarker);
        ImGui.TextDisabled($"Banner position: ({cfg.BannerPosition.X:F0}, {cfg.BannerPosition.Y:F0})");
        if (ImGui.SmallButton("Reset to centre")) { cfg.BannerPosition = new Vector2(600, 400); cfg.Save(); }
        SetHeaderHelpOnHover("Moves the chat banner back near the middle of the screen.");

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

        InlineIcon(FontAwesomeIcon.Play);
        if (ImGui.Button("Run Test Messages"))
            plugin.RunTestMessages();
        SetHeaderHelpOnHover("Shows sample messages so you can preview the banner without waiting for chat.");

        ImGui.Spacing();
        ImGui.TextDisabled("/chatecho            open settings");
        ImGui.TextDisabled("/chatecho on / off   enable or disable");
        ImGui.TextDisabled("/chatecho test       preview test messages");

    }

    private void DrawDisplayTab(Configuration cfg)
    {
        ImGui.Spacing();

        var opacity = cfg.BackgroundOpacity;
        InlineIcon(FontAwesomeIcon.Adjust);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("Background Opacity", ref opacity, 0f, 1f, "%.2f"))
        { cfg.BackgroundOpacity = opacity; cfg.Save(); }
        SetHeaderHelpOnHover("Changes how dark the chat banner background is.");

        var padding = cfg.BackgroundPadding;
        InlineIcon(FontAwesomeIcon.Expand);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("Background Padding", ref padding, 0f, 20f, "%.0f px"))
        { cfg.BackgroundPadding = padding; cfg.Save(); }
        SetHeaderHelpOnHover("Adds space around the text inside the chat banner.");

        ImGui.Spacing(); ImGui.Separator(); SectionHeader(FontAwesomeIcon.Magic, "Text Effect"); ImGui.Spacing();

        int eff = (int)cfg.TextEffect;
        bool ec = ImGui.RadioButton("None",    ref eff, 0); ImGui.SameLine();
        ec |=     ImGui.RadioButton("Shadow",  ref eff, 1); ImGui.SameLine();
        ec |=     ImGui.RadioButton("Outline", ref eff, 2);
        if (ec) { cfg.TextEffect = (TextEffect)eff; cfg.Save(); }
        SetHeaderHelpOnHover("Adds a shadow or outline so banner text is easier to read over the game.");

        if (cfg.TextEffect == TextEffect.Shadow)
        {
            if (CE4("Shadow colour##shc", cfg.ShadowColor, out var sc)) { cfg.ShadowColor = sc; cfg.Save(); }
            SetHeaderHelpOnHover("Pick the shadow color used behind banner text.");
            ImGui.SameLine();
            if (ImGui.SmallButton("R##shc")) { cfg.ShadowColor = new Vector4(0,0,0,0.8f); cfg.Save(); }
            SetHeaderHelpOnHover("Reset the shadow color.");
        }
        if (cfg.TextEffect == TextEffect.Outline)
        {
            if (CE4("Outline colour##olc", cfg.OutlineColor, out var oc)) { cfg.OutlineColor = oc; cfg.Save(); }
            SetHeaderHelpOnHover("Pick the outline color used around banner text.");
            ImGui.SameLine();
            if (ImGui.SmallButton("R##olc")) { cfg.OutlineColor = new Vector4(0,0,0,1); cfg.Save(); }
            SetHeaderHelpOnHover("Reset the outline color.");
        }

        ImGui.Spacing(); ImGui.Separator(); SectionHeader(FontAwesomeIcon.IdCard, "Name Format"); ImGui.Spacing();

        var pref = cfg.ShowChannelPrefix;
        if (ImGui.Checkbox("Show channel prefix  e.g. (Party)", ref pref))
        { cfg.ShowChannelPrefix = pref; cfg.Save(); }
        SetHeaderHelpOnHover("Shows the chat channel before the name so you know where the message came from.");

        var fn = cfg.FirstNameOnly;
        if (ImGui.Checkbox("First name only", ref fn))
        { cfg.FirstNameOnly = fn; cfg.Save(); }
        SetHeaderHelpOnHover("Shortens player names to first name only.");

    }

    private void DrawChannelsTab(Configuration cfg)
    {
        ImGui.Spacing();

        SectionHeader(FontAwesomeIcon.PaintBrush, "Color Mode:");
        ImGui.SameLine();
        int cm = (int)cfg.ColorMode;
        bool cmc = ImGui.RadioButton("Per-channel##cm", ref cm, 0);
        SetHeaderHelpOnHover("Per-channel example: Party can be blue, Tell can be pink, and NPC Dialogue can be yellow.");
        ImGui.SameLine();
        cmc |= ImGui.RadioButton("Split##cm", ref cm, 1);
        SetHeaderHelpOnHover("Split example: player name can be gold while the message text is white.");
        ImGui.SameLine();
        cmc |= ImGui.RadioButton("Solid##cm", ref cm, 2);
        SetHeaderHelpOnHover("Solid example: every channel, name, and message uses the same color.");
        ImGui.SameLine();
        cmc |= ImGui.RadioButton("Gradient##cm", ref cm, 3);
        SetHeaderHelpOnHover("Gradient example: a name can fade blue to white, and the message can fade green to yellow.");
        if (cmc) { cfg.ColorMode = (ColorMode)cm; cfg.Save(); }

        if (cfg.ColorMode == ColorMode.Solid)
        {
            ImGui.Spacing();
            ImGui.Text("Global color:"); ImGui.SameLine();
            if (CE4("##scc", cfg.SolidColor, out var scc)) { cfg.SolidColor = scc; cfg.Save(); }
            ImGui.SameLine();
            if (ImGui.SmallButton("R##scc")) { cfg.SolidColor = new Vector4(1,1,1,1); cfg.Save(); }
        }

        ImGui.Spacing();

        if (cfg.ColorMode == ColorMode.PerChannel)
            ImGui.TextDisabled("  En   [color]  R   Channel");
        else if (cfg.ColorMode == ColorMode.Split)
            ImGui.TextDisabled("  En   Channel:   Name [color]   Message [color]   R");
        else if (cfg.ColorMode == ColorMode.Gradient)
            ImGui.TextDisabled("  En   Channel:   Name [start] [end]   Message [start] [end]   R");
        else
            ImGui.TextDisabled("  En   Channel   (all use global solid color above)");

        ImGui.Separator();
        ImGui.Spacing();

        foreach (var (from, to, label, icon) in Sections)
        {
            SectionHeader(icon, label);
            for (int i = from; i <= to && i < ChannelDefs.All.Length; i++)
            {
                var def = ChannelDefs.All[i];

                var ch      = cfg.Get(def.Key, def.DefaultColor);
                var enabled = ch.Enabled;
                if (ImGui.Checkbox($"##{def.Key}en", ref enabled)) { ch.Enabled = enabled; cfg.Save(); }
                ImGui.SameLine();

                if (cfg.ColorMode == ColorMode.PerChannel)
                {
                    if (CE4($"##{def.Key}col", ch.Color, out var c)) { ch.Color = c; cfg.Save(); }
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"R##{def.Key}cr")) { ch.Color = def.DefaultColor; cfg.Save(); }
                    ImGui.SameLine();
                    ImGui.Text(def.Label);
                }
                else if (cfg.ColorMode == ColorMode.Split)
                {
                    if (def.HasSender || IsGameLogEffect(def))
                    {
                        ImGui.Text($"{def.Label}:");
                        ImGui.SameLine();
                        ImGui.TextDisabled(def.HasSender ? "Name" : "Effect");
                        ImGui.SameLine();
                        if (CE4($"##n{def.Key}", ch.NameColor, out var nc)) { ch.NameColor = nc; cfg.Save(); }
                        ImGui.SameLine();
                        ImGui.TextDisabled("Message");
                        ImGui.SameLine();
                        if (CE4($"##m{def.Key}", ch.MsgColor, out var mc)) { ch.MsgColor = mc; cfg.Save(); }
                        ImGui.SameLine();
                        if (ImGui.SmallButton($"R##{def.Key}r"))
                        {
                            ch.NameColor = def.DefaultColor;
                            ch.MsgColor  = new Vector4(1, 1, 1, 1);
                            cfg.Save();
                        }
                    }
                    else
                    {
                        if (CE4($"##{def.Key}col", ch.Color, out var c)) { ch.Color = c; cfg.Save(); }
                        ImGui.SameLine();
                        if (ImGui.SmallButton($"R##{def.Key}cr")) { ch.Color = def.DefaultColor; cfg.Save(); }
                        ImGui.SameLine();
                        ImGui.Text(def.Label);
                    }
                }
                else if (cfg.ColorMode == ColorMode.Gradient)
                {
                    if (def.HasSender || IsGameLogEffect(def))
                    {
                        ImGui.Text($"{def.Label}:");
                        ImGui.SameLine();
                        ImGui.TextDisabled(def.HasSender ? "Name" : "Effect");
                        ImGui.SameLine();
                        if (CE4($"##g1n{def.Key}", ch.NameColor, out var nc1)) { ch.NameColor = nc1; cfg.Save(); }
                        ImGui.SameLine();
                        if (CE4($"##g2n{def.Key}", ch.NameColor2, out var nc2)) { ch.NameColor2 = nc2; cfg.Save(); }
                        ImGui.SameLine();
                        ImGui.TextDisabled("Message");
                        ImGui.SameLine();
                        if (CE4($"##g1m{def.Key}", ch.MsgColor, out var mc1)) { ch.MsgColor = mc1; cfg.Save(); }
                        ImGui.SameLine();
                        if (CE4($"##g2m{def.Key}", ch.MsgColor2, out var mc2)) { ch.MsgColor2 = mc2; cfg.Save(); }
                        ImGui.SameLine();
                        if (ImGui.SmallButton($"R##g{def.Key}r"))
                        {
                            ch.NameColor = def.DefaultColor;
                            ch.NameColor2 = new Vector4(1, 1, 1, 1);
                            ch.MsgColor = new Vector4(1, 1, 1, 1);
                            ch.MsgColor2 = new Vector4(1, 1, 1, 1);
                            cfg.Save();
                        }
                    }
                    else
                    {
                        if (CE4($"##g1{def.Key}col", ch.Color, out var c1)) { ch.Color = c1; cfg.Save(); }
                        ImGui.SameLine();
                        if (CE4($"##g2{def.Key}col", ch.MsgColor2, out var c2)) { ch.MsgColor2 = c2; cfg.Save(); }
                        ImGui.SameLine();
                        if (ImGui.SmallButton($"R##g{def.Key}cr")) { ch.Color = def.DefaultColor; ch.MsgColor2 = new Vector4(1, 1, 1, 1); cfg.Save(); }
                        ImGui.SameLine();
                        ImGui.Text(def.Label);
                    }
                }
                else
                {
                    ImGui.Text(def.Label);
                }
            }

            if (label == "Game Log")
            {
                SectionHeader(FontAwesomeIcon.Heartbeat, "Effects:");
                ImGui.SameLine();
                int ges = (int)cfg.GameLogEffectScope;
                bool gesc = ImGui.RadioButton("Only me##ges", ref ges, 0);
                ImGui.SameLine();
                gesc |= ImGui.RadioButton("Party##ges", ref ges, 2);
                ImGui.SameLine();
                gesc |= ImGui.RadioButton("All##ges", ref ges, 1);
                if (gesc) { cfg.GameLogEffectScope = (GameLogEffectScope)ges; cfg.Save(); }
                ImGui.TextDisabled("Only me shows you. Party shows you and party members. All shows everyone in the game log.");
            }

            ImGui.Spacing();
        }

    }

    private static bool IsGameLogEffect(ChannelDefs.Def def)
        => def.Types.Length == 1 && (ushort)def.Types[0] is >= 46 and <= 49;

    private void DrawDebuffHelperTab(Configuration cfg)
    {
        ImGui.Spacing();
        ImGui.TextDisabled("Debuff Helper is an ongoing feature and will be improved over time.");
        ImGui.Spacing();

        SectionHeader(FontAwesomeIcon.PowerOff, "Window");
        ImGui.Spacing();

        var enabled = cfg.DebuffHelperEnabled;
        InlineIcon(FontAwesomeIcon.ToggleOn);
        if (ImGui.Checkbox("Enable Debuff Helper", ref enabled)) { cfg.DebuffHelperEnabled = enabled; cfg.Save(); }
        SetHeaderHelpOnHover("Shows a separate helper window for important debuffs on you.");
        ImGui.SameLine();
        var locked = cfg.DebuffHelperLocked;
        InlineIcon(FontAwesomeIcon.Lock);
        if (ImGui.Checkbox("Lock helper window", ref locked)) { cfg.DebuffHelperLocked = locked; cfg.Save(); }
        SetHeaderHelpOnHover("Locked means clicks pass through the Debuff Helper. Unlock it to drag the window.");

        ImGui.Spacing();
        InlineIcon(FontAwesomeIcon.MapMarker);
        ImGui.TextDisabled($"Position: ({cfg.DebuffHelperPosition.X:F0}, {cfg.DebuffHelperPosition.Y:F0})");
        ImGui.SameLine();
        if (ImGui.SmallButton("Reset##dhpos")) { cfg.DebuffHelperPosition = new Vector2(720, 520); cfg.Save(); }
        SetHeaderHelpOnHover("Moves the Debuff Helper back to its default position.");
        ImGui.SameLine();
        if (ImGui.Button("Test Debuff Helper"))
            plugin.DebuffHelperWindow.ShowTestDebuff();
        SetHeaderHelpOnHover("Shows sample debuffs so you can preview your Debuff Helper styling.");

        ImGui.Separator();
        ImGui.Spacing();

        SectionHeader(FontAwesomeIcon.Eye, "Visible Parts");
        ImGui.Spacing();

        var showIcon = cfg.DebuffHelperShowIcon;
        InlineIcon(FontAwesomeIcon.Image);
        if (ImGui.Checkbox("Icon", ref showIcon)) { cfg.DebuffHelperShowIcon = showIcon; cfg.Save(); }
        SetHeaderHelpOnHover("Shows the debuff icon next to the debuff name.");
        ImGui.SameLine();
        var showTime = cfg.DebuffHelperShowTime;
        InlineIcon(FontAwesomeIcon.Clock);
        if (ImGui.Checkbox("Time left", ref showTime)) { cfg.DebuffHelperShowTime = showTime; cfg.Save(); }
        SetHeaderHelpOnHover("Shows how long the debuff has left.");
        ImGui.SameLine();
        var showDetails = cfg.DebuffHelperShowDetails;
        InlineIcon(FontAwesomeIcon.InfoCircle);
        if (ImGui.Checkbox("Debuff details", ref showDetails)) { cfg.DebuffHelperShowDetails = showDetails; cfg.Save(); }
        SetHeaderHelpOnHover("Shows the game's description for the debuff when available.");
        ImGui.SameLine();
        var showAdvice = cfg.DebuffHelperShowAdvice;
        InlineIcon(FontAwesomeIcon.Lightbulb);
        if (ImGui.Checkbox("Advice text", ref showAdvice)) { cfg.DebuffHelperShowAdvice = showAdvice; cfg.Save(); }
        SetHeaderHelpOnHover("Shows the short helper advice line under the debuff.");

        ImGui.Spacing();

        SectionHeader(FontAwesomeIcon.Route, "Growth direction:");
        ImGui.SameLine();
        var direction = (int)cfg.DebuffHelperGrowthDirection;
        var directionChanged = ImGui.RadioButton("Down##dhdir", ref direction, 0);
        ImGui.SameLine();
        directionChanged |= ImGui.RadioButton("Up##dhdir", ref direction, 1);
        ImGui.SameLine();
        directionChanged |= ImGui.RadioButton("Right##dhdir", ref direction, 2);
        ImGui.SameLine();
        directionChanged |= ImGui.RadioButton("Left##dhdir", ref direction, 3);
        if (directionChanged) { cfg.DebuffHelperGrowthDirection = (DebuffHelperGrowthDirection)direction; cfg.Save(); }

        var itemsPerRow = cfg.DebuffHelperItemsPerRow;
        InlineIcon(FontAwesomeIcon.ThLarge);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderInt("Items per row", ref itemsPerRow, 1, 6)) { cfg.DebuffHelperItemsPerRow = itemsPerRow; cfg.Save(); }

        ImGui.Spacing();
        SectionHeader(FontAwesomeIcon.SlidersH, "Sizing and Background");
        ImGui.Spacing();

        var iconSize = cfg.DebuffHelperIconSize;
        InlineIcon(FontAwesomeIcon.Image);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("Icon size", ref iconSize, 16f, 80f, "%.0f px")) { cfg.DebuffHelperIconSize = iconSize; cfg.Save(); }

        var wrapWidth = cfg.DebuffHelperWrapWidth;
        InlineIcon(FontAwesomeIcon.AlignLeft);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("Text wrap width##dhwrap", ref wrapWidth, 220f, 900f, "%.0f px")) { cfg.DebuffHelperWrapWidth = wrapWidth; cfg.Save(); }

        var bgOpacity = cfg.DebuffHelperBackgroundOpacity;
        InlineIcon(FontAwesomeIcon.Adjust);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("Window background opacity", ref bgOpacity, 0f, 1f, "%.2f")) { cfg.DebuffHelperBackgroundOpacity = bgOpacity; cfg.Save(); }

        var bgPadding = cfg.DebuffHelperBackgroundPadding;
        InlineIcon(FontAwesomeIcon.Expand);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("Window padding", ref bgPadding, 0f, 24f, "%.0f px")) { cfg.DebuffHelperBackgroundPadding = bgPadding; cfg.Save(); }

        var debuffSpacing = cfg.DebuffHelperDebuffSpacing;
        InlineIcon(FontAwesomeIcon.Bars);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("Debuff spacing", ref debuffSpacing, 0f, 32f, "%.0f px")) { cfg.DebuffHelperDebuffSpacing = debuffSpacing; cfg.Save(); }

        ImGui.Separator();
        ImGui.Spacing();
        SectionHeader(FontAwesomeIcon.Font, "Text Styling");
        ImGui.Spacing();

        DrawDebuffTextControls(
            "Name",
            cfg.DebuffHelperNameFontSize,
            v => cfg.DebuffHelperNameFontSize = v,
            cfg.DebuffHelperNameColor,
            v => cfg.DebuffHelperNameColor = v,
            cfg.DebuffHelperNameBackgroundColor,
            v => cfg.DebuffHelperNameBackgroundColor = v,
            cfg.DebuffHelperNameEffect,
            v => cfg.DebuffHelperNameEffect = v,
            cfg.DebuffHelperNameEffectColor,
            v => cfg.DebuffHelperNameEffectColor = v,
            cfg);

        DrawDebuffTextControls(
            "Details",
            cfg.DebuffHelperDetailsFontSize,
            v => cfg.DebuffHelperDetailsFontSize = v,
            cfg.DebuffHelperDetailsColor,
            v => cfg.DebuffHelperDetailsColor = v,
            cfg.DebuffHelperDetailsBackgroundColor,
            v => cfg.DebuffHelperDetailsBackgroundColor = v,
            cfg.DebuffHelperDetailsEffect,
            v => cfg.DebuffHelperDetailsEffect = v,
            cfg.DebuffHelperDetailsEffectColor,
            v => cfg.DebuffHelperDetailsEffectColor = v,
            cfg);

        DrawDebuffTextControls(
            "Helper text",
            cfg.DebuffHelperAdviceFontSize,
            v => cfg.DebuffHelperAdviceFontSize = v,
            cfg.DebuffHelperAdviceColor,
            v => cfg.DebuffHelperAdviceColor = v,
            cfg.DebuffHelperAdviceBackgroundColor,
            v => cfg.DebuffHelperAdviceBackgroundColor = v,
            cfg.DebuffHelperAdviceEffect,
            v => cfg.DebuffHelperAdviceEffect = v,
            cfg.DebuffHelperAdviceEffectColor,
            v => cfg.DebuffHelperAdviceEffectColor = v,
            cfg);

    }

    private static void DrawDebuffTextControls(
        string label,
        float fontSize,
        Action<float> setFontSize,
        Vector4 textColor,
        Action<Vector4> setTextColor,
        Vector4 backgroundColor,
        Action<Vector4> setBackgroundColor,
        TextEffect effect,
        Action<TextEffect> setEffect,
        Vector4 effectColor,
        Action<Vector4> setEffectColor,
        Configuration cfg)
    {
        SectionHeader(label.Contains("Name", StringComparison.OrdinalIgnoreCase) ? FontAwesomeIcon.IdCard : label.Contains("Detail", StringComparison.OrdinalIgnoreCase) ? FontAwesomeIcon.InfoCircle : FontAwesomeIcon.Lightbulb, label);
        InlineIcon(FontAwesomeIcon.Font);
        ImGui.SetNextItemWidth(170f);
        var fs = fontSize;
        if (ImGui.SliderFloat($"Font##{label}", ref fs, 10f, 48f, "%.0f px")) { setFontSize(fs); cfg.Save(); }

        InlineIcon(FontAwesomeIcon.PaintBrush);
        if (CE4($"##{label}text", textColor, out var tc)) { setTextColor(tc); cfg.Save(); }

        var eff = (int)effect;
        InlineIcon(FontAwesomeIcon.Magic);
        bool changed = ImGui.RadioButton($"None##{label}eff", ref eff, 0);
        ImGui.SameLine();
        changed |= ImGui.RadioButton($"Shadow##{label}eff", ref eff, 1);
        ImGui.SameLine();
        changed |= ImGui.RadioButton($"Outline##{label}eff", ref eff, 2);
        if (changed) { setEffect((TextEffect)eff); cfg.Save(); }

        InlineIcon(FontAwesomeIcon.Tint);
        ImGui.TextDisabled("Effect color");
        ImGui.SameLine();
        if (CE4($"##{label}effcol", effectColor, out var ec)) { setEffectColor(ec); cfg.Save(); }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    private void DrawCastHelperTab(Configuration cfg)
    {
        ImGui.Spacing();
        ImGui.TextDisabled("Boss Helper is an ongoing feature and will be improved over time.");
        ImGui.TextDisabled("It checks your target, focus target, and nearby enemies for supported boss casts.");
        ImGui.Spacing();

        SectionHeader(FontAwesomeIcon.PowerOff, "Window");
        ImGui.Spacing();

        var enabled = cfg.CastHelperEnabled;
        InlineIcon(FontAwesomeIcon.ToggleOn);
        if (ImGui.Checkbox("Enable Boss Helper", ref enabled)) { cfg.CastHelperEnabled = enabled; cfg.Save(); }
        SetHeaderHelpOnHover("Shows a separate helper window when supported bosses start important casts.");
        ImGui.SameLine();
        var locked = cfg.CastHelperLocked;
        InlineIcon(FontAwesomeIcon.Lock);
        if (ImGui.Checkbox("Lock helper window##cast", ref locked)) { cfg.CastHelperLocked = locked; cfg.Save(); }
        SetHeaderHelpOnHover("Locked means clicks pass through the Boss Helper. Unlock it to drag the window.");

        ImGui.Spacing();
        InlineIcon(FontAwesomeIcon.MapMarker);
        ImGui.TextDisabled($"Position: ({cfg.CastHelperPosition.X:F0}, {cfg.CastHelperPosition.Y:F0})");
        ImGui.SameLine();
        if (ImGui.SmallButton("Reset##chpos")) { cfg.CastHelperPosition = new Vector2(760, 420); cfg.Save(); }
        SetHeaderHelpOnHover("Moves the Boss Helper back to its default position.");
        ImGui.SameLine();
        if (ImGui.Button("Test Boss Helper"))
            plugin.CastHelperWindow.ShowTestCast();
        SetHeaderHelpOnHover("Shows a sample boss cast so you can preview your Boss Helper styling.");

        ImGui.Separator();
        ImGui.Spacing();

        SectionHeader(FontAwesomeIcon.Eye, "Visible Parts");
        ImGui.Spacing();

        var showIcon = cfg.CastHelperShowIcon;
        InlineIcon(FontAwesomeIcon.Image);
        if (ImGui.Checkbox("Icon##cast", ref showIcon)) { cfg.CastHelperShowIcon = showIcon; cfg.Save(); }
        SetHeaderHelpOnHover("Shows the action icon next to the cast name.");
        ImGui.SameLine();
        var showTime = cfg.CastHelperShowTime;
        InlineIcon(FontAwesomeIcon.Clock);
        if (ImGui.Checkbox("Time left##cast", ref showTime)) { cfg.CastHelperShowTime = showTime; cfg.Save(); }
        SetHeaderHelpOnHover("Shows the countdown left on the boss cast.");
        ImGui.SameLine();
        var showDetails = cfg.CastHelperShowDetails;
        InlineIcon(FontAwesomeIcon.InfoCircle);
        if (ImGui.Checkbox("Cast details", ref showDetails)) { cfg.CastHelperShowDetails = showDetails; cfg.Save(); }
        SetHeaderHelpOnHover("Shows the short line describing what the boss is casting.");
        ImGui.SameLine();
        var showAdvice = cfg.CastHelperShowAdvice;
        InlineIcon(FontAwesomeIcon.Lightbulb);
        if (ImGui.Checkbox("Advice text##cast", ref showAdvice)) { cfg.CastHelperShowAdvice = showAdvice; cfg.Save(); }
        SetHeaderHelpOnHover("Shows the mechanic advice line under the cast.");

        var keepLast = cfg.CastHelperKeepLastUntilNextCast;
        InlineIcon(FontAwesomeIcon.History);
        if (ImGui.Checkbox("Keep last alert until next cast", ref keepLast)) { cfg.CastHelperKeepLastUntilNextCast = keepLast; cfg.Save(); }
        SetHeaderHelpOnHover("Keeps the last boss alert visible longer so you have time to read it.");

        ImGui.Spacing();
        SectionHeader(FontAwesomeIcon.SlidersH, "Sizing and Background");
        ImGui.Spacing();

        var iconSize = cfg.CastHelperIconSize;
        InlineIcon(FontAwesomeIcon.Image);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("Icon size##cast", ref iconSize, 16f, 80f, "%.0f px")) { cfg.CastHelperIconSize = iconSize; cfg.Save(); }

        var wrapWidth = cfg.CastHelperWrapWidth;
        InlineIcon(FontAwesomeIcon.AlignLeft);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("Text wrap width##chwrap", ref wrapWidth, 220f, 900f, "%.0f px")) { cfg.CastHelperWrapWidth = wrapWidth; cfg.Save(); }

        var bgOpacity = cfg.CastHelperBackgroundOpacity;
        InlineIcon(FontAwesomeIcon.Adjust);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("Window background opacity##cast", ref bgOpacity, 0f, 1f, "%.2f")) { cfg.CastHelperBackgroundOpacity = bgOpacity; cfg.Save(); }

        var bgPadding = cfg.CastHelperBackgroundPadding;
        InlineIcon(FontAwesomeIcon.Expand);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("Window padding##cast", ref bgPadding, 0f, 24f, "%.0f px")) { cfg.CastHelperBackgroundPadding = bgPadding; cfg.Save(); }

        var castSpacing = cfg.CastHelperCastSpacing;
        InlineIcon(FontAwesomeIcon.Bars);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("Cast spacing", ref castSpacing, 0f, 32f, "%.0f px")) { cfg.CastHelperCastSpacing = castSpacing; cfg.Save(); }

        ImGui.Separator();
        ImGui.Spacing();
        SectionHeader(FontAwesomeIcon.Font, "Text Styling");
        ImGui.Spacing();

        DrawDebuffTextControls(
            "Cast Name",
            cfg.CastHelperNameFontSize,
            v => cfg.CastHelperNameFontSize = v,
            cfg.CastHelperNameColor,
            v => cfg.CastHelperNameColor = v,
            Vector4.Zero,
            _ => { },
            cfg.CastHelperNameEffect,
            v => cfg.CastHelperNameEffect = v,
            cfg.CastHelperNameEffectColor,
            v => cfg.CastHelperNameEffectColor = v,
            cfg);

        DrawDebuffTextControls(
            "Cast Details",
            cfg.CastHelperDetailsFontSize,
            v => cfg.CastHelperDetailsFontSize = v,
            cfg.CastHelperDetailsColor,
            v => cfg.CastHelperDetailsColor = v,
            Vector4.Zero,
            _ => { },
            cfg.CastHelperDetailsEffect,
            v => cfg.CastHelperDetailsEffect = v,
            cfg.CastHelperDetailsEffectColor,
            v => cfg.CastHelperDetailsEffectColor = v,
            cfg);

        DrawDebuffTextControls(
            "Boss Helper text",
            cfg.CastHelperAdviceFontSize,
            v => cfg.CastHelperAdviceFontSize = v,
            cfg.CastHelperAdviceColor,
            v => cfg.CastHelperAdviceColor = v,
            Vector4.Zero,
            _ => { },
            cfg.CastHelperAdviceEffect,
            v => cfg.CastHelperAdviceEffect = v,
            cfg.CastHelperAdviceEffectColor,
            v => cfg.CastHelperAdviceEffectColor = v,
            cfg);

        SectionHeader(FontAwesomeIcon.PaintBrush, "Important Colors");
        ImGui.TextDisabled("Important text color");
        ImGui.SameLine();
        if (CE4("##chimportant", cfg.CastHelperImportantColor, out var important)) { cfg.CastHelperImportantColor = important; cfg.Save(); }
        ImGui.SameLine();
        ImGui.TextDisabled("Blue");
        ImGui.SameLine();
        if (CE4("##chblue", cfg.CastHelperBlueColor, out var blue)) { cfg.CastHelperBlueColor = blue; cfg.Save(); }
        ImGui.SameLine();
        ImGui.TextDisabled("Yellow");
        ImGui.SameLine();
        if (CE4("##chyellow", cfg.CastHelperYellowColor, out var yellow)) { cfg.CastHelperYellowColor = yellow; cfg.Save(); }

    }

    private void DrawPriorityTab(Configuration cfg)
    {
        NormalizePriorityWords(cfg);
        ImGui.Spacing();

        SectionHeader(FontAwesomeIcon.Star, "Priority Matching");
        ImGui.Spacing();

        var en = cfg.EnablePriority;
        InlineIcon(FontAwesomeIcon.ToggleOn);
        if (ImGui.Checkbox("Enable priority highlighting", ref en)) { cfg.EnablePriority = en; cfg.Save(); }
        SetHeaderHelpOnHover("Highlights important words like stack, spread, or tank swap when they appear in chat.");

        if (cfg.EnablePriority)
        {
            ImGui.SameLine();
            if (CE4("##pc", cfg.PriorityColor, out var pc)) { cfg.PriorityColor = pc; cfg.Save(); }
            SetHeaderHelpOnHover("Choose the color used for priority words.");
            ImGui.SameLine();
            if (ImGui.SmallButton("R##pc")) { cfg.PriorityColor = new Vector4(1, 0.25f, 0.25f, 1); cfg.Save(); }
            SetHeaderHelpOnHover("Reset the priority highlight color.");
        }

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

        var po = cfg.PriorityOnly;
        InlineIcon(FontAwesomeIcon.Filter);
        if (ImGui.Checkbox("Priority Only Messages", ref po)) { cfg.PriorityOnly = po; cfg.Save(); }
        SetHeaderHelpOnHover("Only shows chat messages that include one of your priority words.");

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
        ImGui.TextWrapped(
            "When enabled, only messages containing a keyword from the list below " +
            "will appear in the banner. The status indicator at the top of the settings " +
            "window will turn red to remind you that filtering is active.");
        ImGui.PopStyleColor();

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

        SectionHeader(FontAwesomeIcon.Plus, "Words and Phrases");
        ImGui.Spacing();

        ImGui.TextDisabled("Add a word or phrase and click Add.");
        ImGui.TextDisabled("Matching is case-insensitive, whole-word only.");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(200f);
        ImGui.InputText("##nw", ref newPriorityWord, 64);
        ImGui.SameLine();
        if (ImGui.Button("Add") && !string.IsNullOrWhiteSpace(newPriorityWord))
        {
            var w = NormalizePriorityWord(newPriorityWord);
            if (!string.IsNullOrWhiteSpace(w) && !PriorityWordExists(cfg, w))
            {
                cfg.PriorityWords.Add(w);
                cfg.Save();
            }
            newPriorityWord = string.Empty;
        }

        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.18f, 0.18f, 0.24f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 6f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,   new Vector2(8f, 6f));

        float listW = ImGui.GetContentRegionAvail().X * 0.75f;
        if (ImGui.BeginChild("##wl", new Vector2(listW, 200), true))
        {
            float innerW = ImGui.GetContentRegionAvail().X;
            for (int i = cfg.PriorityWords.Count - 1; i >= 0; i--)
            {
                ImGui.Text(cfg.PriorityWords[i]);
                ImGui.SameLine(innerW - 60f);
                if (ImGui.SmallButton($"Remove##{i}")) { cfg.PriorityWords.RemoveAt(i); cfg.Save(); }
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();

    }

    private static void NormalizePriorityWords(Configuration cfg)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        for (var i = cfg.PriorityWords.Count - 1; i >= 0; i--)
        {
            var normalized = NormalizePriorityWord(cfg.PriorityWords[i]);
            if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
            {
                cfg.PriorityWords.RemoveAt(i);
                changed = true;
                continue;
            }

            if (cfg.PriorityWords[i] != normalized)
            {
                cfg.PriorityWords[i] = normalized;
                changed = true;
            }
        }

        if (changed)
            cfg.Save();
    }

    private static bool PriorityWordExists(Configuration cfg, string word)
    {
        foreach (var existing in cfg.PriorityWords)
        {
            if (string.Equals(NormalizePriorityWord(existing), word, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string NormalizePriorityWord(string word)
        => string.Join(' ', word.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
