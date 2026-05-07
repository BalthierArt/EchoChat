using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace ChatEcho;

public enum TextEffect { None, Shadow, Outline }
public enum ColorMode  { PerChannel, Split, Solid, Gradient }
public enum GameLogEffectScope { OnlyUser = 0, All = 1, Party = 2 }
public enum DebuffHelperGrowthDirection { Down, Up, Right, Left }

[Serializable]
public class ChannelSettings
{
    public bool    Enabled   { get; set; } = false;
    public Vector4 Color     { get; set; } = new(1, 1, 1, 1);
    public Vector4 NameColor { get; set; } = new(1, 1, 1, 1);
    public Vector4 MsgColor  { get; set; } = new(1, 1, 1, 1);
    public Vector4 NameColor2 { get; set; } = new(1, 1, 1, 1);
    public Vector4 MsgColor2  { get; set; } = new(1, 1, 1, 1);
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = false;

    public float DisplayDuration { get; set; } = 3.0f;
    public int   MaxMessages     { get; set; } = 5;
    public bool  ShowLastFaded   { get; set; } = true;
    public bool  Locked          { get; set; } = false;

    public float     FontSize          { get; set; } = 22f;
    public float     WrapWidth         { get; set; } = 650f;
    public float     BackgroundOpacity { get; set; } = 0.35f;
    public float     BackgroundPadding { get; set; } = 6f;
    public TextEffect TextEffect       { get; set; } = TextEffect.Outline;
    public Vector4   OutlineColor      { get; set; } = new(0, 0, 0, 1);
    public Vector4   ShadowColor       { get; set; } = new(0, 0, 0, 0.8f);
    public Vector4   SolidColor        { get; set; } = new(1, 1, 1, 1);

    public bool ShowChannelPrefix { get; set; } = false;
    public bool FirstNameOnly     { get; set; } = false;
    public GameLogEffectScope GameLogEffectScope { get; set; } = GameLogEffectScope.OnlyUser;

    public ColorMode ColorMode { get; set; } = ColorMode.PerChannel;

    public Dictionary<string, ChannelSettings> Channels { get; set; } = new();

    [NonSerialized] private readonly object channelLock = new();

    public ChannelSettings Get(string key, System.Numerics.Vector4 defaultColor)
    {
        lock (channelLock)
        {
            if (!Channels.TryGetValue(key, out var s))
            {
                s = new ChannelSettings { Color = defaultColor, NameColor = defaultColor, NameColor2 = defaultColor, MsgColor2 = new Vector4(1, 1, 1, 1) };
                Channels[key] = s;
            }
            return s;
        }
    }

    public bool         EnablePriority { get; set; } = true;
    public bool         PriorityOnly   { get; set; } = false;
    public Vector4      PriorityColor  { get; set; } = new(1f, 0.25f, 0.25f, 1f);
    public List<string> PriorityWords  { get; set; } = new()
    {
        "stack", "spread", "tank swap", "swap",
        "lb", "lb3", "limit break",
        "heal", "heals", "move", "dodge"
    };

    public Vector2 BannerPosition { get; set; } = new(600, 400);

