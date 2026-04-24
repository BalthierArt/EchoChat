using Dalamud.Game.Text;
using System;

namespace ChatEcho;

public sealed class ChatMessage
{
    public XivChatType Type    { get; }
    public string      Sender  { get; }
    public string      Text    { get; }
    public DateTime    AddedAt { get; }

    public ChatMessage(XivChatType type, string sender, string text)
    {
        Type    = type;
        Sender  = sender;
        Text    = text;
        AddedAt = DateTime.Now;
    }

    public bool IsExpired(float duration)
        => (DateTime.Now - AddedAt).TotalSeconds >= duration;

    /// <summary>
    /// Returns 0–1 alpha. Full opacity until the last 0.4 s, then fades to 0.
    /// </summary>
    public float GetAlpha(float duration, float fadeDuration = 0.4f)
    {
        double remaining = duration - (DateTime.Now - AddedAt).TotalSeconds;
        if (remaining >= fadeDuration) return 1.0f;
        if (remaining <= 0)           return 0.0f;
        return (float)(remaining / fadeDuration);
    }
}
