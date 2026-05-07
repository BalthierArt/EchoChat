using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;

namespace ChatEcho.Windows;

public sealed class ChatEchoWindow : Window
{
    private readonly record struct RenderSegment(string Text, Vector4 Color, Vector4? Color2 = null);

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

                    bool before = IsBoundary(rem, i - 1);
                    bool after  = IsBoundary(rem, i + kw.Length);

                    if (before && after)
                    {
                        if (bestIdx < 0 || i < bestIdx) { bestIdx = i; bestKw = kw; }
                        break;
                    }
                    searchFrom = i + 1;
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

    private static void SegGradient(ImDrawListPtr dl, ImFontPtr font, float sz,
                                    ref float x, float y, string text,
                                    Vector4 startColor, Vector4 endColor, Configuration cfg, float alpha)
    {
        if (string.IsNullOrEmpty(text)) return;

        var totalWidth = Math.Max(ImGui.CalcTextSize(text).X, 1f);
        var origin = x;
        foreach (var ch in text)
        {
            var s = ch.ToString();
            var width = ImGui.CalcTextSize(s).X;
            var t = Math.Clamp((x - origin) / totalWidth, 0f, 1f);
            var color = Vector4.Lerp(startColor, endColor, t);
            Seg(dl, font, sz, ref x, y, s, color, cfg, alpha);
            if (width <= 0f)
                x += ImGui.CalcTextSize(" ").X;
        }
    }

    private static void SegGradientPart(ImDrawListPtr dl, ImFontPtr font, float sz,
                                        ref float x, float y, string text,
                                        Vector4 startColor, Vector4 endColor, Configuration cfg, float alpha,
                                        float totalWidth, ref float drawnWidth)
    {
        if (string.IsNullOrEmpty(text)) return;

        foreach (var ch in text)
        {
            var s = ch.ToString();
            var width = ImGui.CalcTextSize(s).X;
            var t = Math.Clamp(drawnWidth / Math.Max(totalWidth, 1f), 0f, 1f);
            var color = Vector4.Lerp(startColor, endColor, t);
            Seg(dl, font, sz, ref x, y, s, color, cfg, alpha);
            drawnWidth += width;
            if (width <= 0f)
                drawnWidth += ImGui.CalcTextSize(" ").X;
        }
    }

    private float cachedLineH   = 0f;
    private float cachedFontSz  = 0f;

    private bool  dragging      = false;

    public override void PreOpenCheck()
    {
        IsOpen = true;
    }

