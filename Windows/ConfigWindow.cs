using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace ChatEcho.Windows;

public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;
    private string newPriorityWord = string.Empty;

    // Section labels for Channels tab
    // Indices into ChannelDefs.All — update if channels are added/removed
    private static readonly (int from, int to, string label)[] Sections =
    {
        (0,  2,  "Combat / Raid"),   // party, alliance, pvpteam
        (3,  8,  "Social"),          // say, shout, yell, tell, fc, novice
        (9,  10, "Linkshells"),      // ls, cls
        (11, 14, "System"),          // echo, system, notice, urgent
    };

    public ConfigWindow(Plugin plugin) : base("Chat Echo — Settings")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 460),
            MaximumSize = new Vector2(820, 820)
        };
        Size = new Vector2(560, 600);
    }

    // CE4: color picker without needing ref on a property
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

        // Master toggle
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
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Toggle the overlay.\n/chatecho on  |  /chatecho off");

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

        if (!ImGui.BeginTabBar("##tabs")) return;
        DrawGeneralTab(cfg);
        DrawDisplayTab(cfg);
        DrawChannelsTab(cfg);
        DrawPriorityTab(cfg);
        ImGui.EndTabBar();
    }

    // ── General ──────────────────────────────────────────────────────

    private void DrawGeneralTab(Configuration cfg)
    {
        if (!ImGui.BeginTabItem("General")) return;
        ImGui.Spacing();

        var dur = cfg.DisplayDuration;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("Display Duration (s)", ref dur, 0.5f, 15f, "%.1f s"))
        { cfg.DisplayDuration = dur; cfg.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("How long each message stays on screen.");

        var maxMsg = cfg.MaxMessages;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderInt("Max Messages", ref maxMsg, 1, 10))
        { cfg.MaxMessages = maxMsg; cfg.Save(); }

        var fs = cfg.FontSize;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("Font Size", ref fs, 10f, 72f, "%.0f px"))
        { cfg.FontSize = fs; cfg.Save(); }

        var showLast = cfg.ShowLastFaded;
        if (ImGui.Checkbox("Show last message faded after expiry", ref showLast))
        { cfg.ShowLastFaded = showLast; cfg.Save(); }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Keeps the last message visible at 20% opacity so you can glance back.");

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

        // Lock position here in General so it's always easy to find
        var locked = cfg.Locked;
        if (ImGui.Checkbox("Lock banner position (click-through)", ref locked))
        { cfg.Locked = locked; cfg.Save(); }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("LOCKED — fully click-through, perfect for combat.\nUNLOCKED — drag the banner's title bar to reposition it.");

        ImGui.Spacing();
        ImGui.TextDisabled($"Banner position: ({cfg.BannerPosition.X:F0}, {cfg.BannerPosition.Y:F0})");
        if (ImGui.SmallButton("Reset to centre")) { cfg.BannerPosition = new Vector2(600, 400); cfg.Save(); }

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

        if (ImGui.Button("Run Test Messages"))
            plugin.RunTestMessages();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Fires fake raid callouts one at a time so you can preview the banner.\nAlso: /chatecho test");

        ImGui.Spacing();
        ImGui.TextDisabled("/chatecho            open settings");
        ImGui.TextDisabled("/chatecho on / off   enable or disable");
        ImGui.TextDisabled("/chatecho test       preview test messages");

        ImGui.EndTabItem();
    }

    // ── Display ──────────────────────────────────────────────────────

    private static void DrawDisplayTab(Configuration cfg)
    {
        if (!ImGui.BeginTabItem("Display")) return;
        ImGui.Spacing();

        var opacity = cfg.BackgroundOpacity;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("Background Opacity", ref opacity, 0f, 1f, "%.2f"))
        { cfg.BackgroundOpacity = opacity; cfg.Save(); }

        var padding = cfg.BackgroundPadding;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("Background Padding", ref padding, 0f, 20f, "%.0f px"))
        { cfg.BackgroundPadding = padding; cfg.Save(); }

        ImGui.Spacing(); ImGui.Separator(); ImGui.Text("Text Effect"); ImGui.Spacing();

        int eff = (int)cfg.TextEffect;
        bool ec = ImGui.RadioButton("None",    ref eff, 0); ImGui.SameLine();
        ec |=     ImGui.RadioButton("Shadow",  ref eff, 1); ImGui.SameLine();
        ec |=     ImGui.RadioButton("Outline", ref eff, 2);
        if (ec) { cfg.TextEffect = (TextEffect)eff; cfg.Save(); }

        if (cfg.TextEffect == TextEffect.Shadow)
        {
            if (CE4("Shadow colour##shc", cfg.ShadowColor, out var sc)) { cfg.ShadowColor = sc; cfg.Save(); }
            ImGui.SameLine();
            if (ImGui.SmallButton("R##shc")) { cfg.ShadowColor = new Vector4(0,0,0,0.8f); cfg.Save(); }
        }
        if (cfg.TextEffect == TextEffect.Outline)
        {
            if (CE4("Outline colour##olc", cfg.OutlineColor, out var oc)) { cfg.OutlineColor = oc; cfg.Save(); }
            ImGui.SameLine();
            if (ImGui.SmallButton("R##olc")) { cfg.OutlineColor = new Vector4(0,0,0,1); cfg.Save(); }
        }

        ImGui.Spacing(); ImGui.Separator(); ImGui.Text("Name Format"); ImGui.Spacing();

        var pref = cfg.ShowChannelPrefix;
        if (ImGui.Checkbox("Show channel prefix  e.g. (Party)", ref pref))
        { cfg.ShowChannelPrefix = pref; cfg.Save(); }

        var fn = cfg.FirstNameOnly;
        if (ImGui.Checkbox("First name only", ref fn))
        { cfg.FirstNameOnly = fn; cfg.Save(); }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("e.g. 'Moenbryda' instead of 'Moenbryda Vrai'");

        ImGui.EndTabItem();
    }

    // ── Channels ─────────────────────────────────────────────────────

    private static void DrawChannelsTab(Configuration cfg)
    {
        if (!ImGui.BeginTabItem("Channels")) return;
        ImGui.Spacing();

        // Color mode selector
        ImGui.Text("Color Mode:");
        ImGui.SameLine();
        int cm = (int)cfg.ColorMode;
        bool cmc = ImGui.RadioButton("Per-channel##cm", ref cm, 0);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("One color per channel.");
        ImGui.SameLine();
        cmc |= ImGui.RadioButton("Split##cm", ref cm, 1);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Separate Name and Message text colors per channel.");
        ImGui.SameLine();
        cmc |= ImGui.RadioButton("Solid##cm", ref cm, 2);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("One global color for all text.");
        if (cmc) { cfg.ColorMode = (ColorMode)cm; cfg.Save(); }

        // Solid color picker shown inline when Solid is selected
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
        else
            ImGui.TextDisabled("  En   Channel   (all use global solid color above)");

        ImGui.Separator();
        ImGui.Spacing();

        foreach (var (from, to, label) in Sections)
        {
            ImGui.Text(label);
            for (int i = from; i <= to && i < ChannelDefs.All.Length; i++)
            {
                var def = ChannelDefs.All[i];

                var ch      = cfg.Get(def.Key, def.DefaultColor);
                var enabled = ch.Enabled;
                if (ImGui.Checkbox($"##{def.Key}en", ref enabled)) { ch.Enabled = enabled; cfg.Save(); }
                ImGui.SameLine();

                if (cfg.ColorMode == ColorMode.PerChannel)
                {
                    // En  [Color]  R  Channel Label
                    if (CE4($"##{def.Key}col", ch.Color, out var c)) { ch.Color = c; cfg.Save(); }
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"R##{def.Key}cr")) { ch.Color = def.DefaultColor; cfg.Save(); }
                    ImGui.SameLine();
                    ImGui.Text(def.Label);
                }
                else if (cfg.ColorMode == ColorMode.Split)
                {
                    if (def.HasSender)
                    {
                        // En  Channel:  Name [Color]  Message [Color]  R
                        ImGui.Text($"{def.Label}:");
                        ImGui.SameLine();
                        ImGui.TextDisabled("Name");
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
                        // No sender name — just one color picker like Per-channel mode
                        if (CE4($"##{def.Key}col", ch.Color, out var c)) { ch.Color = c; cfg.Save(); }
                        ImGui.SameLine();
                        if (ImGui.SmallButton($"R##{def.Key}cr")) { ch.Color = def.DefaultColor; cfg.Save(); }
                        ImGui.SameLine();
                        ImGui.Text(def.Label);
                    }
                }
                else // Solid
                {
                    ImGui.Text(def.Label);
                }
            }
            ImGui.Spacing();
        }

        ImGui.EndTabItem();
    }

    // ── Priority ─────────────────────────────────────────────────────

    private void DrawPriorityTab(Configuration cfg)
    {
        if (!ImGui.BeginTabItem("Priority")) return;
        ImGui.Spacing();

        // ── Highlight toggle ─────────────────────────────────────────
        var en = cfg.EnablePriority;
        if (ImGui.Checkbox("Enable priority highlighting", ref en)) { cfg.EnablePriority = en; cfg.Save(); }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "Words in the list below will be highlighted in a different color\n" +
                "when they appear in a message. Useful for catching important callouts\n" +
                "like 'stack', 'spread', or 'tank swap' while tunnel-visioning in a fight.\n\n" +
                "Uses whole-word matching — 'out' will NOT highlight inside 'outside'.");

        if (cfg.EnablePriority)
        {
            ImGui.SameLine();
            if (CE4("##pc", cfg.PriorityColor, out var pc)) { cfg.PriorityColor = pc; cfg.Save(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Color used to highlight priority words.");
            ImGui.SameLine();
            if (ImGui.SmallButton("R##pc")) { cfg.PriorityColor = new Vector4(1, 0.25f, 0.25f, 1); cfg.Save(); }
        }

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

        // ── Priority Only toggle ─────────────────────────────────────
        var po = cfg.PriorityOnly;
        if (ImGui.Checkbox("Priority Only Messages", ref po)) { cfg.PriorityOnly = po; cfg.Save(); }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "Only show messages that contain at least one keyword\n" +
                "from the list below. All other chat is silently ignored.");

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
        ImGui.TextWrapped(
            "When enabled, only messages containing a keyword from the list below " +
            "will appear in the banner. The status indicator at the top of the settings " +
            "window will turn red to remind you that filtering is active.");
        ImGui.PopStyleColor();

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

        // ── Word list ────────────────────────────────────────────────
        ImGui.TextDisabled("Add a word or phrase and click Add.");
        ImGui.TextDisabled("Matching is case-insensitive, whole-word only.");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(200f);
        ImGui.InputText("##nw", ref newPriorityWord, 64);
        ImGui.SameLine();
        if (ImGui.Button("Add") && !string.IsNullOrWhiteSpace(newPriorityWord))
        {
            var w = newPriorityWord.Trim().ToLowerInvariant();
            if (!cfg.PriorityWords.Contains(w)) { cfg.PriorityWords.Add(w); cfg.Save(); }
            newPriorityWord = string.Empty;
        }

        ImGui.Spacing();

        // Narrower child with padding and slightly offset background color
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.18f, 0.18f, 0.24f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 6f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,   new Vector2(8f, 6f));

        // Use 75% of available width so it doesn't stretch edge to edge
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

        ImGui.EndTabItem();
    }
}