    public bool DebuffHelperEnabled { get; set; } = false;
    public bool DebuffHelperLocked { get; set; } = false;
    public bool DebuffHelperShowIcon { get; set; } = true;
    public bool DebuffHelperShowTime { get; set; } = true;
    public bool DebuffHelperShowDetails { get; set; } = true;
    public bool DebuffHelperShowAdvice { get; set; } = true;
    public DebuffHelperGrowthDirection DebuffHelperGrowthDirection { get; set; } = DebuffHelperGrowthDirection.Down;
    public int DebuffHelperItemsPerRow { get; set; } = 1;
    public float DebuffHelperIconSize { get; set; } = 44f;
    public float DebuffHelperWrapWidth { get; set; } = 520f;
    public float DebuffHelperNameFontSize { get; set; } = 36f;
    public float DebuffHelperDetailsFontSize { get; set; } = 31f;
    public float DebuffHelperAdviceFontSize { get; set; } = 34f;
    public float DebuffHelperBackgroundOpacity { get; set; } = 0.25f;
    public float DebuffHelperBackgroundPadding { get; set; } = 8f;
    public float DebuffHelperDebuffSpacing { get; set; } = 10f;
    public Vector2 DebuffHelperPosition { get; set; } = new(720, 520);
    public Vector4 DebuffHelperNameColor { get; set; } = new(1f, 0.78f, 0.35f, 1f);
    public Vector4 DebuffHelperDetailsColor { get; set; } = new(0.9f, 0.9f, 0.9f, 1f);
    public Vector4 DebuffHelperAdviceColor { get; set; } = new(0.42f, 0.9f, 1f, 1f);
    public Vector4 DebuffHelperNameBackgroundColor { get; set; } = new(0f, 0f, 0f, 0f);
    public Vector4 DebuffHelperDetailsBackgroundColor { get; set; } = new(0f, 0f, 0f, 0f);
    public Vector4 DebuffHelperAdviceBackgroundColor { get; set; } = new(0f, 0f, 0f, 0f);
    public TextEffect DebuffHelperNameEffect { get; set; } = TextEffect.Outline;
    public TextEffect DebuffHelperDetailsEffect { get; set; } = TextEffect.Shadow;
    public TextEffect DebuffHelperAdviceEffect { get; set; } = TextEffect.Outline;
    public Vector4 DebuffHelperNameEffectColor { get; set; } = new(0f, 0f, 0f, 1f);
    public Vector4 DebuffHelperDetailsEffectColor { get; set; } = new(0f, 0f, 0f, 0.8f);
    public Vector4 DebuffHelperAdviceEffectColor { get; set; } = new(0f, 0f, 0f, 1f);

    public bool CastHelperEnabled { get; set; } = false;
    public bool CastHelperLocked { get; set; } = false;
    public bool CastHelperShowIcon { get; set; } = true;
    public bool CastHelperShowTime { get; set; } = true;
    public bool CastHelperShowDetails { get; set; } = true;
    public bool CastHelperShowAdvice { get; set; } = true;
    public bool CastHelperKeepLastUntilNextCast { get; set; } = false;
    public DebuffHelperGrowthDirection CastHelperGrowthDirection { get; set; } = DebuffHelperGrowthDirection.Down;
    public int CastHelperItemsPerRow { get; set; } = 1;
    public float CastHelperIconSize { get; set; } = 44f;
    public float CastHelperWrapWidth { get; set; } = 520f;
    public float CastHelperNameFontSize { get; set; } = 36f;
    public float CastHelperDetailsFontSize { get; set; } = 31f;
    public float CastHelperAdviceFontSize { get; set; } = 34f;
    public float CastHelperBackgroundOpacity { get; set; } = 0.25f;
    public float CastHelperBackgroundPadding { get; set; } = 8f;
    public float CastHelperCastSpacing { get; set; } = 10f;
    public Vector2 CastHelperPosition { get; set; } = new(760, 420);
    public Vector4 CastHelperNameColor { get; set; } = new(1f, 0.78f, 0.35f, 1f);
    public Vector4 CastHelperDetailsColor { get; set; } = new(0.9f, 0.9f, 0.9f, 1f);
    public Vector4 CastHelperAdviceColor { get; set; } = new(0.42f, 0.9f, 1f, 1f);
    public Vector4 CastHelperImportantColor { get; set; } = new(0.25f, 1f, 0.35f, 1f);
    public Vector4 CastHelperBlueColor { get; set; } = new(0.35f, 0.6f, 1f, 1f);
    public Vector4 CastHelperYellowColor { get; set; } = new(1f, 0.9f, 0.15f, 1f);
    public TextEffect CastHelperNameEffect { get; set; } = TextEffect.Outline;
    public TextEffect CastHelperDetailsEffect { get; set; } = TextEffect.Shadow;
    public TextEffect CastHelperAdviceEffect { get; set; } = TextEffect.Outline;
    public Vector4 CastHelperNameEffectColor { get; set; } = new(0f, 0f, 0f, 1f);
    public Vector4 CastHelperDetailsEffectColor { get; set; } = new(0f, 0f, 0f, 0.8f);
    public Vector4 CastHelperAdviceEffectColor { get; set; } = new(0f, 0f, 0f, 1f);

    [NonSerialized] private IDalamudPluginInterface? pluginInterface;
    public void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;
    public void Save() => pluginInterface?.SavePluginConfig(this);
}