    public override void Update()
    {
        var cfg = plugin.Configuration;

        lock (messageLock)
        {
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
        Position = cfg.BannerPosition;
        PositionCondition = cfg.Locked ? ImGuiCond.Always : ImGuiCond.FirstUseEver;
        BgAlpha = cfg.Locked ? cfg.BackgroundOpacity : 0.7f;

        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar
              | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings
              | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing
              | ImGuiWindowFlags.NoBringToFrontOnFocus;

        if (cfg.Locked)
        {
            Flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove
                   | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.AlwaysAutoResize;
            Size = null;
        }
        else
        {
            Size = new Vector2(Math.Max(380f, cfg.FontSize * 14f), Math.Max(90f, cfg.FontSize * 2.8f));
            SizeCondition = ImGuiCond.Always;
        }
    }

    public override void PreDraw()
    {
        var cfg = plugin.Configuration;
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
        var key = ChannelDefs.KeyFor(msg.Type);
        var def = key != null ? ChannelDefs.ByKey(key) : null;
        var ch  = key != null ? cfg.Get(key, def?.DefaultColor ?? new Vector4(1,1,1,1)) : null;

        Vector4 nameColor, msgColor, nameColor2, msgColor2;
        switch (cfg.ColorMode)
        {
            case ColorMode.Solid:
                nameColor = msgColor = cfg.SolidColor;
                nameColor2 = msgColor2 = cfg.SolidColor;
                break;
            case ColorMode.Gradient:
                if (IsGameLogEffect(msg.Type) || def?.HasSender != false)
                {
                    nameColor = ch?.NameColor ?? new Vector4(1,1,1,1);
                    nameColor2 = ch?.NameColor2 ?? nameColor;
                    msgColor = ch?.MsgColor ?? new Vector4(1,1,1,1);
                    msgColor2 = ch?.MsgColor2 ?? msgColor;
                }
                else
                {
                    nameColor = msgColor = ch?.Color ?? new Vector4(1,1,1,1);
                    nameColor2 = msgColor2 = ch?.MsgColor2 ?? msgColor;
                }
                break;
            case ColorMode.Split:
                if (IsGameLogEffect(msg.Type))
                {
                    nameColor = ch?.NameColor ?? new Vector4(1,1,1,1);
                    msgColor  = ch?.MsgColor ?? new Vector4(1,1,1,1);
                    nameColor2 = nameColor;
                    msgColor2 = msgColor;
                }
                else if (def?.HasSender == false)
                {
                    nameColor = msgColor = ch?.Color ?? new Vector4(1,1,1,1);
                    nameColor2 = msgColor2 = msgColor;
                }
                else
                {
                    nameColor = ch?.NameColor ?? new Vector4(1,1,1,1);
                    msgColor  = ch?.MsgColor  ?? new Vector4(1,1,1,1);
                    nameColor2 = nameColor;
                    msgColor2 = msgColor;
                }
                break;
            default:
                nameColor = msgColor = ch?.Color ?? new Vector4(1,1,1,1);
                nameColor2 = msgColor2 = msgColor;
                break;
        }

        string prefix  = cfg.ShowChannelPrefix && def != null ? $"({def.Label}) " : "";
        bool   hasSender = def?.HasSender ?? true;
        string sender  = hasSender ? FormatSender(msg.Sender, cfg.FirstNameOnly) : "";
        var    scrPos  = ImGui.GetCursorScreenPos();
        var    dl      = ImGui.GetWindowDrawList();
        var    font    = ImGui.GetFont();
        float  sz      = cfg.FontSize;
        float  x       = scrPos.X, y = scrPos.Y;

        var segments = new List<RenderSegment>();
        if (hasSender && !string.IsNullOrWhiteSpace(sender))
            segments.Add(new RenderSegment(prefix + sender + ": ", nameColor, cfg.ColorMode == ColorMode.Gradient ? nameColor2 : null));
        else if (!string.IsNullOrEmpty(prefix))
            segments.Add(new RenderSegment(prefix, nameColor, cfg.ColorMode == ColorMode.Gradient ? nameColor2 : null));

        if (cfg.ColorMode == ColorMode.Split && IsGameLogEffect(msg.Type) && TrySplitGameLogEffect(msg.Text, out var beforeEffect, out var effectName, out var afterEffect))
        {
            segments.Add(new RenderSegment(beforeEffect, msgColor));
            segments.Add(new RenderSegment(effectName, nameColor));
            segments.Add(new RenderSegment(afterEffect, msgColor));
        }
        else if (cfg.ColorMode == ColorMode.Gradient && IsGameLogEffect(msg.Type) && TrySplitGameLogEffect(msg.Text, out beforeEffect, out effectName, out afterEffect))
        {
            segments.Add(new RenderSegment(beforeEffect, msgColor, msgColor2));
            segments.Add(new RenderSegment(effectName, nameColor, nameColor2));
            segments.Add(new RenderSegment(afterEffect, msgColor, msgColor2));
        }
        else if (cfg.EnablePriority && cfg.PriorityWords.Count > 0)
        {
            foreach (var (seg, isPri) in Tokenize(msg.Text, cfg.PriorityWords))
                segments.Add(new RenderSegment(seg, isPri ? cfg.PriorityColor : msgColor, isPri || cfg.ColorMode != ColorMode.Gradient ? null : msgColor2));
        }
        else
        {
            segments.Add(new RenderSegment(msg.Text, msgColor, cfg.ColorMode == ColorMode.Gradient ? msgColor2 : null));
        }

        var lineCount = DrawWrappedSegments(dl, font, sz, scrPos, segments, cfg, alpha, cfg.WrapWidth);

        if (Math.Abs(cachedFontSz - sz) > 0.01f)
        {
            cachedLineH  = ImGui.CalcTextSize("A").Y + 2f;
            cachedFontSz = sz;
        }
        ImGui.Dummy(new Vector2(Math.Max(cfg.WrapWidth, 10f), cachedLineH * lineCount));
    }

    private static int DrawWrappedSegments(ImDrawListPtr dl, ImFontPtr font, float sz, Vector2 start, List<RenderSegment> segments, Configuration cfg, float alpha, float wrapWidth)
    {
        var x = start.X;
        var y = start.Y;
        var lineStart = start.X;
        var lineCount = 1;

        foreach (var segment in segments)
        {
            var gradientWidth = Math.Max(ImGui.CalcTextSize(segment.Text).X, 1f);
            var gradientDrawnWidth = 0f;
            foreach (var token in SplitForWrap(segment.Text))
            {
                var tokenWidth = ImGui.CalcTextSize(token).X;
                if (!string.IsNullOrWhiteSpace(token) && x > lineStart && x + tokenWidth - lineStart > wrapWidth)
                {
                    x = lineStart;
                    y += ImGui.CalcTextSize("A").Y + 2f;
                    lineCount++;
                    if (string.IsNullOrWhiteSpace(token))
                        continue;
                }

                if (segment.Color2 is { } endColor)
                    SegGradientPart(dl, font, sz, ref x, y, token, segment.Color, endColor, cfg, alpha, gradientWidth, ref gradientDrawnWidth);
                else
                    Seg(dl, font, sz, ref x, y, token, segment.Color, cfg, alpha);
            }
        }

        return lineCount;
    }

    private static IEnumerable<string> SplitForWrap(string text)
    {
        if (string.IsNullOrEmpty(text))
            yield break;

        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
                continue;

            if (i > start)
                yield return text[start..i];

            yield return text[i].ToString();
            start = i + 1;
        }

        if (start < text.Length)
            yield return text[start..];
    }

    private static bool IsGameLogEffect(XivChatType type)
    {
        var id = (ushort)type;
        return id >= 46 && id <= 49;
    }

    private static bool TrySplitGameLogEffect(string text, out string beforeEffect, out string effectName, out string afterEffect)
    {
        beforeEffect = text;
        effectName = string.Empty;
        afterEffect = string.Empty;

        var effectIndex = text.IndexOf("effect of ", StringComparison.OrdinalIgnoreCase);
        if (effectIndex < 0)
            return false;

        var nameStart = effectIndex + "effect of ".Length;
        while (nameStart < text.Length && !char.IsLetterOrDigit(text[nameStart]))
            nameStart++;

        if (nameStart >= text.Length)
            return false;

        var nameEnd = text.IndexOf('.', nameStart);
        if (nameEnd < 0)
            nameEnd = text.Length;

        beforeEffect = text[..nameStart];
        effectName = text[nameStart..nameEnd];
        afterEffect = text[nameEnd..];
        return !string.IsNullOrWhiteSpace(effectName);
    }
}
