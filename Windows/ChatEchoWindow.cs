using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;

namespace ChatEcho.Windows;

public sealed class ChatEchoWindow : Window
{
    private readonly Plugin plugin;
    private readonly List<ChatMessage> messages = new();
    private readonly object messageLock = new();
    private ChatMessage? lastExpiredMessage;
    private bool hasMessages;
    private bool showFaded;
    private bool shouldDraw;
    private bool stylePushed;

    public ChatEchoWindow(Plugin plugin)
        : base("Chat Echo  --  Drag title bar, then Lock###ChatEchoOverlay")
    {
        this.plugin = plugin;
        IsOpen = true;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        DisableFadeInFadeOut = true;
    }

    public void AddMessage(XivChatType type, string sender, string text)
    {
        var cfg = plugin.Configuration;

        // If Priority Only mode is on, drop messages with no keyword match
        if (cfg.PriorityOnly && cfg.EnablePriority && cfg.PriorityWords.Count > 0)
        {
            if (!ContainsPriorityWord(text, cfg.PriorityWords))
                return;
        }

        lock (messageLock)
        {
            messages.Add(new ChatMessage(type, sender, text));
            if (messages.Count > cfg.MaxMessages)
                messages.RemoveAt(0);
        }
    }

    /// <summary>Returns true if the text contains at least one priority keyword at a word boundary.</summary>
    private static bool ContainsPriorityWord(string text, System.Collections.Generic.List<string> kws)
    {
        foreach (var kw in kws)
        {
            int searchFrom = 0;
            while (searchFrom < text.Length)
            {
                int i = text.IndexOf(kw, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (i < 0) break;
                if (IsBoundary(text, i - 1) && IsBoundary(text, i + kw.Length))
                    return true;
                searchFrom = i + 1;
            }
        }
        return false;
    }

    private static string FormatSender(string raw, bool firstNameOnly)
    {
        string name = raw.Length > 0 && char.IsDigit(raw[0]) ? raw[1..].TrimStart() : raw;
        if (firstNameOnly)
        {
            int sp = name.IndexOf(' ');
            if (sp > 0) name = name[..sp];
        }
        return name;
    }

    /// <summary>
    /// Returns true if the character is a word boundary (space, punctuation, or string edge).
    /// Used to prevent "out" matching inside "outside".
    /// </summary>
    private static bool IsBoundary(string s, int idx)
    {
        if (idx < 0 || idx >= s.Length) return true;
        return !char.IsLetterOrDigit(s[idx]);
    }

    private static List<(string text, bool priority)> Tokenize(string text, List<string> kws)
    {
        var result = new List<(string, bool)>();
        var rem    = text;
        while (!string.IsNullOrEmpty(rem))
        {
            int     bestIdx = -1;
            string? bestKw  = null;

            foreach (var kw in kws)
            {
                int searchFrom = 0;
                while (searchFrom < rem.Length)
                {
                    int i = rem.IndexOf(kw, searchFrom, StringComparison.OrdinalIgnoreCase);
                    if (i < 0) break;

                    // Whole-word check: chars before and after must be boundaries
                    bool before = IsBoundary(rem, i - 1);
                    bool after  = IsBoundary(rem, i + kw.Length);

                    if (before && after)
                    {
                        if (bestIdx < 0 || i < bestIdx) { bestIdx = i; bestKw = kw; }
                        break;
                    }
                    searchFrom = i + 1; // skip past this non-boundary match and keep looking
                }
            }

            if (bestIdx < 0 || bestKw == null) { result.Add((rem, false)); break; }
            if (bestIdx > 0) result.Add((rem[..bestIdx], false));
            result.Add((rem.Substring(bestIdx, bestKw.Length), true));
            rem = rem[(bestIdx + bestKw.Length)..];
        }
        return result;
    }

    private static void Seg(ImDrawListPtr dl, ImFontPtr font, float sz,
                            ref float x, float y, string text,
                            Vector4 color, Configuration cfg, float alpha)
    {
        if (string.IsNullOrEmpty(text)) return;
        var c   = color with { W = color.W * alpha };
        var pos = new Vector2(x, y);

        if (cfg.TextEffect == TextEffect.Shadow)
        {
            var sc = cfg.ShadowColor with { W = cfg.ShadowColor.W * alpha };
            dl.AddText(font, sz, new Vector2(x + 2, y + 2), ImGui.ColorConvertFloat4ToU32(sc), text);
        }
        else if (cfg.TextEffect == TextEffect.Outline)
        {
            uint ou = ImGui.ColorConvertFloat4ToU32(cfg.OutlineColor with { W = cfg.OutlineColor.W * alpha });
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                dl.AddText(font, sz, new Vector2(x + dx, y + dy), ou, text);
            }
        }

        dl.AddText(font, sz, pos, ImGui.ColorConvertFloat4ToU32(c), text);
        x += ImGui.CalcTextSize(text).X;
    }

    // Cached line height — recalculated only when font size changes
    private float cachedLineH   = 0f;
    private float cachedFontSz  = 0f;

    // Drag debounce — only save position when mouse is released
    private bool  dragging      = false;

