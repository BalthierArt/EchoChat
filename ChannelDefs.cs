using Dalamud.Game.Text;
using System.Numerics;

namespace ChatEcho;

/// <summary>Master list of every supported channel with its key, label, default color, and chat types.</summary>
public static class ChannelDefs
{
    public sealed record Def(string Key, string Label, Vector4 DefaultColor, XivChatType[] Types, bool HasSender = true);

    public static readonly Def[] All =
    {
        // ── Combat / Raid ─────────────────────────────────────────────
        new("party",    "Party",           new(0.259f, 0.784f, 0.961f, 1), new[] { XivChatType.Party, XivChatType.CrossParty }),
        new("alliance", "Alliance / Raid", new(0.937f, 0.435f, 0.922f, 1), new[] { XivChatType.Alliance }),
        new("pvpteam",  "PvP Team",        new(0.93f,  0.42f,  0.42f,  1), new[] { XivChatType.PvPTeam }),
        // ── Social ───────────────────────────────────────────────────
        new("say",    "Say",             new(1,     1,     1,     1), new[] { XivChatType.Say }),
        new("shout",  "Shout",           new(1,     0.79f, 0.26f, 1), new[] { XivChatType.Shout }),
        new("yell",   "Yell",            new(1,     0.66f, 0,     1), new[] { XivChatType.Yell }),
        new("tell",   "Tell (incoming)", new(1,     0.57f, 0.78f, 1), new[] { XivChatType.TellIncoming }),
        new("fc",     "Free Company",    new(0.42f, 0.93f, 0.56f, 1), new[] { XivChatType.FreeCompany }),
        new("novice", "Novice Network",  new(0.52f, 0.93f, 0.85f, 1), new[] { XivChatType.NoviceNetwork }),
        // ── Linkshells — have sender names ───────────────────────────
        new("ls",  "Linkshell (all)",      new(0.78f, 0.93f, 0.42f, 1),
            new[] { XivChatType.Ls1, XivChatType.Ls2, XivChatType.Ls3, XivChatType.Ls4,
                    XivChatType.Ls5, XivChatType.Ls6, XivChatType.Ls7, XivChatType.Ls8 }),
        new("cls", "Cross-world LS (all)", new(0.65f, 0.93f, 0.42f, 1),
            new[] { XivChatType.CrossLinkShell1, XivChatType.CrossLinkShell2,
                    XivChatType.CrossLinkShell3, XivChatType.CrossLinkShell4,
                    XivChatType.CrossLinkShell5, XivChatType.CrossLinkShell6,
                    XivChatType.CrossLinkShell7, XivChatType.CrossLinkShell8 }),
        // ── System — no sender name ───────────────────────────────────
        new("echo",   "Echo",   new(0.7f,  0.7f,  0.7f,  1), new[] { XivChatType.Echo },          HasSender: false),
        new("system", "System", new(0.8f,  0.8f,  0.8f,  1), new[] { XivChatType.SystemMessage, XivChatType.SystemError }, HasSender: false),
        new("notice", "Notice", new(0.93f, 0.93f, 0.42f, 1), new[] { XivChatType.Notice },        HasSender: false),
        new("urgent", "Urgent", new(0.93f, 0.42f, 0.42f, 1), new[] { XivChatType.Urgent },        HasSender: false),
    };

    // Fast lookup: XivChatType → Def key
    private static readonly System.Collections.Generic.Dictionary<XivChatType, string> TypeToKey = BuildMap();
    private static readonly System.Collections.Generic.Dictionary<string, Def>         KeyToDef  = BuildKeyMap();

    private static System.Collections.Generic.Dictionary<XivChatType, string> BuildMap()
    {
        var map = new System.Collections.Generic.Dictionary<XivChatType, string>();
        foreach (var def in All)
            foreach (var t in def.Types)
                map.TryAdd(t, def.Key);
        return map;
    }

    private static System.Collections.Generic.Dictionary<string, Def> BuildKeyMap()
    {
        var map = new System.Collections.Generic.Dictionary<string, Def>();
        foreach (var def in All)
            map[def.Key] = def;
        return map;
    }

    public static string? KeyFor(XivChatType type)
        => TypeToKey.TryGetValue(type, out var k) ? k : null;

    /// <summary>O(1) lookup by key — use this instead of Array.Find in hot paths.</summary>
    public static Def? ByKey(string key)
        => KeyToDef.TryGetValue(key, out var d) ? d : null;
}
