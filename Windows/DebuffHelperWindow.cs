using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Statuses;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace ChatEcho.Windows;

public sealed class DebuffHelperWindow : Window
{
    private const double RefreshIntervalSeconds = 0.25;
    private const double TestDurationSeconds = 3.0;
    private const double TestFadeSeconds = 0.8;

    private readonly Plugin plugin;
    private readonly IObjectTable objectTable;
    private readonly ITextureProvider textureProvider;
    private readonly IPluginLog log;
    private readonly List<IStatus> activeDebuffs = new();
    private readonly Dictionary<uint, DebuffInfo> debuffInfoCache = new();
    private readonly HashSet<uint> loggedUnknownDebuffs = new();
    private readonly List<TestDebuff> testDebuffs = new();
    private readonly List<DebuffBlock> activeDebuffBlocks = new();
    private double testDebuffStartedAt;
    private double nextRefreshTime;
    private bool dragging;

    private static readonly TestDebuff[] TestDebuffs =
    {
        new("Gold Lung", "A layer of sulphuric sludge has built up on the body.", "Eat a Morbol Fruit.", 217021, 12.4f),
        new("Forced March", "Advancing in the ordered direction.", "Face the safe direction before it resolves.", 215773, 6.8f),
        new("Walking Dead", "Most attacks will not reduce HP below 1. Restoring HP with each weaponskill successfully delivered and spell cast. The inability to restore 100% of HP before timer runs out will result in KO.", "Restore your full HP before the timer ends or you will be KO'd.", 213116, 9.9f),
        new("Extreme Caution", "A penalty will be assessed for any action, auto-attack, or movement taken after status ends.", "Stop moving, attacking, and using actions before the timer resolves.", 215747, 4.5f),
        new("Petrification", "Stone-like rigidity is preventing the execution of actions.", "You cannot act. Avoid gaze or statue mechanics if the fight uses them.", 215001, 5.2f),
    };

    public DebuffHelperWindow(Plugin plugin, IObjectTable objectTable, ITextureProvider textureProvider, IPluginLog log)
        : base("Debuff Helper  --  Drag title bar, then Lock###ChatEchoDebuffHelper")
    {
        this.plugin = plugin;
        this.objectTable = objectTable;
        this.textureProvider = textureProvider;
        this.log = log;
        IsOpen = true;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        DisableFadeInFadeOut = true;
    }

    public override void PreOpenCheck()
    {
        IsOpen = true;
    }

    public override void Update()
    {
        var cfg = plugin.Configuration;
        if (cfg.DebuffHelperEnabled)
            RefreshDebuffs();

        Position = cfg.DebuffHelperPosition;
        PositionCondition = cfg.DebuffHelperLocked ? ImGuiCond.Always : ImGuiCond.FirstUseEver;
        BgAlpha = cfg.DebuffHelperLocked ? 0f : 0.7f;
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
              | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize;

        if (cfg.DebuffHelperLocked)
            Flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs;
        else
            Flags |= ImGuiWindowFlags.NoFocusOnAppearing;
    }

    public void ShowTestDebuff()
    {
        testDebuffs.Clear();
        var count = Random.Shared.Next(1, 4);
        var usedIndexes = new HashSet<int>();
        while (testDebuffs.Count < count)
        {
            var index = Random.Shared.Next(TestDebuffs.Length);
            if (usedIndexes.Add(index))
                testDebuffs.Add(TestDebuffs[index]);
        }

        testDebuffStartedAt = ImGui.GetTime();
    }

    public override bool DrawConditions()
    {
        var cfg = plugin.Configuration;
        return IsTestVisible() || (cfg.DebuffHelperEnabled && (!cfg.DebuffHelperLocked || activeDebuffs.Count > 0));
    }

