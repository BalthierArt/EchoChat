using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace ChatEcho;

public enum TextEffect { None, Shadow, Outline }
public enum ColorMode  { PerChannel, Split, Solid }

/// <summary>Per-channel enable + color settings.</summary>
[Serializable]
public class ChannelSettings
{
    public bool    Enabled   { get; set; } = false;
    public Vector4 Color     { get; set; } = new(1, 1, 1, 1);
    public Vector4 NameColor { get; set; } = new(1, 1, 1, 1);
    public Vector4 MsgColor  { get; set; } = new(1, 1, 1, 1);
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // ── Master toggle — OFF by default so the banner doesn't surprise new users ──
    public bool Enabled { get; set; } = false;

    // ── General ──────────────────────────────────────────────────────
    public float DisplayDuration { get; set; } = 3.0f;
    public int   MaxMessages     { get; set; } = 5;
    public bool  ShowLastFaded   { get; set; } = true;
    public bool  Locked          { get; set; } = false;

    // ── Display ──────────────────────────────────────────────────────
    public float     FontSize          { get; set; } = 22f;
    public float     BackgroundOpacity { get; set; } = 0.35f;
    public float     BackgroundPadding { get; set; } = 6f;
    public TextEffect TextEffect       { get; set; } = TextEffect.Outline;
    public Vector4   OutlineColor      { get; set; } = new(0, 0, 0, 1);
    public Vector4   ShadowColor       { get; set; } = new(0, 0, 0, 0.8f);
    public Vector4   SolidColor        { get; set; } = new(1, 1, 1, 1);

    // ── Name format ───────────────────────────────────────────────────
    public bool ShowChannelPrefix { get; set; } = false;
    public bool FirstNameOnly     { get; set; } = false;

    // ── Color mode ────────────────────────────────────────────────────
    public ColorMode ColorMode { get; set; } = ColorMode.PerChannel;

    // ── Per-channel settings ──────────────────────────────────────────
    public Dictionary<string, ChannelSettings> Channels { get; set; } = new();

    [NonSerialized] private readonly object channelLock = new();

    public ChannelSettings Get(string key, System.Numerics.Vector4 defaultColor)
    {
        lock (channelLock)
        {
            if (!Channels.TryGetValue(key, out var s))
            {
                s = new ChannelSettings { Color = defaultColor, NameColor = defaultColor };
                Channels[key] = s;
            }
            return s;
        }
    }

    // ── Priority ──────────────────────────────────────────────────────
    public bool         EnablePriority { get; set; } = true;
    public bool         PriorityOnly   { get; set; } = false;
    public Vector4      PriorityColor  { get; set; } = new(1f, 0.25f, 0.25f, 1f);
    public List<string> PriorityWords  { get; set; } = new()
    {
        "stack", "spread", "tank swap", "swap",
        "lb", "lb3", "limit break",
        "heal", "heals", "move", "dodge"
    };

    // ── Position ──────────────────────────────────────────────────────
    public Vector2 BannerPosition { get; set; } = new(600, 400);

    // ─────────────────────────────────────────────────────────────────
    [NonSerialized] private IDalamudPluginInterface? pluginInterface;
    public void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;
    public void Save() => pluginInterface?.SavePluginConfig(this);
}
