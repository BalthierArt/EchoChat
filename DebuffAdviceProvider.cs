using System;
using System.Collections.Generic;

namespace ChatEcho;

public static class DebuffAdviceProvider
{
    private const string VulnerabilityAdvice = "Try and avoid getting hit by other mechanics so you dont get more stacks.";

    private static readonly Dictionary<uint, string> ById = new()
    {
        [1789] = VulnerabilityAdvice,
        [302] = "Eat a Morbol Fruit.",
        [4562] = "DONT MOVE, stay still until it falls off.",
        [5174] = "Face the safe direction before it resolves.",
    };

    private static readonly (string Needle, string Advice)[] ByName =
    {
        ("Doom", "Cleanse it if possible, heal to full, or resolve the fight-specific mechanic before the timer ends."),
        ("Nymian Plague", "Cleanse or resolve it before it expires to avoid transformation."),
        ("Gold Lung", "Eat a Morbol Fruit."),
        ("Magic Foam", "WAIT 5 seconds before soaking another orb."),
        ("Misery", "Have another player use /comfort near you."),
        ("Magic Vulnerability Up", "Avoid magic damage and respect the next mechanic."),
        ("Physical Vulnerability Up", "Avoid physical damage and respect the next mechanic."),
        ("Vulnerability Up", VulnerabilityAdvice),
        ("Down for the Count", "Just wait."),
        ("Damage Down", "You dealt with a mechanic incorrectly; keep playing safely until it expires."),
        ("Physical Damage Down", "Your physical damage is reduced until this falls off."),
        ("Magic Damage Down", "Your magic damage is reduced until this falls off."),
        ("Malodorous", "Your damage is reduced. Keep playing safely until it expires."),
        ("Nausea", "Your damage and max HP are reduced. Avoid extra damage until it falls off."),
        ("Minimum", "You are smaller and weaker. Avoid extra damage until it wears off."),
        ("Weakness", "You were recently raised. Play safely while your damage and healing are reduced."),
        ("Brink of Death", "You were raised with heavy penalties. Avoid risk until it expires."),
        ("Voidblood", "You take increased damage. Avoid extra hits until it falls off."),
        ("Accursed Pox", "This is damage over time with extra penalties. Use healing and avoid extra damage."),
        ("Poison", "Use Esuna or mitigation/healing if the damage becomes dangerous."),
        ("Bleeding", "Use Esuna if removable, otherwise heal through the damage over time."),
        ("Burns", "Use Esuna if removable, otherwise heal through the damage over time."),
        ("Windburn", "This is wind damage over time. Use healing or mitigation if needed."),
        ("Venomous Bite", "This is poison damage over time. Use healing or mitigation if needed."),
        ("Dropsy", "Use Esuna if removable, otherwise heal through the damage over time."),
        ("Electrocution", "Use Esuna if removable, otherwise heal through the damage over time."),
        ("Sustained Damage", "This is damage over time. Use mitigation or healing if needed."),
        ("Paralysis", "Use Esuna if removable; expect interrupted actions while it remains."),
        ("Heavy", "Movement is slowed. Move early and avoid mechanics that require fast repositioning."),
        ("Slow", "Actions are slower. Avoid tight casts and play defensively."),
        ("Blind", "Physical attacks may miss. Use Esuna if removable."),
        ("Silence", "You cannot cast spells. Use Esuna if removable or wait it out."),
        ("Pacification", "You cannot use weaponskills. Use Esuna if removable or wait it out."),
        ("Out of the Action", "You cannot act. Wait for it to end and prepare for the next mechanic."),
        ("Bewildered", "You cannot control your actions. Wait it out and avoid unsafe positioning."),
        ("Stun", "You cannot act. Wait for it to end and prepare for the next mechanic."),
        ("Sleep", "You cannot act until it ends or damage wakes you."),
        ("Bind", "You cannot move. Use Esuna if removable or wait it out."),
        ("Thin Ice", "Movement may slide or be hard to control. Move carefully and avoid overcorrecting."),
        ("Vital Sign", "Movement is severely reduced. Position early and avoid extra damage."),
        ("Toad", "You are transformed and cannot use normal actions. Use any available duty action or wait it out."),
        ("Imp", "Use Imp Punch if available, or step in the matching mechanic puddle if the fight uses one."),
        ("Transfiguration", "Your form is altered and actions may be limited. Follow the fight-specific mechanic."),
        ("Aether Sickness", "Movement or resonance is impaired. Play safely until it expires."),
        ("Virus", "Physical stats are reduced. Avoid unnecessary damage until it falls off."),
        ("Fever", "Mental stats are reduced. Avoid unnecessary damage until it falls off."),
        ("Malady", "Healing received is reduced. Use mitigation and avoid extra damage."),
        ("Deep Freeze", "You cannot move or act. Wait for it to end and be ready to reposition."),
        ("Misery", "Healing received is reduced. Avoid extra damage until it falls off."),
        ("Healing Down", "Healing received is reduced. Use mitigation and avoid extra damage."),
        ("HP Penalty", "Your max HP is reduced. Avoid unnecessary damage until it falls off."),
        ("Twice-come Ruin", "Do not fail another marked mechanic or this may become fatal."),
        ("Thrice-come Ruin", "One more failed marked mechanic may kill you. Play very carefully."),
        ("Acceleration Bomb", "DONT MOVE, stay still until it falls off."),
        ("Extreme Caution", "DONT MOVE, stay still until it falls off."),
        ("Churning", "DONT MOVE, stay still until it falls off."),
        ("Pyretic", "DONT MOVE, stay still until it falls off."),
        ("Forked Lightning", "Move away from other players before it resolves."),
        ("Allagan Field", "Avoid taking damage; stored damage explodes when the effect ends."),
        ("Allagan Rot", "Pass or resolve the rot mechanic before the timer expires."),
        ("Allagan Immunity", "You cannot receive Allagan Rot while this is active."),
        ("Corrupted Crystal", "Avoid reaching three stacks or it will explode."),
        ("Thunderstruck", "Move away from other players before it resolves."),
        ("Static Condensation", "Avoid extra electric charges; healing received is reduced more at higher stacks."),
        ("Electroconductivity", "Collect the required charges for Surge Protection if the fight calls for it."),
        ("Surge Protection", "You are protected from the lightning mechanic. Avoid collecting extra charges."),
        ("Walking Dead", "Restore your full HP before the timer ends or you will be KO'd."),
        ("Throttle", "Resolve or cleanse it immediately; KO is imminent."),
        ("Unwilling Host", "Avoid touching other players unless the mechanic requires passing it."),
        ("Positive Charge", "Pair or separate based on matching magnetic charges for the mechanic."),
        ("Negative Charge", "Pair or separate based on matching magnetic charges for the mechanic."),
        ("Decree Nisi", "Avoid mixing with the opposite Nisi unless the fight specifically calls for it."),
        ("Final Decree Nisi", "Avoid mixing with the opposite Nisi unless the fight specifically calls for it."),
        ("Final Judgment", "Resolve your assigned judgment before the sentence triggers."),
        ("Restraining Order", "Stay separated from the assigned player."),
        ("Defamation", "Move away from the party before it resolves."),
        ("Shared Sentence", "Stack with the assigned players to share the sentence."),
        ("Off-balance", "Prepare for knockback from the next hit."),
        ("The Worm's Curse", "Keep attacking to restore HP and manage the damage over time."),
        ("Sinking", "Move or resolve the mechanic before burial causes KO."),
        ("Six Fulms Under", "Move or resolve the mechanic before burial causes KO."),
        ("Prey", "You are targeted by a mechanic. Watch your marker and resolve the encounter-specific action."),
        ("Fetters", "Your movement or actions are restricted. Resolve the fight mechanic or wait it out."),
        ("Forced March", "Your movement will be forced. Face or position yourself safely before it resolves."),
        ("Directional Disregard", "Your facing or movement may be affected. Position early and avoid unsafe directions."),
        ("Concussion", "Your controls or actions may be impaired. Play safely until it expires."),
        ("Hysteria", "You lose control of movement. Avoid dangerous areas before it takes effect."),
        ("Confused", "You may lose control of actions or targeting. Wait it out and avoid risky positioning."),
        ("Terror", "You cannot act. Wait for it to end and prepare for the next mechanic."),
        ("Petrification", "You cannot act. Avoid gaze or statue mechanics if the fight uses them."),
    };

    public static string GetAdvice(uint id, string name, string details, out bool exact)
    {
        if (IsVulnerabilityUp(name, details))
        {
            exact = true;
            return VulnerabilityAdvice;
        }

        if (ById.TryGetValue(id, out var idAdvice))
        {
            exact = true;
            return idAdvice;
        }

        foreach (var (needle, advice) in ByName)
        {
            if (Contains(name, needle))
            {
                exact = true;
                return advice;
            }
        }

        if (DebuffAdviceDatabase.Entries.TryGetValue(id, out var generated) && generated.IsSpecific)
        {
            exact = true;
            return generated.Advice;
        }

        exact = false;
        return string.Empty;
    }

    private static bool Contains(string text, string needle)
    {
        return text.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVulnerabilityUp(string name, string details)
    {
        return Contains(name, "Vulnerability Up")
            || Contains(name, "Vunerability Up")
            || (Contains(name, "Vulnerability") && Contains(details, "Damage taken is increased"))
            || (Contains(name, "Vunerability") && Contains(details, "Damage taken is increased"));
    }
}