    public override void PreDraw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(plugin.Configuration.DebuffHelperBackgroundPadding));
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar();
    }

    public override void Draw()
    {
        var cfg = plugin.Configuration;
        if (!cfg.DebuffHelperLocked)
        {
            var pos = ImGui.GetWindowPos();
            if (pos != cfg.DebuffHelperPosition)
            {
                cfg.DebuffHelperPosition = pos;
                dragging = true;
            }

            if (dragging && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                cfg.Save();
                dragging = false;
            }
        }

        if (testDebuffs.Count > 0 && IsTestVisible())
        {
            DrawTestDebuff(cfg);
            return;
        }

        if (!cfg.DebuffHelperEnabled)
            return;

        if (activeDebuffs.Count == 0)
        {
            if (!cfg.DebuffHelperLocked)
                DrawStyledText("No active debuffs", cfg.DebuffHelperDetailsColor, cfg.DebuffHelperDetailsEffect, cfg.DebuffHelperDetailsEffectColor, cfg.DebuffHelperDetailsFontSize);
            return;
        }

        activeDebuffBlocks.Clear();
        foreach (var status in activeDebuffs)
            activeDebuffBlocks.Add(new DebuffBlock(() => DrawDebuff(status, cfg)));

        DrawDebuffBlocks(activeDebuffBlocks, cfg);
    }

    private bool IsTestVisible()
    {
        return testDebuffs.Count > 0 && ImGui.GetTime() < testDebuffStartedAt + TestDurationSeconds + TestFadeSeconds;
    }

    private float TestAlpha()
    {
        var elapsed = ImGui.GetTime() - testDebuffStartedAt;
        if (elapsed <= TestDurationSeconds)
            return 1f;

        return Math.Clamp((float)(1.0 - ((elapsed - TestDurationSeconds) / TestFadeSeconds)), 0f, 1f);
    }

    private void RefreshDebuffs()
    {
        var now = ImGui.GetTime();
        if (now < nextRefreshTime)
            return;

        nextRefreshTime = now + RefreshIntervalSeconds;
        activeDebuffs.Clear();

        var localPlayer = objectTable.LocalPlayer;
        if (localPlayer == null)
            return;

        foreach (var status in localPlayer.StatusList)
        {
            if (IsIgnoredStatus(status.StatusId))
                continue;

            var row = status.GameData.Value;
            if (row.StatusCategory == 2)
                activeDebuffs.Add(status);
        }
    }

    private static bool IsIgnoredStatus(uint statusId)
    {
        return statusId is 43 or 44;
    }

    private void DrawDebuff(IStatus status, Configuration cfg)
    {
        var info = GetDebuffInfo(status);
        if (string.IsNullOrWhiteSpace(info.Advice) && loggedUnknownDebuffs.Add(status.StatusId))
            log.Information("Debuff Helper unknown advice: {Id} | {Name} | {Details}", status.StatusId, info.Name, info.Details);

        if (cfg.DebuffHelperShowIcon)
        {
            var icon = textureProvider.GetFromGameIcon(new GameIconLookup(info.IconId));
            if (icon.TryGetWrap(out var texture, out _))
            {
                ImGui.Image(texture.Handle, new Vector2(cfg.DebuffHelperIconSize));
                ImGui.SameLine();
            }
        }

        var title = cfg.DebuffHelperShowTime && status.RemainingTime > 0
            ? $"{info.Name}  {status.RemainingTime:F1}s"
            : info.Name;

        DrawStyledText(title, cfg.DebuffHelperNameColor, cfg.DebuffHelperNameEffect, cfg.DebuffHelperNameEffectColor, cfg.DebuffHelperNameFontSize);

        if (cfg.DebuffHelperShowDetails && !string.IsNullOrWhiteSpace(info.Details))
            DrawStyledText(info.Details, cfg.DebuffHelperDetailsColor, cfg.DebuffHelperDetailsEffect, cfg.DebuffHelperDetailsEffectColor, cfg.DebuffHelperDetailsFontSize, cfg.DebuffHelperWrapWidth);

        if (cfg.DebuffHelperShowAdvice && !string.IsNullOrWhiteSpace(info.Advice))
            DrawStyledText(info.Advice, cfg.DebuffHelperAdviceColor, cfg.DebuffHelperAdviceEffect, cfg.DebuffHelperAdviceEffectColor, cfg.DebuffHelperAdviceFontSize, cfg.DebuffHelperWrapWidth);

        ImGui.Spacing();
    }

    private void DrawTestDebuff(Configuration cfg)
    {
        if (testDebuffs.Count == 0)
            return;

        var alpha = TestAlpha();
        if (alpha <= 0f)
        {
            testDebuffs.Clear();
            return;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, alpha);

        activeDebuffBlocks.Clear();
        foreach (var debuff in testDebuffs)
            activeDebuffBlocks.Add(new DebuffBlock(() => DrawTestDebuffItem(debuff, cfg)));

        DrawDebuffBlocks(activeDebuffBlocks, cfg);

        ImGui.PopStyleVar();
    }

    private static void DrawDebuffBlocks(List<DebuffBlock> blocks, Configuration cfg)
    {
        if (blocks.Count == 0)
            return;

        var start = ImGui.GetCursorScreenPos();
        var baseCursor = ImGui.GetCursorPos();
        var spacing = cfg.DebuffHelperDebuffSpacing;
        var itemsPerRow = Math.Clamp(cfg.DebuffHelperItemsPerRow, 1, 6);

        foreach (var block in blocks)
            MeasureDebuffBlock(block, cfg);

        var totalRows = (int)Math.Ceiling(blocks.Count / (double)itemsPerRow);
        var rowHeights = new float[totalRows];
        var columnWidths = new float[itemsPerRow];

        for (var i = 0; i < blocks.Count; i++)
        {
            var row = i / itemsPerRow;
            var col = i % itemsPerRow;
            rowHeights[row] = Math.Max(rowHeights[row], blocks[i].Size.Y);
            columnWidths[col] = Math.Max(columnWidths[col], blocks[i].Size.X);
        }

        var offsets = new Vector2[blocks.Count];
        for (var i = 0; i < blocks.Count; i++)
        {
            var row = i / itemsPerRow;
            var col = i % itemsPerRow;
            offsets[i] = OffsetFor(row, col, rowHeights, columnWidths, spacing, cfg.DebuffHelperGrowthDirection);
        }

        for (var i = 0; i < blocks.Count; i++)
        {
            var pos = start + offsets[i];
            ImGui.SetCursorScreenPos(pos);
            DrawDebuffBlock(blocks[i].DrawContent, cfg);
        }

        var totalSize = TotalSize(rowHeights, columnWidths, spacing);
        ImGui.SetCursorPos(baseCursor);
        ImGui.Dummy(totalSize);
    }

    private static Vector2 OffsetFor(int row, int col, float[] rowHeights, float[] columnWidths, float spacing, DebuffHelperGrowthDirection direction)
    {
        static float SumBefore(float[] values, int count, float spacing)
        {
            var total = 0f;
            for (var i = 0; i < count; i++)
                total += values[i] + spacing;
            return total;
        }

        var x = SumBefore(columnWidths, col, spacing);
        var y = SumBefore(rowHeights, row, spacing);
        var width = SumBefore(columnWidths, columnWidths.Length, spacing);

        return direction switch
        {
            DebuffHelperGrowthDirection.Up => new Vector2(x, SumBefore(rowHeights, rowHeights.Length - row - 1, spacing)),
            DebuffHelperGrowthDirection.Left => new Vector2(width - x - columnWidths[col], y),
            _ => new Vector2(x, y),
        };
    }

    private static Vector2 TotalSize(float[] rowHeights, float[] columnWidths, float spacing)
    {
        static float Sum(float[] values, float spacing)
        {
            var total = 0f;
            for (var i = 0; i < values.Length; i++)
                total += values[i] + (i == values.Length - 1 ? 0f : spacing);
            return total;
        }

        return new Vector2(Sum(columnWidths, spacing), Sum(rowHeights, spacing));
    }

    private static void MeasureDebuffBlock(DebuffBlock block, Configuration cfg)
    {
        var start = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(new Vector2(-100000f, -100000f));
        ImGui.BeginGroup();
        block.DrawContent();
        ImGui.EndGroup();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        block.Size = (max - min) + new Vector2(cfg.DebuffHelperBackgroundPadding * 2f);
        ImGui.SetCursorScreenPos(start);
    }

    private static void DrawDebuffBlock(Action drawContent, Configuration cfg)
    {
        var padding = cfg.DebuffHelperBackgroundPadding;
        var start = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        ImGui.SetCursorScreenPos(start + new Vector2(padding, padding));
        ImGui.BeginGroup();
        drawContent();
        ImGui.EndGroup();
        var contentMin = ImGui.GetItemRectMin();
        var contentMax = ImGui.GetItemRectMax();
        var min = contentMin - new Vector2(padding, padding);
        var max = contentMax + new Vector2(padding, padding);

        if (cfg.DebuffHelperBackgroundOpacity > 0f)
        {
            var color = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, cfg.DebuffHelperBackgroundOpacity));
            drawList.ChannelsSetCurrent(0);
            drawList.AddRectFilled(min, max, color, 8f);
        }

        drawList.ChannelsMerge();
        ImGui.SetCursorScreenPos(new Vector2(start.X, max.Y));
    }

    private void DrawTestDebuffItem(TestDebuff debuff, Configuration cfg)
    {

        if (cfg.DebuffHelperShowIcon)
        {
            var icon = textureProvider.GetFromGameIcon(new GameIconLookup(debuff.IconId));
            if (icon.TryGetWrap(out var texture, out _))
            {
                ImGui.Image(texture.Handle, new Vector2(cfg.DebuffHelperIconSize));
                ImGui.SameLine();
            }
        }

        var title = cfg.DebuffHelperShowTime
            ? $"{debuff.Name}  {Math.Max(0f, debuff.RemainingTime - (float)(ImGui.GetTime() - testDebuffStartedAt)):F1}s"
            : debuff.Name;

        DrawStyledText(title, cfg.DebuffHelperNameColor, cfg.DebuffHelperNameEffect, cfg.DebuffHelperNameEffectColor, cfg.DebuffHelperNameFontSize);

        if (cfg.DebuffHelperShowDetails && !string.IsNullOrWhiteSpace(debuff.Details))
            DrawStyledText(debuff.Details, cfg.DebuffHelperDetailsColor, cfg.DebuffHelperDetailsEffect, cfg.DebuffHelperDetailsEffectColor, cfg.DebuffHelperDetailsFontSize, cfg.DebuffHelperWrapWidth);

        if (cfg.DebuffHelperShowAdvice && !string.IsNullOrWhiteSpace(debuff.Advice))
            DrawStyledText(debuff.Advice, cfg.DebuffHelperAdviceColor, cfg.DebuffHelperAdviceEffect, cfg.DebuffHelperAdviceEffectColor, cfg.DebuffHelperAdviceFontSize, cfg.DebuffHelperWrapWidth);
    }

    private DebuffInfo GetDebuffInfo(IStatus status)
    {
        var id = status.StatusId;
        if (debuffInfoCache.TryGetValue(id, out var info))
            return info;

        var row = status.GameData.Value;
        var name = row.Name.ExtractText();
        var details = row.Description.ExtractText();
        var advice = DebuffAdviceProvider.GetAdvice(id, name, details, out _);
        info = new DebuffInfo(name, details, advice, row.Icon);
        debuffInfoCache[id] = info;
        return info;
    }

    private static void DrawStyledText(string text, Vector4 color, TextEffect effect, Vector4 effectColor, float fontSize, float wrapWidth = 0f)
    {
        if (wrapWidth <= 0f)
        {
            DrawStyledLine(text, color, effect, effectColor, fontSize);
            return;
        }

        var scale = fontSize / ImGui.GetFontSize();
        foreach (var line in WrapText(text, wrapWidth / scale))
            DrawStyledLine(line, color, effect, effectColor, fontSize);
    }

    private static void DrawStyledLine(string text, Vector4 color, TextEffect effect, Vector4 effectColor, float fontSize)
    {
        var scale = fontSize / ImGui.GetFontSize();

        ImGui.SetWindowFontScale(scale);
        var textPos = ImGui.GetCursorScreenPos();
        if (effect == TextEffect.Shadow)
        {
            ImGui.SetCursorScreenPos(textPos + new Vector2(2f, 2f));
            ImGui.TextColored(effectColor, text);
            ImGui.SetCursorScreenPos(textPos);
        }
        else if (effect == TextEffect.Outline)
        {
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                ImGui.SetCursorScreenPos(textPos + new Vector2(dx, dy));
                ImGui.TextColored(effectColor, text);
            }

            ImGui.SetCursorScreenPos(textPos);
        }

        ImGui.TextColored(color, text);
        ImGui.SetWindowFontScale(1f);
    }

    private static List<string> WrapText(string text, float wrapWidth)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (current.Length > 0 && ImGui.CalcTextSize(candidate).X > wrapWidth)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0)
            lines.Add(current);

        return lines.Count > 0 ? lines : new List<string> { text };
    }

    private sealed record DebuffInfo(string Name, string Details, string Advice, uint IconId);
    private sealed record TestDebuff(string Name, string Details, string Advice, uint IconId, float RemainingTime);
    private sealed class DebuffBlock(Action drawContent)
    {
        public Action DrawContent { get; } = drawContent;
        public Vector2 Size { get; set; }
    }
}