    public override void PreDraw()
    {
        var cfg = plugin.Configuration;

        lock (messageLock)
        {
            // Single-pass expiry — no LINQ allocation every frame
            ChatMessage? lastExp = null;
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].IsExpired(cfg.DisplayDuration))
                {
                    if (lastExp == null || messages[i].AddedAt > lastExp.AddedAt)
                        lastExp = messages[i];
                    messages.RemoveAt(i);
                }
            }
            if (lastExp != null) lastExpiredMessage = lastExp;
            hasMessages = messages.Count > 0;
        }

        showFaded = cfg.ShowLastFaded
            && !hasMessages
            && lastExpiredMessage != null
            && (DateTime.Now - lastExpiredMessage.AddedAt).TotalSeconds < cfg.DisplayDuration * 2.0;

        shouldDraw = !cfg.Locked || hasMessages || showFaded;
        IsOpen = true;
        Position = cfg.BannerPosition;
        PositionCondition = cfg.Locked ? ImGuiCond.Always : ImGuiCond.FirstUseEver;
        BgAlpha = cfg.Locked ? cfg.BackgroundOpacity : 0.7f;

        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar
              | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings
              | ImGuiWindowFlags.NoCollapse;

        if (cfg.Locked)
        {
            Flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove
                   | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.AlwaysAutoResize;
            Size = null;
        }
        else
        {
            Flags |= ImGuiWindowFlags.NoFocusOnAppearing;
            Size = new Vector2(Math.Max(380f, cfg.FontSize * 14f), Math.Max(90f, cfg.FontSize * 2.8f));
            SizeCondition = ImGuiCond.Always;
        }

        if (shouldDraw)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(cfg.BackgroundPadding, cfg.BackgroundPadding));
            stylePushed = true;
        }
    }

    public override bool DrawConditions() => shouldDraw;

    public override void Draw()
    {
        var cfg = plugin.Configuration;

        if (!cfg.Locked)
        {
            var pos = ImGui.GetWindowPos();
            if (pos != cfg.BannerPosition)
            {
                cfg.BannerPosition = pos;
                dragging = true;
            }

            // Only write to disk when drag ends (mouse released) — not 60x per second
            if (dragging && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                cfg.Save();
                dragging = false;
            }
        }

        DrawContent(cfg, hasMessages, showFaded);
        ImGui.SetWindowFontScale(1f);
    }

    public override void PostDraw()
    {
        if (!stylePushed) return;

        ImGui.PopStyleVar();
        stylePushed = false;
    }

    private void DrawContent(Configuration cfg, bool hasMessages, bool showFaded)
    {
        float fontScale = cfg.FontSize / ImGui.GetFontSize();
        ImGui.SetWindowFontScale(fontScale);

        if (!cfg.Enabled)
        {
            ImGui.TextDisabled("[ Chat Echo DISABLED ]");
            return;
        }

        if (!hasMessages)
        {
            if (showFaded && lastExpiredMessage != null)
                RenderMessage(lastExpiredMessage, cfg, 0.2f);
            else if (!cfg.Locked)
            {
                string n = cfg.FirstNameOnly ? "Moenbryda" : "Moenbryda Vrai";
                ImGui.TextDisabled($"[ {n}: Stack on A marker ]");
            }
            return;
        }

        List<ChatMessage> snap;
        lock (messageLock)
        {
            if (messages.Count == 0) return;
            snap = new List<ChatMessage>(messages);
        }
        foreach (var msg in snap)
            RenderMessage(msg, cfg, msg.GetAlpha(cfg.DisplayDuration));
    }

    private void RenderMessage(ChatMessage msg, Configuration cfg, float alpha)
    {
        // O(1) def lookup via pre-built dictionary — no linear scan
        var key = ChannelDefs.KeyFor(msg.Type);
        var def = key != null ? ChannelDefs.ByKey(key) : null;
        var ch  = key != null ? cfg.Get(key, def?.DefaultColor ?? new Vector4(1,1,1,1)) : null;

        Vector4 nameColor, msgColor;
        switch (cfg.ColorMode)
        {
            case ColorMode.Solid:
                nameColor = msgColor = cfg.SolidColor;
                break;
            case ColorMode.Split:
                nameColor = ch?.NameColor ?? new Vector4(1,1,1,1);
                msgColor  = ch?.MsgColor  ?? new Vector4(1,1,1,1);
                break;
            default:
                nameColor = msgColor = ch?.Color ?? new Vector4(1,1,1,1);
                break;
        }

        string prefix  = cfg.ShowChannelPrefix && def != null ? $"({def.Label}) " : "";
        string sender  = FormatSender(msg.Sender, cfg.FirstNameOnly);
        var    scrPos  = ImGui.GetCursorScreenPos();
        var    dl      = ImGui.GetWindowDrawList();
        var    font    = ImGui.GetFont();
        float  sz      = cfg.FontSize;
        float  x       = scrPos.X, y = scrPos.Y;

        Seg(dl, font, sz, ref x, y, prefix + sender + ": ", nameColor, cfg, alpha);

        if (cfg.EnablePriority && cfg.PriorityWords.Count > 0)
        {
            foreach (var (seg, isPri) in Tokenize(msg.Text, cfg.PriorityWords))
                Seg(dl, font, sz, ref x, y, seg, isPri ? cfg.PriorityColor : msgColor, cfg, alpha);
        }
        else
        {
            Seg(dl, font, sz, ref x, y, msg.Text, msgColor, cfg, alpha);
        }

        // Cache line height — only recalculate when font size changes
        if (Math.Abs(cachedFontSz - sz) > 0.01f)
        {
            cachedLineH  = ImGui.CalcTextSize("A").Y + 2f;
            cachedFontSz = sz;
        }
        ImGui.Dummy(new Vector2(Math.Max(x - scrPos.X, 10f), cachedLineH));
    }
}
