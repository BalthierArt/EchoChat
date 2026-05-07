using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ActionRow = Lumina.Excel.Sheets.Action;

namespace ChatEcho.Windows;

public sealed class CastHelperWindow : Window
{
    private const double RefreshIntervalSeconds = 0.25;
    private const double TestDurationSeconds = 3.0;
    private const double TestFadeSeconds = 0.8;
    private const double HeldCastMaxAgeSeconds = 45.0;

    private readonly Plugin plugin;
    private readonly IObjectTable objectTable;
    private readonly ITargetManager targetManager;
    private readonly IDataManager dataManager;
    private readonly ITextureProvider textureProvider;
    private readonly IPluginLog log;
    private readonly Dictionary<uint, CastActionInfo> actionInfoCache = new();
    private readonly HashSet<uint> loggedUnknownCasts = new();
    private readonly List<CastAlert> activeCasts = new();
    private readonly List<CastAlert> heldCasts = new();
    private readonly List<MechanicAlert> activeMechanicAlerts = new();
    private readonly List<CastBlock> activeCastBlocks = new();
    private double nextRefreshTime;
    private double heldCastsUpdatedAt;
    private double testCastStartedAt;
    private CastRule? testRule;
    private bool testCastVisible;
    private bool dragging;

    private static readonly ChatMechanicRule[] ChatMechanicRules =
    {
        ChatRule(new[] { "Memories of the slain are made manifest" }, "Immortal Remains", "Memento: Slain Memories", Normal("Edge adds will fire line AoEs. Watch memory positions and dodge the lanes.")),
        ChatRule(new[] { "Memories of an enemy bombardment are made manifest" }, "Immortal Remains", "Memento: Bombardment", Normal("Memory clumps mean large AoEs; single memories mean small AoEs. Read the spawn pattern and dodge.")),
        ChatRule(new[] { "Memories of war casualties are made manifest" }, "Immortal Remains", "Memento: War Casualties", Normal("Running memories show giant line AoEs. Watch their direction and move out of the lanes.")),
        ChatRule(new[] { "The Immortal Remains is gathering energy" }, "Immortal Remains", "Turmoil", Normal("Arm slam with no castbar. Watch the raised arm and move away from that half.")),
        ChatRule(new[] { "Traumerei prepares a ward against spirits" }, "Traumerei", "Ghostduster", Normal("Spirit players are killed. Be living before it resolves.")),
        ChatRule(new[] { "Traumerei prepares a ward against the living" }, "Traumerei", "Fleshbuster", Normal("Living players are killed. Take Ghostly Guise before it resolves.")),
    };

    private static readonly CastRule[] EnuoRules =
    {
        Rule("Dense Emptiness", "Enuo is casting Dense Emptiness.", Normal("Stack in "), Green("LIGHT PARTIES")),
        Rule("Naught Grows", "Enuo is casting Naught Grows.", Normal("1 Black Hole = full party stack, 2 Black Holes = light party stack.")),
        Rule("Meltdown", "Enuo is casting Meltdown.", Normal("Stand Center, Wait for Pyretic to wear off, and then run to clockspot/safespot for a spread.")),
        Rule("Airy Emptiness", "Enuo is casting Airy Emptiness.", Normal("Stack in "), Blue("PAIRS")),
        Rule("Gaze of the Void", "Enuo is casting Gaze of the Void.", Normal("The first orb is the "), Green("NEW NORTH"), Normal(". Go behind the spin, and dodge into a safe tile, Pop "), Yellow("YELLOW"), Normal(" Tethers first.")),
        Rule("Vacuum", "Enuo is casting Vacuum.", Normal("Line AOEs, Stand Opposite it, hug tight in towards the middle of the boss.")),
        Rule("Vacume", "Enuo is casting Vacuum.", Normal("Line AOEs, Stand Opposite it, hug tight in towards the middle of the boss.")),
        Rule("Deep Freeze", "Enuo is casting Deep Freeze.", Normal("Tanks go NORTH, Rest go SOUTH, And keep MOVING while it goes off so you dont get frozen.")),
        Rule("All for Naught", "Enuo is casting All for Naught.", Normal("Add Phase, There will be two towers on each side, go to your pair positions and soak the tower closest to you.")),
        Rule("Voidal Turbulence", "Enuo is casting Voidal Turbulence.", Normal("Four Towers, and four players with AOE Markers. If you dont have a marker, soak the tower in your zone. If you do have the marker, stand where there is a missing tower. Gazing eye just face middle.")),
        Rule("Lightless World", "Enuo is casting Lightless World.", Normal("Hits hard for Heavy Damage Multiple Times! HEAL and MITIGATE")),
        Rule("Meteorain", "Enuo is casting Meteorain.", Normal("Raid Wide damage - Unavoidable")),
        Rule("Almagest", "Enuo is casting Almagest.", Normal("Raid Wide Damage and Adds another black hole on outside.")),
        Rule("Naught Wakes", "Enuo is casting Naught Wakes.", Normal("Moves the black holes, One may split into two, and do AOEs. Big one shoots down the MIDDLE, the two small ones shoot down the sides. If both are split, the MIDDLE SAFE.")),
        Rule("Shrouded Holy", "Enuo is casting Shrouded Holy.", Normal("LIGHT PARTY STACKS on both healers.")),
        Rule("Naught Hunts", "Enuo is casting Naught Hunts.", Normal("Two players get targeted. Wait for the AOEs on the walls go off, then go out clockwise from their black hole. It will chase you, lead it clockwise around the arena. Rest of party hide middle to dodge. You will tether to a player, that player will take over the run around.")),
    };

    private static readonly CastRule[] DoomtrainRules =
    {
        Rule("Lightning Burst", "Doomtrain is casting Lightning Burst.", Normal("Tankbusters on both tanks with splash damage. Tanks spread out, party stay away.")),
        Rule("Lightning Express", "Doomtrain is casting Lightning Express.", Normal("Blue lane markers show laser height. Stand in a lane with a "), Green("HIGH"), Normal(" circle or use cargo/platform cover as the fight allows.")),
        Rule("Windpipe", "Doomtrain is casting Windpipe.", Normal("Draw-in, then front two rows explode. Move toward the back or use knockback immunity if the strat allows.")),
        Rule("Unlimited Express", "Doomtrain is casting Unlimited Express.", Normal("Current car is getting knocked off. Prepare to be thrown to the next car and heal the hit.")),
        Rule("Turret Crossing", "Doomtrain is casting Turret Crossing.", Normal("Turrets spawn one per row. Watch which side charges and use cargo boxes/platforms to block lasers.")),
        Rule("Electray", "Turrets are casting Electray.", Normal("Turret lasers. Hide behind cargo boxes or use platform height to avoid the firing side.")),
        Rule("Head-on Emission", "Doomtrain is casting Head-on Emission.", Normal("Check the tell. Blue floor wave: go "), Green("UP"), Normal(" on side platforms. Orange headlamp/high hit: stay "), Blue("DOWN"), Normal(".")),
        Rule("Runaway Train", "Doomtrain is casting Runaway Train.", Normal("Intermission starts. Kill the Aether ball before Doom-engine Power reaches 100.")),
        Rule("Aether Surge", "Aether is casting Aether Surge.", Normal("Four conals from the Aether ball. Dodge conals, then adjust for Ghost Train's moving tank conal.")),
        Rule("Arcane Revelation", "Doomtrain is casting Arcane Revelation.", Normal("Circular AoE follows the lightning diamond. Watch the hands for when it stops. Move to the safe side before it explodes.")),
        Rule("Derailment Siege", "Doomtrain is casting Derailment Siege.", Normal("Three-hit stack AoE inside the tower. Stack and mitigate.")),
        Rule("Derail", "Doomtrain is casting Derail.", Normal("This car is being ripped off. Use the rear teleporter to reach the next safe car.")),
        Rule("Battering Arms", "Doomtrain is casting Battering Arms.", Normal("Three-hit stack AoE. Stack, mitigate, and heal through all hits.")),
        Rule("Dead Man's Windpipe", "Doomtrain is casting Dead Man's Windpipe.", Normal("Suck into front two-row slam. Use knockback immunity if needed, but do not KBI car 1.")),
        Rule("Dead Man's Express", "Doomtrain is casting Dead Man's Express.", Normal("Knockback plus lasers. Stand in an "), Green("UP"), Normal(" laser column and resolve knockback safely.")),
        Rule("Ponder the Orb", "Doomtrain intermission orb.", Normal("Orient looking at the little train and listen for the audio tell. Big train smoke: "), Green("2 puffs = light parties"), Normal(", "), Yellow("3 puffs = spreads"), Normal(".")),
        Rule("Spread", "Doomtrain spread mechanic.", Normal("Spread after knock/pull. Supports outside, DPS inside. Ranged/healers use back corners when possible.")),
        Rule("Pairs", "Doomtrain pairs mechanic.", Normal("Pair after knock/pull. Melee front, ranged back. Pairs usually hit 4 DPS or 4 supports.")),
        Rule("Turret", "Doomtrain turret mechanic.", Normal("Find the safe spot from turrets. Use knockback immunity as needed for knock or pull.")),
        Rule("Tankbuster", "Doomtrain tankbuster.", Normal("Tankbuster incoming. Tanks mitigate, party stay clear.")),
        Rule("Raidwide", "Doomtrain raidwide damage.", Normal("Raidwide damage. Mitigate and heal.")),
        Rule("Stack Tower", "Doomtrain stack tower.", Normal("Party share tower. Stack in the tower and be ready to run after the last hit.")),
        Rule("Tower", "Doomtrain tower mechanic.", Normal("Soak assigned tower. If this is the invuln tower strat, tank handles it and everyone else gets ready to run.")),
        Rule("Twister", "Doomtrain twister mechanic.", Normal("Twister coming. Stop clipping each other, use your eyes, and do not stand where another twister will spawn.")),
        Rule("Up/Down", "Doomtrain up/down mechanic.", Normal("Lightning = "), Blue("DOWN"), Normal(" hit. Red light = "), Green("UP"), Normal(" hit. Get safe from first, move up or fall down, then stop moving until snapshot.")),
        Rule("Revelation", "Doomtrain Revelation.", Normal("Ground circle moves randomly. Listen for lightning/crackle and watch the hands. On final move, fall down and run to safe spot or stay up if already safe.")),
        Rule("Defamation", "Doomtrain defamation mechanic.", Normal("Defams do not kill alone, overlapping two does. Spread defams, or stand on one defam if the strat calls for it. Twister may be next.")),
        Rule("Defamations", "Doomtrain defamation mechanic.", Normal("Defams do not kill alone, overlapping two does. Spread defams, or stand on one defam if the strat calls for it. Twister may be next.")),
        Rule("Party Share", "Doomtrain party share.", Normal("Party share stack. Stack together unless height/up-down would prevent you from counting.")),
        Rule("Telegraph", "Doomtrain telegraph.", Normal("Read the platform setup and next mechanic order. Reset early and listen for the crackle.")),
        Rule("Knock", "Doomtrain knockback mechanic.", Normal("Prepare for knockback. Use knockback immunity if free, or position to land safely.")),
        Rule("Pull", "Doomtrain pull mechanic.", Normal("Prepare for pull. Position so the pull does not drag you into danger.")),
        Rule("Enrage", "Doomtrain enrage.", Normal("Final damage check. Use remaining burst, mitigation, and healing.")),
    };

    private static readonly CastRule[] ZeleniaRules =
    {
        Rule("Alexandrian Thunder IV", "Zelenia is casting Alexandrian Thunder IV.", Normal("In/out or out/in. Watch the first AoE, stand on the future safe spot, then move after the first hit resolves.")),
        Rule("Alexandrian Thunder I V", "Zelenia is casting Alexandrian Thunder IV.", Normal("In/out or out/in. Watch the first AoE, stand on the future safe spot, then move after the first hit resolves.")),
        Rule("Shock", "Zelenia is casting Shock.", Normal("Everyone gets traveling point-blank AoEs. "), Yellow("SPREAD"), Normal(", give each other room, and move off dropped puddles fast.")),
        Rule("Power Break", "Zelenia is casting Power Break.", Normal("Watch the charged sword. She cleaves the half of the arena the sword leans toward. Move to the safe half or safe tile.")),
        Rule("Holy Hazard", "Zelenia is casting Holy Hazard.", Normal("Two 2/3 arena cleaves. Find the first safe lane, remember the second safe lane, then move there after the first resolves.")),
        Rule("Specter of the Lost", "Zelenia is casting Specter of the Lost.", Normal("Tankbuster cones on both tanks. Tanks spread away from party; everyone else avoid the cones.")),
        Rule("Thunder Slash", "Zelenia is casting Thunder Slash.", Normal("Six center conal slashes plus Thunder IV in/out. Start near an early cone, resolve in/out, then move into an open slice.")),
        Rule("Perfumed Quietus", "Zelenia is casting Perfumed Quietus.", Normal("Knockback cannot be cancelled. Stay close enough to center before the cutscene, then mitigate raidwide damage.")),
        Rule("Roseblood Bloom", "Zelenia is casting Roseblood Bloom.", Normal("Arena tiles light red. Avoid red-lit tiles and avoid inside the boss hitbox; later mechanics touching red tiles make them explode.")),
        Rule("Alexandrian Thunder III", "Zelenia is casting Alexandrian Thunder III.", Normal("Ground point-blank AoEs resolve in order. Avoid puddles and any red tiles they touch, because those tiles also explode.")),
        Rule("Roseblood Drop", "Zelenia is casting Roseblood Drop.", Normal("Red orb travels down tile edges and lights a new tile. Move to the half the orb does "), Green("NOT"), Normal(" cross.")),
        Rule("Stock Break", "Zelenia is casting Stock Break.", Normal("Four-hit group stack. "), Green("STACK"), Normal(", mitigate, and heal through all hits.")),
        Rule("Valorous Ascension", "Zelenia is casting Valorous Ascension.", Normal("Three raidwide hits, then blade line AoEs. Dodge blades while spreading for the next mechanic.")),
        Rule("Thorned Catharsis", "Zelenia is casting Thorned Catharsis.", Normal("Raidwide damage. Mitigate and heal.")),
    };

    private static readonly CastRule[] KirinRules =
    {
        Rule("Stonega IV", "Faithbound Kirin is casting Stonega IV.", Normal("Raidwide damage. Mitigate and heal.")),
        Rule("Wrought Arms", "Faithbound Kirin is casting Wrought Arms.", Normal("Arms spawn east and west. Watch for echoed attacks from the arms after Kirin's first hit.")),
        Rule("Synchronized Strike", "Faithbound Kirin is casting Synchronized Strike.", Normal("Kirin line AoEs go first, then the arms hit the spaces that were safe. Be ready to move quickly.")),
        Rule("Crimson Riddle", "Faithbound Kirin is casting Crimson Riddle.", Normal("Check the tell. Glowing tail: move to Kirin's front half. Fire breath charge: move behind him.")),
        Rule("Summon Shijin", "Faithbound Kirin is casting Summon Shijin.", Normal("One Four Lord joins. Identify Byakko, Genbu, Suzaku, or Seiryu and handle that lord's pattern.")),
        Rule("Quake", "Faithbound Kirin is casting Quake.", Normal("Three sets of circular AoEs drop on players. Keep moving and leave space for later sets.")),
        Rule("Stonega III", "Faithbound Kirin is casting Stonega III.", Normal("Eight player circle AoEs. Spread after the tiny Suzaku safe area opens.")),
        Rule("Double Cast", "Faithbound Kirin is casting Double Cast.", Normal("Some players drop circles, others carry traveling circles. Stack dropped circles together, traveling circles spread out.")),
        Rule("Wringer", "Faithbound Kirin is casting Wringer.", Normal("Point-blank first, then arm donut. Move into the point-blank space after it resolves.")),
        Rule("Striking Right", "Faithbound Kirin is casting Striking Right.", Normal("Avoid Kirin's right-side circle and the charged arm explosion. Move as soon as you identify both.")),
        Rule("Striking Left", "Faithbound Kirin is casting Striking Left.", Normal("Avoid Kirin's left-side circle and the charged arm explosion. Move as soon as you identify both.")),
        Rule("Double Wringer", "Faithbound Kirin is casting Double Wringer.", Normal("Resolve point-blank first. Then dodge the arm donut plus the projected strike, then the echoed arm hit.")),
        Rule("Synchronized Sequence", "Faithbound Kirin is casting Synchronized Sequence.", Normal("Projected linears resolve first, then outer linears plus point-blank, then the donut.")),
        Rule("Striking Right Sequence", "Faithbound Kirin is casting Striking Right Sequence.", Normal("Right-side strike first, then charged arm plus point-blank, then arm donut.")),
        Rule("Striking Left Sequence", "Faithbound Kirin is casting Striking Left Sequence.", Normal("Left-side strike first, then charged arm plus point-blank, then arm donut.")),
        Rule("Mighty Grip", "Faithbound Kirin is casting Mighty Grip.", Normal("Center square becomes safe with four tank towers. Three tanks fill the open towers.")),
        Rule("Deadly Hold", "Faithbound Kirin is casting Deadly Hold.", Normal("Tower tankbusters. Tanks soak towers with cooldowns or the raid gets crushed.")),
        Rule("Kirin Captivator", "Faithbound Kirin is casting Kirin Captivator.", Normal("DPS check. Kill both arms before the cast finishes or the raid dies.")),
    };

    private static readonly CastRule[] ByakkoRules =
    {
        Rule("Gloaming Gleam", "Duskbound Byakko is casting Gloaming Gleam.", Normal("Byakko fires wall-to-wall line AoE, then pounces in a large circle. Dodge the line and be ready for the landing AoE.")),
    };

    private static readonly CastRule[] GenbuRules =
    {
        Rule("Shattering Stomp", "Moonbound Genbu is casting Shattering Stomp.", Normal("Raidwide plus water geysers in two sets. Start on crack spaces with no geysers, then move into the first safe spots.")),
        Rule("Midwinter March", "Moonbound Genbu is casting Midwinter March.", Normal("Out then in. Dodge the first circle, then move into that space for the donut.")),
    };

    private static readonly CastRule[] SuzakuRules =
    {
        Rule("Vermillion Flight", "Sunbound Suzaku is casting Vermillion Flight.", Normal("Suzaku dives across center and fireballs expand. Avoid the center line and the fireball circles.")),
    };

    private static readonly CastRule[] SeiryuRules =
    {
        Rule("Eastwind Wheel", "Dawnbound Seiryu is casting Eastwind Wheel.", Normal("Rotating line AoE. Watch arrow direction and move behind the sweeping path.")),
    };

    private static readonly CastRule[] DetectorRules =
    {
        Rule("Electray", "Little Detector is casting Electray.", Normal("Long line AoE at a random player. Dodge the line and watch for stacked lines covering the floor.")),
        Rule("Electroswipe", "Big Detector is casting Electroswipe.", Normal("Wide cone at a random player. This is interruptible if your job can interrupt.")),
    };

    private static readonly CastRule[] OmegaRules =
    {
        Rule("Ion Efflux", "Omega is casting Ion Efflux.", Normal("Raidwide damage. Mitigate and heal.")),
        Rule("Fore-to-aft Fire", "Omega is casting Fore-to-aft Fire.", Normal("Front cleave first, then rear cleave. Wait behind Omega, then move in front after the first blast.")),
        Rule("Aft-to-fore Fire", "Omega is casting Aft-to-fore Fire.", Normal("Rear/front order is flipped. Use the blue ground warning and dodge within the safe lane.")),
        Rule("Anti-personnel Missiles", "Omega is firing Anti-personnel Missiles.", Normal("Traveling AoEs on players. Spread quickly and avoid clipping others.")),
        Rule("Surface Missile", "Omega is casting Surface Missile.", Normal("Three waves of floor eighths. Memorize the safe tiles for all waves and move through them in order.")),
        Rule("Trajectory Projection", "Omega is casting Trajectory Projection.", Normal("Offset spread AoEs with countdown. Move near edge, watch the projected direction, then run opposite at zero.")),
        Rule("Hyper Pulse", "Omega is casting Hyper Pulse.", Normal("Proximity pulse from the new airship. Move to the edges and heal up after Citadel Buster.")),
    };

    private static readonly CastRule[] UltimaRules =
    {
        Rule("Antimatter", "Ultima is casting Antimatter.", Normal("Three tankbusters, one per alliance tank. Tanks mitigate; party stay away.")),
        Rule("Energy Orb", "Ultima is casting Energy Orb.", Normal("Laser lanes. Watch north/south/center orbs and any Mana Screen bounces; stand outside the projected laser path.")),
        Rule("Tractor Beam", "Ultima is casting Tractor Beam.", Normal("Move to the side where arrows originate, away from Ultima. Watch the pulled airship and avoid that half.")),
        Rule("Mana Screen", "Ultima is casting Mana Screen.", Normal("Transparent shield will split and bounce Energy Orb lasers. Track the shield lanes before the laser fires.")),
        Rule("Tractor Field", "Ultima is casting Tractor Field.", Normal("Draw-in, then run through circle AoE waves to the green launch line. Sprint or dash if needed.")),
        Rule("Citadel Buster", "Ultima is casting Citadel Buster.", Normal("Raidwide damage. Mitigate and heal before Omega's proximity hit.")),
        Rule("Chemical Bomb", "Ultima is casting Chemical Bomb.", Normal("Four proximity bombs rotate around the field. Run the edge opposite the bombs to reduce damage.")),
    };

    private static readonly CastRule[] KrakenRules =
    {
        Rule("Sucker", "Kraken is casting Sucker.", Normal("Large draw-in followed by Flood point-blank. Get halfway out or use knockback immunity.")),
        Rule("Flood", "Kraken is casting Flood.", Normal("Point-blank after Sucker. Be outside the small AoE.")),
    };

    private static readonly CastRule[] LightElementalRules =
    {
        Rule("Banishga", "Light Elemental is casting Banishga.", Normal("Circle AoE on a random player. Move out.")),
    };

    private static readonly CastRule[] BansheeRules =
    {
        Rule("Paralyze III", "Banshee is casting Paralyze III.", Normal("Circle AoE on a random player. Move out.")),
    };

    private static readonly CastRule[] AlkyoneusRules =
    {
        Rule("Impact Roar", "Alkyoneus is casting Impact Roar.", Normal("Raidwide damage. Mitigate and heal.")),
        Rule("Mighty Strikes", "Alkyoneus is casting Mighty Strikes.", Normal("Interrupt this if possible; it likely buffs Alkyoneus' attacks.")),
        Rule("Catapult", "Alkyoneus is casting Catapult.", Normal("Three quick circular AoEs. Keep moving out of each circle.")),
    };

    private static readonly CastRule[] GiantRangerRules =
    {
        Rule("Power Attack", "Giant Ranger is casting Power Attack.", Normal("Large cone at a random player. Dodge the cone.")),
    };

    private static readonly CastRule[] GiantAsceticRules =
    {
        Rule("Catapult", "Giant Ascetic is casting Catapult.", Normal("Circle AoE on a random player. Move out.")),
    };

    private static readonly CastRule[] KamlanautRules =
    {
        Rule("Enspirited Swordplay", "Kam'lanaut is casting Enspirited Swordplay.", Normal("Raidwide damage. Mitigate and heal.")),
        Rule("Proving Ground", "Kam'lanaut is casting Proving Ground.", Normal("Boss hitbox/floor becomes dangerous and applies bleed. Stay out of the shimmering danger zone.")),
        Rule("Elemental Blade", "Kam'lanaut is casting Elemental Blade.", Normal("Watch the elemental rings. Those elements will expand during Sublime Elements.")),
        Rule("Sublime Elements", "Kam'lanaut is casting Sublime Elements.", Normal("Avoid the indicated expanding elements. For conals, avoid widened conals; for linears, stand between safe lines.")),
        Rule("Princely Blow", "Kam'lanaut is casting Princely Blow.", Normal("Tank line busters with knockback. Tanks spread; non-tanks do not stand behind tanks.")),
        Rule("Light Blade", "Kam'lanaut is casting Light Blade.", Normal("Avoid the center line, then read the perpendicular light blades and move to an uncovered quadrant/side.")),
        Rule("Great Wheel", "Kam'lanaut is casting Great Wheel.", Normal("Point-blank first, then he spins into a frontal cleave. Dodge out, then get behind him.")),
        Rule("Esoteric Scrivening", "Kam'lanaut is casting Esoteric Scrivening.", Normal("Arena transition plus raidwide damage. Mitigate and heal.")),
        Rule("Transcendent Union", "Kam'lanaut is casting Transcendent Union.", Normal("Multiple raidwide hits followed by a big hit. Heavy mitigation and healing.")),
        Rule("Esoteric Palisade", "Kam'lanaut is casting Esoteric Palisade.", Normal("Elemental crystals spawn and extended platforms become lethal. Stay on the main safe floor.")),
        Rule("Crystalline Resonance", "Kam'lanaut is casting Crystalline Resonance.", Normal("Match the growing elemental ring to the crystal it will explode. Avoid that crystal's large circle.")),
        Rule("Empyreal Banish III", "Kam'lanaut is casting Empyreal Banish III.", Normal("Spread AoEs in limited space. Spread carefully without trapping others.")),
        Rule("Illumed Facet", "Kam'lanaut is casting Illumed Facet.", Normal("Clones fire lines down paths. Stand just left or right of a clone's front/back line, or between floor hexes.")),
        Rule("Shield Bash", "Kam'lanaut is casting Shield Bash.", Normal("Center knockback. Line up with a protruding path or use knockback immunity.")),
        Rule("Empyreal Banish IV", "Kam'lanaut is casting Empyreal Banish IV.", Normal("Healer stack. Stack together and mitigate.")),
    };

    private static readonly CastRule[] EaldnarcheRules =
    {
        Rule("Uranos Cascade", "Eald'narche is casting Uranos Cascade.", Normal("Tankbusters with splash damage. Tanks spread, party avoid tank circles.")),
        Rule("Cronos Sling", "Eald'narche is casting Cronos Sling.", Normal("Read card shape: cylinder means go in for donut, flattened orb means move out. Then dodge the half-room cleave.")),
        Rule("Empyreal Vortex", "Eald'narche is casting Empyreal Vortex.", Normal("Ground AoEs plus player AoEs with raidwide hits. Spread and heal hard, especially marked players.")),
        Rule("Warp", "Eald'narche is casting Warp.", Normal("Boss teleports to matching corner symbol. Get to that corner and behind him quickly.")),
        Rule("Sleepga", "Eald'narche is casting Sleepga.", Normal("Avoid the AoE or you will be slept. Esuna slept players if they get caught.")),
        Rule("Gaea Stream", "Eald'narche is casting Gaea Stream.", Normal("Stand behind a moving line opposite its arrows. After it moves, step into the space it left.")),
        Rule("Omega Javelin", "Eald'narche is casting Omega Javelin.", Normal("Traveling AoEs that leave crystals and explode again. Spread without crowding center or trapping corners.")),
        Rule("Duplicate", "Eald'narche is casting Duplicate.", Normal("Magic tile pulses outward cardinally, then expands. Stand diagonally from pulsing tile or find the safe tile pair.")),
        Rule("Excelsior", "Eald'narche is casting Excelsior.", Normal("Tile destruction incoming with stun/gather. Note the doomed tile.")),
        Rule("Phase Shift", "Eald'narche is casting Phase Shift.", Normal("Arena changes to the eight-tile platform. Reorient quickly.")),
        Rule("Visions of Paradise", "Eald'narche is casting Visions of Paradise.", Normal("One magic tile moves to the empty space. Recalculate the single safe tile after the shift.")),
        Rule("Stellar Burst", "Eald'narche is casting Stellar Burst.", Normal("Stack mechanic on a grabbed player. Stack together.")),
        Rule("Ancient Triad", "Eald'narche is casting Ancient Triad.", Normal("Three element combo. Identify Water/Wind, Lightning/Fire, and Earth/Ice, then resolve each element's danger.")),
    };

    private static readonly CastRule[] PrisheRules =
    {
        Rule("Banishga", "Prishe is casting Banishga.", Normal("Raidwide damage. Mitigate and heal.")),
        Rule("Knuckle Sandwich", "Prishe is casting Knuckle Sandwich.", Normal("Count charges. 1 = small yellow, 2 = orange, 3 = purple. Dodge out, then step inside for the donut.")),
        Rule("Nullifying Dropkick", "Prishe is casting Nullifying Dropkick.", Normal("Shared tankbuster. Tanks stack/mitigate as planned; party stay clear.")),
        Rule("Banish Storm", "Prishe is casting Banish Storm.", Normal("Staves shoot moving circle AoEs along arrow paths. Stand between projected paths.")),
        Rule("Holy", "Prishe is casting Holy.", Normal("Traveling AoEs on many players. Spread while dodging remaining Banish Storm hits.")),
        Rule("Crystalline Thorns", "Prishe is casting Crystalline Thorns.", Normal("Floor spikes leave overlapping squares and two corners safe. Stay near boss/center, not far corners.")),
        Rule("Auroral Uppercut", "Prishe is casting Auroral Uppercut.", Normal("Count charges for knockback distance. Aim from center into a safe thorn gap; falling or landing in spikes is bad.")),
        Rule("Banishga IV", "Prishe is casting Banishga IV.", Normal("Light orb waves go outer-in or inner-out. Stand near the next safe line and move into recently exploded spaces.")),
        Rule("Asuran Fists", "Prishe is casting Asuran Fists.", Normal("Eight-hit tower stack. Everyone stack and mitigate.")),
    };

    private static readonly CastRule[] FafnirRules =
    {
        Rule("Dark Matter Blast", "Fafnir is casting Dark Matter Blast.", Normal("Raidwide damage. Mitigate and heal.")),
        Rule("Offensive Posture", "Fafnir is using Offensive Posture.", Normal("Read the tell. Tail up: front quarter safe. Landing: move to edge. Mouth heat: dodge donut wave and remember origin.")),
        Rule("Baleful Breath", "Fafnir is casting Baleful Breath.", Normal("Four-hit alliance line stack. Stack together and mitigate.")),
        Rule("Sharp Spike", "Fafnir is casting Sharp Spike.", Normal("Tankbusters on each party tank. Tanks mitigate, party stay away.")),
        Rule("Pestilent Sphere", "Darter is casting Pestilent Sphere.", Normal("Poisoning sphere on target. Tanks hold adds and heal through poison.")),
        Rule("Hurricane Wing", "Fafnir is casting Hurricane Wing.", Normal("Eight raidwide hits, then tornado/wind rings. Stay mid, dodge pulses, avoid instant-death tornadoes.")),
        Rule("Horrid Roar", "Fafnir is casting Horrid Roar.", Normal("Ground AoEs under players. Spread and keep moving, especially during Hurricane Wing.")),
        Rule("Absolute Terror", "Fafnir is casting Absolute Terror.", Normal("Wide line across arena from wall. Move to the sides.")),
        Rule("Winged Terror", "Fafnir is casting Winged Terror.", Normal("Wing cleaves leave centerline safe. Move onto the line Fafnir faces.")),
    };

    private static readonly CastRule[] JeunoTrashRules =
    {
        Rule("Seismostomp", "Goblin Replica is casting Seismostomp.", Normal("Circle AoE on a random player, then the goblin jumps there. Move out of the circle.")),
        Rule("Vanguard Pathfinder", "Vanguard Pathfinder is casting Vanguard Pathfinder.", Normal("Circle AoE on a random player. Move out.")),
        Rule("Scoop", "Elder Goobbue is casting Scoop.", Normal("Wide cone at a random player. Dodge the cone.")),
        Rule("Hundred Fists", "Aquarius is casting Hundred Fists.", Normal("Interruptible tankbuster. Interrupt if possible, otherwise tank mitigate.")),
        Rule("Mysterious Light", "Sprinkler is casting Mysterious Light.", Normal("Gaze mechanic. Look away or get Blinded.")),
        Rule("Isle Drop", "Groundskeeper or Despot is casting Isle Drop.", Normal("Circle AoE on a random player. Move out.")),
        Rule("Scrapline Storm", "Despot is casting Scrapline Storm.", Normal("Draw-in, small point-blank, then donut. Get pulled out enough, dodge point-blank, then move in.")),
        Rule("Panzerfaust", "Despot is casting Panzerfaust.", Normal("Interruptible tankbuster. Interrupt if possible, otherwise tank mitigate.")),
        Rule("Wing Cutter", "Flamingo is casting Wing Cutter.", Normal("Short wide cone at a random player. Dodge the cone.")),
    };

    private static readonly CastRule[] ArkAngelMrRules =
    {
        Rule("Cloud Splitter", "Ark Angel MR is casting Cloud Splitter.", Normal("Tankbusters with attached circles. Tanks spread, party avoid them.")),
        Rule("Havoc Spiral", "Ark Angel MR is casting Havoc Spiral.", Normal("Three rotating conals. Watch arrow color/direction and rotate with the safe space.")),
        Rule("Spiral Finish", "Ark Angel MR is casting Spiral Finish.", Normal("Knockback from center. Stay close enough or use knockback immunity.")),
        Rule("Rampage", "Ark Angel MR is casting Rampage.", Normal("Four line dashes then a large circle. Avoid lines first, then move out of the circle.")),
    };

    private static readonly CastRule[] ArkAngelGkRules =
    {
        Rule("Meikyo Shisui", "Ark Angel GK is casting Meikyo Shisui.", Normal("Dodge ice weave twice, look away from gaze orb, avoid growing pink circle, then move into its donut safe spot.")),
        Rule("Dragonfall", "Ark Angel GK is casting Dragonfall.", Normal("Party stacks on healers. Each party stacks separately; do not combine all three.")),
    };

    private static readonly CastRule[] ArkAngelTtRules =
    {
        Rule("Meteor", "Ark Angel TT is casting Meteor.", Normal("INTERRUPT. Meteor kills everyone if it finishes.")),
        Rule("Guillotine", "Ark Angel TT is casting Guillotine.", Normal("Four slashes cover front 2/3. Move behind TT and stay there until all hits finish.")),
    };

    private static readonly CastRule[] ArkAngelEvRules =
    {
        Rule("Dominion Slash", "Ark Angel EV is casting Dominion Slash.", Normal("Raidwide plus light puddles. Avoid puddles, especially if HM clones are chasing.")),
        Rule("Holy", "Ark Angel EV is casting Holy.", Normal("Raidwide damage. Mitigate and heal.")),
        Rule("Arrogance Incarnate", "Ark Angel EV is casting Arrogance Incarnate.", Normal("Five-hit alliance stack. Stack and mitigate.")),
    };

    private static readonly CastRule[] ArkAngelHmRules =
    {
        Rule("Utsusemi", "Ark Angel HM is casting Utsusemi.", Normal("Clones chase tethered players after Mighty Strikes. Keep running until they disappear.")),
        Rule("Cross Reaver", "Ark Angel HM is casting Cross Reaver.", Normal("Cross-shaped AoE through center. Keep moving if clones are chasing, then dodge the cross.")),
        Rule("Mijin Gakure", "Ark Angel HM is casting Mijin Gakure.", Normal("Interrupt after EV shield is down. If this finishes, it explodes the raid.")),
        Rule("Mighty Strikes", "Ark Angel HM is casting Mighty Strikes.", Normal("Clone/attack charge. Do not let tethered clones catch you.")),
    };

    private static readonly CastRule[] ShadowLordRules =
    {
        Rule("Giga Slash", "Shadow Lord is casting Giga Slash.", Normal("Watch phantom sword paths. Dodge side cleaves in order; direct left/right are often safest on correct side.")),
        Rule("Umbra Smash", "Shadow Lord is casting Umbra Smash.", Normal("Cardinal linears split outward. Stay near middle and sidestep the splitting lines.")),
        Rule("Flames of Hatred", "Shadow Lord is casting Flames of Hatred.", Normal("Raidwide damage. Mitigate and heal.")),
        Rule("Implosion", "Shadow Lord is casting Implosion.", Normal("Point-blank plus half-room cleave. Watch which hand has the larger dark orb and avoid that side.")),
        Rule("Cthonic Fury", "Shadow Lord is casting Cthonic Fury.", Normal("Raidwide and arena pattern change. Stand in one of the connected safe circles/paths.")),
        Rule("Dark Nebula", "Shadow Lord is casting Dark Nebula.", Normal("Linear knockback along paths. Position to be knocked from one safe circle/path into the next.")),
        Rule("Echoes of Agony", "Shadow Lord is casting Echoes of Agony.", Normal("Multi-hit alliance stack. Stack and mitigate.")),
        Rule("Nightfall", "Shadow Lord is casting Nightfall.", Normal("Phase change. Recenter and prepare for upgraded Giga Slash.")),
        Rule("Giga Slash: Nightfall", "Shadow Lord is casting Giga Slash: Nightfall.", Normal("Three slashes: left/right sequence plus front or back cleave. Watch phantom sword indicators.")),
        Rule("Shadow Spawn", "Shadow Lord is casting Shadow Spawn.", Normal("Clones spawn. Watch enemy list and clone tells while resolving boss mechanics.")),
        Rule("Unbridled Rage", "Shadow Lord is casting Unbridled Rage.", Normal("Linear tankbusters. Tanks spread lines; non-tanks avoid between boss and tanks.")),
        Rule("Binding Sigil", "Shadow Lord is casting Binding Sigil.", Normal("Memorize three sigil patterns and move through safe spots. Getting bound can snowball into death.")),
        Rule("Damning Strikes", "Shadow Lord is casting Damning Strikes.", Normal("Three stack towers. Split by party or assigned groups and soak.")),
        Rule("Doom Arc", "Shadow Lord is casting Doom Arc.", Normal("Raidwide damage plus Bleeding. Mitigate and heal the bleed.")),
    };

    private static readonly CastRule[] QueenEternalRules =
    {
        Rule("Legitimate Force", "Queen Eternal is casting Legitimate Force.", Normal("Watch all four arms. Same side = stay safe side; opposite sides = swap after first blast.")),
        Rule("Aethertithe", "Queen Eternal is casting Aethertithe.", Normal("Green floor grid pulls show lethal draw-in conals. Dodge left, right, and middle pulls in order.")),
        Rule("Coronation", "Queen Eternal is casting Coronation.", Normal("If personal circle follows you, let it lock, then step out. Drone tether version: tethered players hold front/back to center line AoE.")),
        Rule("Prosecution of War", "Queen Eternal is casting Prosecution of War.", Normal("Tankbuster. Tank mitigate; party stay clear.")),
        Rule("Virtual Shift", "Queen Eternal is casting Virtual Shift.", Normal("Platform change plus raidwide damage. Mitigate and reorient.")),
        Rule("Downburst", "Queen Eternal is casting Downburst.", Normal("Corner knockback. Aim along long paths or use knockback immunity.")),
        Rule("Powerful Gust", "Queen Eternal is casting Powerful Gust.", Normal("Strong knockback. Stand near arrow origin on a straight lane or use knockback immunity.")),
        Rule("Castellation", "Queen Eternal is casting Castellation.", Normal("Wall with gaps. Floating = use high gap. Grounded = use low gap. Stay over solid platforms.")),
        Rule("Absolute Authority", "Queen Eternal is casting Absolute Authority.", Normal("DPS flares corners, non-healers stop for Acceleration Bomb, then look away from healer gazes and pair in safe squares.")),
    };

    private static readonly CastRule[] ZoraalJaRules =
    {
        Rule("Soul Overflow", "Zoraal Ja is casting Soul Overflow.", Normal("Raidwide damage. Later versions spawn Mamool Ja circles; avoid them before they explode.")),
        Rule("Double-edged Swords", "Zoraal Ja is casting Double-edged Swords.", Normal("Front half then back half cleave. Shuffle to the safe half after the first hit.")),
        Rule("Patricidal Pique", "Zoraal Ja is casting Patricidal Pique.", Normal("Tankbuster. Tank mitigate; party stay clear.")),
        Rule("Calamity's Edge", "Zoraal Ja is casting Calamity's Edge.", Normal("Raidwide damage. Mitigate and heal.")),
        Rule("Vorpal Trail", "Zoraal Ja is casting Vorpal Trail.", Normal("Swords trace lines, turn 90 degrees, and return. Track blade paths while handling other mechanics.")),
        Rule("Smiting Circuit", "Zoraal Ja is casting Smiting Circuit.", Normal("Sword pattern across hitbox = point-blank, move out. Swords outside = donut, move in.")),
        Rule("Dawn of an Age", "Zoraal Ja is casting Dawn of an Age.", Normal("Raidwide damage. Mitigate and heal.")),
        Rule("Vollok", "Zoraal Ja is casting Vollok.", Normal("Sword tile patterns appear. Identify safe tiles or future safe quadrant when outer platforms copy in.")),
        Rule("Bitter Reaping", "Zoraal Ja is casting Bitter Reaping.", Normal("Tankbusters on both tanks. Tanks mitigate and spread.")),
        Rule("Sync", "Zoraal Ja is casting Sync.", Normal("Outer platform sword pattern copies onto main grid. Find the safe tiles before it appears.")),
        Rule("Gateway", "Zoraal Ja is casting Gateway.", Normal("Blue connectors show where blade rows will travel from outer platforms to main grid.")),
        Rule("Blade Warp", "Zoraal Ja is casting Blade Warp.", Normal("Blades appear on connected platforms. Watch which ones tether before Forged Track.")),
        Rule("Forged Track", "Zoraal Ja is casting Forged Track.", Normal("Tethered blades travel along connector rows. Avoid the copied rows on the main grid.")),
        Rule("Actualize", "Zoraal Ja is casting Actualize.", Normal("Raidwide damage. Mitigate and heal.")),
        Rule("Half Full", "Zoraal Ja is casting Half Full.", Normal("Glowing sword on right/left cleaves that half. Move to the other side.")),
        Rule("Half Circuit", "Zoraal Ja is casting Half Circuit.", Normal("Combine side cleave with point-blank or donut. Find the safe slice between both AoEs, then spread circles.")),
        Rule("Duty's Edge", "Zoraal Ja is casting Duty's Edge.", Normal("Four-hit line stack. Stack together and mitigate.")),
    };

    private static readonly CastRule[] NecronRules =
    {
        Rule("Fear of Death", "Necron is casting Fear of Death.", Normal("Raidwide plus arm clusters that fire lines where they face. Move where no arms point.")),
        Rule("Cold Grip", "Necron is casting Cold Grip.", Normal("Center safe first, then dripping purple arm sweeps inward. Move to the far third away from that arm.")),
        Rule("Memento Mori", "Necron is casting Memento Mori.", Normal("Center laser, then hands fire perpendicular lines from sludge path. Stand between hands on your side.")),
        Rule("Blue Shockwave", "Necron is casting Blue Shockwave.", Normal("Traveling tank conal. Main tank moves to a side; party uses freed safe space.")),
        Rule("Soul Reaping", "Necron is casting Soul Reaping.", Normal("Remember shape shown. Donut means later move in; circle means later move out.")),
        Rule("Aetherblight", "Necron is casting Aetherblight.", Normal("Stored Soul Reaping resolves. Donut = move in. Circle = move out.")),
        Rule("Grand Cross", "Necron is casting Grand Cross.", Normal("Small arena with spinning timed lines. Dodge lines when timers hit zero, then go off-cardinal edge for proximity lines.")),
        Rule("Neutron Ring", "Necron is casting Neutron Ring.", Normal("Raidwide damage and return to full platform. Mitigate and heal.")),
        Rule("Darkness of Eternity", "Necron is casting Darkness of Eternity.", Normal("Fear meter blast, then personal doom prison. Kill hands and escape by jump pads before Doom ends.")),
        Rule("Specter of Death", "Necron is casting Specter of Death.", Normal("Spooky hands slap two rows. Adjust to avoid the hand lanes or you get sent to the abyss.")),
        Rule("Relentless Reaping", "Necron is casting Relentless Reaping.", Normal("Four stored circle/donut shapes alternating. Remember odd/even shapes.")),
        Rule("Crop Rotation", "Necron is casting Crop Rotation.", Normal("Diamond counters rotate. Note which number starts; mechanics resolve in order from there.")),
        Rule("Seasons of Blight", "Necron is casting Seasons of Blight.", Normal("Stored shapes resolve by counter order. Do the in/out dance based on remembered shapes.")),
        Rule("Mass Macabre", "Necron is casting Mass Macabre.", Normal("Four towers need four players each. Split up and fill them.")),
    };

    private static readonly CastRule[] ValigarmandaRules =
    {
        Rule("Ice Talon", "Valigarmanda is casting Ice Talon.", Normal("Tankbusters on both tanks with Frostbite. Tanks mitigate and spread; party stay clear.")),
        Rule("Skyruin", "Valigarmanda is casting Skyruin.", Normal("Raidwide damage with DoT and elemental platform shift. Mitigate, heal, and identify ice or lightning setting.")),
        Rule("Freezing Dust", "Valigarmanda is casting Freezing Dust.", Normal("Keep moving when the timer resolves. If you stop, you get trapped in ice and need breaking out.")),
        Rule("Disaster Zone", "Valigarmanda is casting Disaster Zone.", Normal("Raidwide damage and return to rocky platform. Mitigate and heal.")),
        Rule("Thunderous Breath", "Valigarmanda is casting Thunderous Breath.", Normal("Huge floor conal. Stand on a glowing tile to float above it.")),
        Rule("Hail of Feathers", "Valigarmanda is casting Hail of Feathers.", Normal("Feathers strike non-glowing tiles. Move to open plain tiles and get low before lightning.")),
        Rule("Blighted Bolt", "Valigarmanda is casting Blighted Bolt.", Normal("Lightning hits high/floating players. Stand on low/plain tiles, not glowing levitation tiles.")),
        Rule("Ruinfall", "Valigarmanda is casting Ruinfall.", Normal("Two-tank tower plus knockback wave. Tanks soak; use knockback immunity or stand front to be pushed between back AoEs.")),
        Rule("Ruin Foretold", "Valigarmanda is casting Ruin Foretold.", Normal("Raidwide damage. Mitigate and heal.")),
        Rule("Calamitous Cry", "Valigarmanda is casting Calamitous Cry.", Normal("Wild charge through nails while dodging conal fans. Stack in charge, kill nails, and move nail to nail.")),
        Rule("Tulidisaster", "Valigarmanda is casting Tulidisaster.", Normal("Damage based on remaining Ruinous Power. Heavy mitigate and heal; final fire hit leaves DoT.")),
        Rule("Eruption", "Valigarmanda is casting Eruption.", Normal("Three players get three sequential eruption circles. Stack bait if planned, then keep moving out of circles.")),
        Rule("Arcane Circles of Ice", "Valigarmanda summons arcane circles of ice.", Normal("Starburst ice circles fire 8-direction lines. Stand between line paths; getting hit traps you in ice.")),
        Rule("Arcane Circles of Thunder", "Valigarmanda summons arcane circles of thunder.", Normal("Thunder circles create lane AoEs. Stand in the open lane; glowing tile is okay if Thunderous Breath overlaps.")),
        Rule("Avalanche", "Valigarmanda avalanche warning.", Normal("Diagonal half-room avalanche from southeast. Identify which half is covered and move to the safe triangle.")),
        Rule("Watch the Birdy", "Valigarmanda visual tell.", Normal("Read the bird tell: wind circle = go center for donut; talons/gather strength = move far out; open beak = front corners safe.")),
    };

    private static readonly CastRule[] ShantottoRules =
    {
        Rule("Flare Play", "Shantotto is casting Flare Play.", Normal("Raidwide magic damage. Mitigate and heal.")),
        Rule("Vidohunir", "Shantotto is casting Vidohunir.", Normal("Shared tankbuster for three tanks. Tanks stack/share and mitigate.")),
        Rule("Empirical Research", "Shantotto is casting Empirical Research.", Normal("Line AoE in the direction Shantotto faces. Follow her facing and move out of the line.")),
        Rule("Superior Stone II", "Shantotto is casting Superior Stone II.", Normal("Raidwide plus north/south cliffs. Find the column where cliffs are furthest apart for later safe spot.")),
        Rule("Groundbreaking Quake", "Shantotto is casting Groundbreaking Quake.", Normal("Stone cliffs smash together. Stand between the north/south cliffs with the biggest gap.")),
        Rule("Diagrammatic Doorway", "Shantotto is casting Diagrammatic Doorway.", Normal("Ley line jump sequence. Follow Shantotto through jumps and be ready for donut jumps.")),
        Rule("Circumscribed Fire", "Shantotto is casting Circumscribed Fire.", Normal("Donut around Shantotto after each ley line jump. Stay near/follow her, then move out after final jump.")),
        Rule("Localized Blizzard", "Shantotto is casting Localized Blizzard.", Normal("Point-blank at final ley line after donuts. Move out and spread for follow-up attacks.")),
        Rule("Thunder and Error", "Shantotto is casting Thunder and Error.", Normal("Marked AoEs on players. Spread out.")),
        Rule("Meteoric Rhyme", "Shantotto is casting Meteoric Rhyme.", Normal("Ground AoEs, two proximity meteors, then a stack marker. Dodge, move from meteors, then stack.")),
        Rule("Shockwave", "Shantotto is casting Shockwave.", Normal("Meteor raidwide, followed by circle and line AoEs. Mitigate and keep dodging.")),
        Rule("Aero Dynamics", "Shantotto is casting Aero Dynamics.", Normal("Timer and arrow push on player. Use cliffs north/south to break the knockback.")),
        Rule("Final Exam", "Shantotto is casting Final Exam.", Normal("Multi-hit stack marker. Stack together and mitigate.")),
    };

    private static readonly CastRule[] AlexanderRules =
    {
        Rule("Banishga IV", "Alexander is casting Banishga IV.", Normal("Raidwide magic damage. Mitigate and heal.")),
        Rule("Divine Arrow", "Alexander is casting Divine Arrow.", Normal("Note start direction and spin. Rotating conal plus ring AoEs, then ground/marked AoEs.")),
        Rule("Impartial Ruling", "Alexander is casting Impartial Ruling.", Normal("Left/right gauges fill, then that side cleaves. Watch gauges and dodge each side in order.")),
        Rule("Radiant Sacrament", "Alexander is casting Radiant Sacrament.", Normal("Grid columns fire and return. Stand beside first columns, then move into them after they resolve.")),
        Rule("Divine Spear", "Alexander is casting Divine Spear.", Normal("Triangle AoEs form from arena lines. Stay middle until pattern is clear, then move out.")),
        Rule("Mega Holy", "Alexander is casting Mega Holy.", Normal("Multi-hit healer stack. Stack and mitigate.")),
        Rule("Perfect Defense", "Alexander is casting Perfect Defense.", Normal("Alexander invulnerable. Kill four Gordius Systems; watch for donut or point-blank add AoEs.")),
        Rule("Divine Judgment", "Alexander is casting Divine Judgment.", Normal("Massive raidwide damage. Heavy mitigation and healing.")),
        Rule("Electrify", "Alexander is casting Electrify.", Normal("Tethers containers in two sets. Large circle AoEs resolve in tether order.")),
    };

    private static readonly CastRule[] GordiusSystemRules =
    {
        Rule("Donut", "Gordius System is using a donut AoE.", Normal("Move inside the donut safe zone.")),
        Rule("Point-blank", "Gordius System is using a point-blank AoE.", Normal("Move away from the add.")),
    };

    private static readonly CastRule[] PromathiaRules =
    {
        Rule("Empty Salvation", "Promathia is casting Empty Salvation.", Normal("Raidwide magic damage and floor puddles. Mitigate, heal, and avoid puddles.")),
        Rule("Fleeting Eternity", "Promathia is casting Fleeting Eternity.", Normal("Light travels between tethered puddles. Watch where the purple light goes and avoid the large AoE at each puddle.")),
        Rule("Wheel of Impregnability", "Promathia is casting Wheel of Impregnability.", Normal("Closing inward ring. When rings meet, AoE hits the ring area; move out of the marked ring.")),
        Rule("Pestilent Penance", "Promathia is casting Pestilent Penance.", Normal("Boss jumps corner and room AoE, then closing outward ring becomes donut. Dodge the room AoE and be ready for donut.")),
        Rule("False Genesis", "Promathia is casting False Genesis.", Normal("Three-platform add phase. Kill your Memory Receptacle, stand by wall for knockback, then help other platforms.")),
        Rule("Deadly Rebirth", "Promathia is casting Deadly Rebirth.", Normal("Massive raidwide magic damage and stun. Heavy mitigation and healing.")),
        Rule("Earthbound Heaven", "Promathia is casting Earthbound Heaven.", Normal("Three moving pink circles in puddles. Look for the two lights initially close together; that side is safer.")),
        Rule("Malevolent Blessing", "Promathia is casting Malevolent Blessing.", Normal("Conals plus wing side cleave. Avoid conals and move away from glowing wing side.")),
        Rule("Infernal Deliverance", "Promathia is casting Infernal Deliverance.", Normal("Stack/tower on puddle, then immediate AoE. Soak near edge and run out immediately.")),
        Rule("Meteor", "Promathia is casting Meteor.", Normal("Marked AoEs on all players and tankbusters on tanks. Spread, tanks mitigate.")),
    };

    private static readonly CastRule[] ShinryuParadoxRules =
    {
        Rule("Cosmic Breath", "Shinryu Paradox is casting Cosmic Breath.", Normal("Hits top platform. Go to bottom platform.")),
        Rule("Cloak of Twilight", "Shinryu Paradox is casting Cloak of Twilight.", Normal("You receive light or dark cloak. Avoid mechanics of the opposite color.")),
        Rule("Twilight Nebula", "Shinryu Paradox is casting Twilight Nebula.", Normal("One platform light, one platform dark. Stand on the platform matching your cloak color.")),
        Rule("Starcrossed", "Shinryu Paradox is casting Starcrossed.", Normal("Intersecting line AoEs. Move out of the crossing lines.")),
        Rule("Cosmic Tail", "Shinryu Paradox is casting Cosmic Tail.", Normal("Hits bottom platform. Go to top platform.")),
        Rule("Cataclysmic Vortex", "Shinryu Paradox is casting Cataclysmic Vortex.", Normal("Obey your marker: pause = stop actions, play = keep moving, gaze = look away, question gaze = look at boss.")),
        Rule("Dark Nova", "Shinryu Paradox is casting Dark Nova.", Normal("AoE tankbuster. Tank mitigate; party stay away.")),
    };

    private static readonly CastRule[] HollowKingRules =
    {
        Rule("Towers", "Hollow King summons towers.", Normal("Soak front towers. Do not take two towers in succession if you have the debuff.")),
        Rule("Empty Proclamation", "Hollow King is casting Empty Proclamation.", Normal("Raidwide magic damage. Mitigate and heal.")),
        Rule("Left Swordscross", "Hollow King is casting Left Swordscross.", Normal("Attack hits right half and top-right to bottom-left diagonal. Go left/safe side.")),
        Rule("Right Swordscross", "Hollow King is casting Right Swordscross.", Normal("Attack hits left half and top-left to bottom-right diagonal. Go right/safe side.")),
        Rule("Twin Blaze", "Hollow King is casting Twin Blaze.", Normal("Two front orb AoEs. Move front and dodge toward the side with the donut AoE.")),
        Rule("Cataclysmic Blade", "Hollow King is casting Cataclysmic Blade.", Normal("Cataclysmic Vortex instructions plus triangle AoEs from boss. Obey marker and dodge triangles.")),
        Rule("Burst", "Hollow King is casting Burst.", Normal("Ring AoEs from both sides. Dodge between/through rings.")),
        Rule("Cosmic Flame", "Hollow King is casting Cosmic Flame.", Normal("East/west exaflares. Start beside a spawning AoE at edge, then move into it after it fires.")),
        Rule("Atomic Ray", "Hollow King is casting Atomic Ray.", Normal("Two line AoEs move together with timer. Be outside their line when timer snapshots.")),
        Rule("Super Nova", "Hollow King is casting Super Nova.", Normal("Multi-hit stack marker. Stack and mitigate.")),
    };

    private static readonly CastRule[] GuardianArkveldRules =
    {
        Rule("Roar", "Guardian Arkveld is using Roar.", Normal("Raidwide magic damage. Mitigate, heal, and use Mega Potion if needed.")),
        Rule("Chainblade Blow", "Guardian Arkveld is using Chainblade Blow.", Normal("Raised arm cleaves that side, then ground explodes. Go opposite the raised arm, then cross after it resolves.")),
        Rule("Wyvern's Siegeflight", "Guardian Arkveld is using Wyvern's Siegeflight.", Normal("Dash line across arena. Red cracks: middle safe after dash. White cracks: sides safe and watch rolling line AoE.")),
        Rule("White Flash", "Guardian Arkveld is using White Flash.", Normal("Healer light party stacks, then ground AoEs. Split into light parties and move after stack resolves.")),
        Rule("Dragonspark", "Guardian Arkveld is using Dragonspark.", Normal("Healer light party stacks. Split into light parties and mitigate.")),
        Rule("Rush", "Guardian Arkveld is using Rush.", Normal("Three charge lines create expanding rings. Move into a resolved charge spot, then dodge rings.")),
        Rule("Wyvern Ouroblade", "Guardian Arkveld is using Wyvern Ouroblade.", Normal("Single raised-arm half-room cleave. Spread on the safe side.")),
        Rule("Wild Energy", "Guardian Arkveld is using Wild Energy.", Normal("AoE markers on players with magic vuln. Spread out; getting clipped twice is dangerous.")),
        Rule("Chainblade Charge", "Guardian Arkveld is using Chainblade Charge.", Normal("Physical stack on a healer. Stack and mitigate.")),
        Rule("Towers", "Guardian Arkveld summons towers.", Normal("Bait three sets of ground AoEs first, then soak towers. Tanks take the large towers with mitigation.")),
        Rule("Wyvern's Vengeance", "Guardian Arkveld is using Wyvern's Vengeance.", Normal("Four ground AoEs move from boss outward. Dodge away from their paths.")),
        Rule("Forged Fury", "Guardian Arkveld is casting Forged Fury.", Normal("Massive magic damage and long Guardian Will debuff. Heavy mitigation and healing.")),
        Rule("Clamorous Chase", "Guardian Arkveld is using Clamorous Chase.", Normal("Numbered dash/cleave sequence. Stand cardinal edges in order; right arm = clockwise, left arm = counter-clockwise, then move in after your dash.")),
        Rule("Wyvern's Weal", "Guardian Arkveld is using Wyvern's Weal.", Normal("Rotating edge laser with tower/spike sequence. Start near tank spike side if marked, then dodge laser and exploding spikes.")),
        Rule("Wrathful Wrattle", "Guardian Arkveld is using Wrathful Wrattle.", Normal("Repeated line AoEs. Dodge lines, mitigate if clipped, and use Mega Potion if low.")),
        Rule("Steeltail Thrust", "Guardian Arkveld is using Steeltail Thrust.", Normal("Get in front of the boss to dodge the tail thrust.")),
        Rule("Wyvern's Radiance", "Guardian Arkveld is using Wyvern's Radiance.", Normal("Moving AoEs from arena edge. Dodge at wall if easier and avoid triggering nearby spike explosions.")),
    };

    private static readonly CastRule[] ValigarmandaExtremeRules =
    {
        Rule("Skyruin", "Valigarmanda EX is casting Skyruin.", Normal("Massive raidwide, DoT, and elemental phase shift. First flame, then ice/lightning in random order. Prepare for Triscourge debuffs.")),
        Rule("Triscourge", "Valigarmanda EX is casting Triscourge.", Normal("Role debuffs resolve by phase. Check your debuff: move/stack/spread/tankbuster and handle levitation rules in lightning.")),
        Rule("Strangling Coil", "Valigarmanda EX visual tell.", Normal("Donut AoE in middle. Move center-safe/handle partner stack if paired with Charring Cataclysm.")),
        Rule("Susurrant Breath", "Valigarmanda EX visual tell.", Normal("Wide conal from boss. Move to safe corner/side and handle partner stack if paired.")),
        Rule("Slithering Strike", "Valigarmanda EX visual tell.", Normal("Point-blank slightly larger than hitbox. Move out; partner stacks line up outside hitbox if paired.")),
        Rule("Spikesicle", "Valigarmanda EX is using Spikesicle.", Normal("Curved icicle lines sweep across arena and ice blocks explode. Start near first side, move with curves, then dodge block explosions.")),
        Rule("Volcanic Drop", "Valigarmanda EX volcano warning.", Normal("One volcano bubbles east/west. Move to the non-bubbling side and dodge player ground AoEs.")),
        Rule("Charring Cataclysm", "Valigarmanda EX is using Charring Cataclysm.", Normal("Two-player partner stacks. Stack with assigned partner in the correct safe spot for the current bird tell.")),
        Rule("Mountain Fire", "Valigarmanda EX is using Mountain Fire.", Normal("Tank tower sequence. Tanks soak with invuln/mitigation; party stands behind tower relative to boss position.")),
        Rule("Calamitous Cry", "Valigarmanda EX is casting Calamitous Cry.", Normal("Alternating tank/healer line stacks plus conal AoEs. Tanks front the line stack; light parties kill adds while dodging conals.")),
        Rule("Tulidisaster", "Valigarmanda EX is casting Tulidisaster.", Normal("Three heavy raidwide hits, stronger each time, then permanent fire DoT. Heavy mitigation and healing.")),
        Rule("Northern Cross", "Valigarmanda EX avalanche warning.", Normal("Huge line AoE from southeast direction. Move to safe half while also resolving Cataclysm safe spot.")),
        Rule("Chilling Cataclysm", "Valigarmanda EX is using Chilling Cataclysm.", Normal("Arcane Spheres line AoEs. Identify whether A or B waymark is safe; avoid the sphere pointing at the unsafe mark.")),
        Rule("Freezing Dust", "Valigarmanda EX is casting Freezing Dust.", Normal("Keep moving for about two seconds after cast resolves or you freeze.")),
        Rule("Ice Talon", "Valigarmanda EX is casting Ice Talon.", Normal("AoE magical tankbusters on both tanks with strong DoT. Tanks spread/mitigate.")),
        Rule("Hail of Feathers", "Valigarmanda EX is casting Hail of Feathers.", Normal("Six feathers create proximity AoEs. Start opposite first feather, rotate, kill first feather and stay there.")),
        Rule("Blighted Bolt", "Valigarmanda EX is casting Blighted Bolt.", Normal("Destroys feathers in AoE. Be low/not levitating unless your current debuff says otherwise.")),
        Rule("Crackling Cataclysm", "Valigarmanda EX is using Crackling Cataclysm.", Normal("Invisible ground AoE under everyone after initial hit. Dodge tell, then move out of where players stood.")),
        Rule("Thunderous Breath", "Valigarmanda EX is casting Thunderous Breath.", Normal("Mapwide conal requires levitation. Stand on levitating panel in a row without an Arcane Sphere.")),
        Rule("Ruinfall", "Valigarmanda EX is casting Ruinfall.", Normal("Two-tank center tower plus knockback. Tanks mitigate; party use knockback prevention or aim into safe back gap.")),
        Rule("Wrath Unfurled", "Valigarmanda EX is casting Wrath Unfurled.", Normal("Damage buff before final sequence. Finish cleanly before enrage Tulidisaster.")),
    };

    private static readonly CastRule[] ZoraalJaExtremeRules =
    {
        Rule("Actualize", "Zoraal Ja EX is casting Actualize.", Normal("Raidwide magic damage and sometimes arena return. Mitigate and heal.")),
        Rule("Multidirectional Divide", "Zoraal Ja EX is casting Multidirectional Divide.", Normal("X-shaped line AoEs erupt. Find a large gap, then prepare for Forward/Backward Half or tank tethers.")),
        Rule("Backward Half", "Zoraal Ja EX is casting Backward Half.", Normal("Only back quadrant on non-glowing sword side is safe. Go behind him and to safe side.")),
        Rule("Forward Half", "Zoraal Ja EX is casting Forward Half.", Normal("Only front quadrant on glowing sword side is safe. Go in front and to glowing sword side.")),
        Rule("Regicidal Rage", "Zoraal Ja EX is casting Regicidal Rage.", Normal("Two tank tethers. Tanks pick up, separate from party and each other, and mitigate.")),
        Rule("Dawn of an Age", "Zoraal Ja EX is casting Dawn of an Age.", Normal("Massive raidwide and arena change. Heavy mitigation and reorient to grid.")),
        Rule("Vollok", "Zoraal Ja EX is casting Vollok.", Normal("Swords spawn on outer platforms. Identify safe squares before Sync copies patterns to main platform.")),
        Rule("Sync", "Zoraal Ja EX is casting Sync.", Normal("Two outer platform sword patterns connect to main grid. Stand on a square safe from both copied patterns.")),
        Rule("Greater Gateway", "Zoraal Ja EX is casting Greater Gateway.", Normal("Connector lines alter sword paths: red expands to 3 rows/cols, green knocks back, blue normal.")),
        Rule("Blade Warp", "Zoraal Ja EX is casting Blade Warp.", Normal("Swords appear on surrounding platforms. Track which rows/columns will travel through Gateway lines.")),
        Rule("Forged Track", "Zoraal Ja EX is casting Forged Track.", Normal("Tethered swords travel through connector rows. Avoid blue rows, then solve red expanded/green knockback line.")),
        Rule("Chasm of Vollok", "Zoraal Ja EX is casting Chasm of Vollok.", Normal("Yellow markers drop sword AoEs on your square. Spread to assigned squares, then mitigate Actualize.")),
        Rule("Projection of Triumph", "Zoraal Ja EX is casting Projection of Triumph.", Normal("Donut/prey marker rows travel to sword icons. Dodge marker explosions while preparing for Half/Circuit.")),
        Rule("Projection of Turmoil", "Zoraal Ja EX is casting Projection of Turmoil.", Normal("Moving stack line consumes Projection debuffs one by one. Party moves north to south, resolving after vuln falls.")),
        Rule("Bitter Whirlwind", "Zoraal Ja EX is casting Bitter Whirlwind.", Normal("Three-hit magic tankbuster. Swap each hit or invuln all three.")),
        Rule("Drum of Vollok", "Zoraal Ja EX is casting Drum of Vollok.", Normal("Two-player stacks launch non-target based on position. Non-target role positions to be knocked to new platform.")),
        Rule("Aero III", "Zoraal Ja EX is casting Aero III.", Normal("Tornado moves players to opposite platform. Use it when your role needs to swap platforms.")),
        Rule("Duty's Edge", "Zoraal Ja EX is casting Duty's Edge.", Normal("Four-hit stack with increasing damage. Stack and mitigate heavily.")),
        Rule("Burning Chains", "Zoraal Ja EX is casting Burning Chains.", Normal("Supports chained to DPS. Run apart to break tether, then regroup for raidwide.")),
        Rule("Half Circuit", "Zoraal Ja EX is casting Half Circuit.", Normal("Half Full plus point-blank/donut blades. Dodge marker explosion, find safe side/slice, then dodge next explosion.")),
    };

    private static readonly CastRule[] QueenEternalExtremeRules =
    {
        Rule("Aethertithe", "Queen Eternal EX is casting Aethertithe.", Normal("Suck-in conal plus light party line stacks three times. Dodge distortion; tanks stand closest in each stack.")),
        Rule("Virtual Shift", "Queen Eternal EX is casting Virtual Shift.", Normal("Raidwide, stun, and platform change. Reorient fast for Wind/Earth/Ice phase rules.")),
        Rule("Legitimate Force", "Queen Eternal EX is casting Legitimate Force.", Normal("Hands glow in cleave order. Stand on side of second glowing hand, then dodge into first cleave.")),
        Rule("World Shatter", "Queen Eternal EX is casting World Shatter.", Normal("Raidwide and platform returns to normal. Mitigate and heal.")),
        Rule("Laws of Wind", "Queen Eternal EX is casting Laws of Wind.", Normal("Healer stacks drop lethal puddles, then push debuffs and tethers. Drop puddles NW/SE, stack middle, break tethers, dodge cleaves, resolve push.")),
        Rule("Prosecution of War", "Queen Eternal EX is casting Prosecution of War.", Normal("Two-hit tankbuster. Invuln or swap/provoke during cast so first tank does not die.")),
        Rule("Divide and Conquer", "Queen Eternal EX is casting Divide and Conquer.", Normal("Everyone gets line cleave, then previous cleave spots fire again. Fan around boss and dodge into gaps.")),
        Rule("Royal Domain", "Queen Eternal EX is casting Royal Domain.", Normal("Raidwide before Earth shift. Mitigate and prepare for floating/grounded platform mechanics.")),
        Rule("Laws of Earth", "Queen Eternal EX is casting Laws of Earth.", Normal("Use gravity wells: float to cross gaps, ground to soak towers. Handle tethers/AoEs, meteors, then hide behind meteors for Weighty Blow.")),
        Rule("Gravitational Empire", "Queen Eternal EX is casting Gravitational Empire.", Normal("Each light party gets tether/prey, circle AoE, and two tower soakers. Nothing players soak towers; marked players float out.")),
        Rule("Weighty Blow", "Queen Eternal EX is casting Weighty Blow.", Normal("Hide behind meteors. Rotate to next meteor after each pulse destroys one.")),
        Rule("Coronation", "Queen Eternal EX is casting Coronation.", Normal("Laser tethers plus huge personal circles. Take assigned wall/corner spots, do not cross tethers or overlap circles.")),
        Rule("Absolute Authority", "Queen Eternal EX is casting Absolute Authority.", Normal("Stack NW and move along north wall for four puddle baits. Resolve flares/stack together, then pair-stack red arrows and anti-knockback.")),
        Rule("Laws of Ice", "Queen Eternal EX is casting Laws of Ice.", Normal("Keep moving at cast end or freeze. Cross ice bridges one at a time, stretch icicle tethers, then dodge cleaves.")),
    };

    private static readonly CastRule[] ZeleniaExtremeRules =
    {
        Rule("Thorned Catharsis", "Zelenia EX is casting Thorned Catharsis.", Normal("Raidwide magic damage. Mitigate and heal.")),
        Rule("Shock", "Zelenia EX is casting Shock.", Normal("One role gets marked AoEs, other gets donut AoEs plus intercardinal towers. Donut role soaks towers; partners spread out.")),
        Rule("Specter of the Lost", "Zelenia EX is casting Specter of the Lost.", Normal("Non-tank tether tankbusters. Tanks pick up tethers and mitigate away from party.")),
        Rule("Escelons' Fall", "Zelenia EX is casting Escelons' Fall.", Normal("Close/far Witch Hunt baits in sequence. Assigned groups bait 1/3 and 2/4, swapping close/far as needed.")),
        Rule("Stock Break", "Zelenia EX is casting Stock Break.", Normal("Four-hit healer stack. Stack and mitigate.")),
        Rule("Blessed Barricade", "Zelenia EX is casting Blessed Barricade.", Normal("Adds phase. Supports west, DPS east. Kill Roseblood Drops before aether reaches 100.")),
        Rule("Power Break", "Zelenia EX is casting Power Break.", Normal("Shade/tether half-room cleaves. Tethered players point cleaves outside; non-tethered players soak towers.")),
        Rule("Perfumed Quietus", "Zelenia EX is casting Perfumed Quietus.", Normal("Ultimate raidwide after adds. Kill adds or this wipes; mitigate heavily.")),
        Rule("Roseblood Bloom", "Zelenia EX is casting Roseblood Bloom.", Normal("Rose tiles copy mechanics to adjacent tiles. Avoid middle bleed and solve tile programming for the current bloom.")),
        Rule("Alexandrian Thunder II", "Zelenia EX is casting Alexandrian Thunder II.", Normal("Rotating conal AoEs. Chase rotation from safe start and avoid rose tiles.")),
        Rule("Alexandrian Thunder III", "Zelenia EX is casting Alexandrian Thunder III.", Normal("AoEs on all players that hit Rose tiles. Spread without touching/triggering connected Rose tiles.")),
        Rule("Alexandrian Thunder IV", "Zelenia EX is casting Alexandrian Thunder IV.", Normal("In/out sequence from rings. Start on side that lets you dodge the second mechanic safely, then move tile to tile.")),
        Rule("Thunder Slash", "Zelenia EX is casting Thunder Slash.", Normal("Sequential conals plus Thunder IV. Dodge in/out while avoiding cones and Rose tiles.")),
        Rule("Bud of Valor 1", "Zelenia EX is casting Bud of Valor 1.", Normal("Shade creates intercardinal outer towers. Emblazon players program Rose tiles; others soak quadrant towers.")),
        Rule("Bud of Valor 2", "Zelenia EX is casting Bud of Valor 2.", Normal("Shade casts stacks/donut Shock or Power Break depending bloom. Follow role positions and resolve paired mechanics.")),
        Rule("Emblazon", "Zelenia EX is casting Emblazon.", Normal("Rosebud players create new Rose tiles. Do not stand on existing Rose tile when it explodes.")),
        Rule("Alexandrian Banish II", "Zelenia EX is casting Alexandrian Banish II.", Normal("Support and DPS stacks. Stack by role while handling Shock/Escelons positioning.")),
        Rule("Encircling Thorns", "Zelenia EX is casting Encircling Thorns.", Normal("Role tethers break by distance. Move supports/DPS to assigned sides while staying in stack coverage.")),
        Rule("Alexandrian Banish III", "Zelenia EX is casting Alexandrian Banish III.", Normal("Healer stack hits Rose tiles and connected tiles. Make sure everyone is included through safe Rose connections.")),
        Rule("Valorous Ascension", "Zelenia EX is casting Valorous Ascension.", Normal("Briar line AoEs plus raidwide hits. Move from first safe side into opposite safe path, then handle follow-up.")),
        Rule("Holy Hazard", "Zelenia EX is casting Holy Hazard.", Normal("Two sets of 120-degree cleaves. Place/use Rose tiles so unsafe tower is connected safely; dodge cleaves into correct tile.")),
    };

    private static readonly CastRule[] NecronExtremeRules =
    {
        Rule("Blue Shockwave", "Necron EX is casting Blue Shockwave.", Normal("Two-hit conal magic tankbuster with vuln. Tank swap or invuln; party stay middle/out of cone.")),
        Rule("Fear of Death", "Necron EX is casting Fear of Death.", Normal("Raidwide plus puddles/arm adds. Bait assigned arms outward: TMMT north, RHHR south. Stand just outside puddles.")),
        Rule("Cold Grip", "Necron EX is casting Cold Grip.", Normal("Side cleaves then glowing-arm side AoE. Stay near middle, then move away from glowing arm side.")),
        Rule("Memento Mori", "Necron EX is casting Memento Mori.", Normal("Middle line leaves dark puddle; hands fire lines. Tanks take dark side safe line, party uses light side role spots.")),
        Rule("Smite of Gloom", "Necron EX is casting Smite of Gloom.", Normal("Large AoEs on all players with magic vuln. Spread in assigned safe spots; do not overlap.")),
        Rule("Soul Reaping", "Necron EX is casting Soul Reaping.", Normal("Stores in/out/sides/middle safe pattern for later Blight. Remember the safe spot and diamond count.")),
        Rule("Twofold Blight", "Necron EX is casting Twofold Blight.", Normal("Stored Soul Reaping resolves plus healer line stacks. Dodge stored pattern and stack by light party.")),
        Rule("Fourfold Blight", "Necron EX is casting Fourfold Blight.", Normal("Stored Soul Reaping resolves plus role/partner line stacks. Dodge stored pattern and stack with partner.")),
        Rule("The End's Embrace", "Necron EX is casting The End's Embrace.", Normal("Small AoEs drop arm puddles. Drop near previous arm puddle, move out, and bait line away.")),
        Rule("Grand Cross", "Necron EX is casting Grand Cross.", Normal("Heavy raidwide and small circular arena. Stack middle, bait AoEs, fan to intercards, then handle lasers/towers.")),
        Rule("Shock Towers", "Necron EX Grand Cross towers.", Normal("One role has AoEs, other soaks towers. AoE player out, non-AoE player in; repeat opposite configuration.")),
        Rule("Neutron Star", "Necron EX is casting Neutron Star.", Normal("Heavy raidwide and return to normal arena. Stack and mitigate.")),
        Rule("Darkness of Eternity", "Necron EX is casting Darkness of Eternity.", Normal("Ultimate after adds. Mitigate/heal, then kill role-specific hands and escape abyss before Doom.")),
        Rule("Specter of Death", "Necron EX is casting Specter of Death.", Normal("Left/right specter line AoEs create one safe row. Avoid or you get sent back to abyss.")),
        Rule("Relentless Reaping", "Necron EX is casting Relentless Reaping.", Normal("Stores four unique Soul Reaping mechanics. Call numbered safe order: in, out, middle, or sides.")),
        Rule("Crop Rotation", "Necron EX is casting Crop Rotation.", Normal("Rotates stored Reaping diamonds. Identify which number starts, then resolve clockwise order.")),
        Rule("The Fourth Season", "Necron EX is casting The Fourth Season.", Normal("Resolve four stored Reapings, then Fourfold Blight partner stacks on last mechanic.")),
        Rule("The Second Season", "Necron EX is casting The Second Season.", Normal("Resolve four stored Reapings, then Twofold Blight light-party stacks on last mechanic.")),
        Rule("Circle of Lives", "Necron EX is casting Circle of Lives.", Normal("Five planets do donut AoEs in sequence. Move planet to planet; watch Specter line during second/third donuts.")),
        Rule("Mass Macabre", "Necron EX is casting Mass Macabre.", Normal("Portals require 4/3/2 players. Wait for vuln to fall before entering next portal; split by role/side.")),
    };

    private static readonly CastRule[] NecronAddRules =
    {
        Rule("Choking Grasp", "Icy Hands is casting Choking Grasp.", Normal("Ground line AoE or personal add hit. Dodge line in group phase; tanks/healers mitigate in abyss.")),
        Rule("Muted Struggle", "Beckoning Hands is casting Muted Struggle.", Normal("Line tankbuster on current target. Point away from party and mitigate.")),
        Rule("Spreading Fear", "Icy Hands is casting Spreading Fear.", Normal("Interrupt this if possible. DPS should kill the hand that is about to enrage.")),
        Rule("Necrotic Pulse", "Icy Hands is casting Necrotic Pulse.", Normal("Healer abyss bleed DoT. Heal through it and kill the hand fast.")),
        Rule("Chilling Grasp", "Icy Hands is casting Chilling Grasp.", Normal("Cleansable slow in healer abyss. Esuna/cleanse it if possible.")),
    };

    private static readonly CastRule[] DoomtrainExtremeRules =
    {
        Rule("Runaway Train", "Doomtrain EX is casting Runaway Train.", Normal("Intermission. Kill Aether before Doom-engine reaches 100 while handling ghost train stack/spread and tank conals.")),
        Rule("Aetherial Ray", "Doomtrain EX ghost train is using Aetherial Ray.", Normal("Tank conal from lower ghost train when it stops. Tanks bait away from party.")),
        Rule("Aetherosote", "Doomtrain EX ghost train is using Aetherosote.", Normal("Two smoke puffs: unmarked healer light-party stacks. Stack with healer groups.")),
        Rule("Aetherochar", "Doomtrain EX ghost train is using Aetherochar.", Normal("Three smoke puffs: unmarked AoEs on three players. Spread out.")),
        Rule("Derailment Siege", "Doomtrain EX is casting Derailment Siege.", Normal("Stack tower toward front. Soak/mitigate; many groups invuln later towers.")),
        Rule("Psychokinesis", "Doomtrain EX is casting Psychokinesis.", Normal("Boxes removed and players get large AoEs. Marked players spread to assigned sides; others use removed-box safe space.")),
        Rule("Third Rail", "Doomtrain EX is casting Third Rail.", Normal("Bait unmarked AoEs after box removal, then return front.")),
        Rule("Dead Man's Overdraught", "Doomtrain EX is casting Dead Man's Overdraught.", Normal("Use spread spots by light party/role. Respect train car rows and avoid overlaps.")),
        Rule("Arcane Revelation", "Doomtrain EX is casting Arcane Revelation.", Normal("Groups may cheese with heavy mit: tanks front-left, party front-right, mitigate as hits resolve.")),
        Rule("Lightning Burst", "Doomtrain EX is casting Lightning Burst.", Normal("Tankbusters. Tanks spread and mitigate; party stay away.")),
    };

    private static readonly DungeonCastRule[] DawntrailDungeonRules =
    {
        DungeonRule(new[] { "Prime Punutiy" }, "Punutiy Press", "Prime Punutiy is casting Punutiy Press.", Normal("Raidwide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Prime Punutiy" }, "Hydrowave", "Prime Punutiy is casting Hydrowave.", Normal("Conal AoE on a random player. Move out of the cone.")),
        DungeonRule(new[] { "Prime Punutiy" }, "Resurface", "Prime Punutiy is casting Resurface.", Normal("Boss sucks in from the edge, then spits objects into line/circle/cone AoEs. Ends with a donut.")),
        DungeonRule(new[] { "Prime Punutiy" }, "Song of the Punutiy", "Prime Punutiy is casting Song of the Punutiy.", Normal("Adds tether players for large AoEs. Tank gets boss conal; spread tethers, then kill adds.")),
        DungeonRule(new[] { "Prime Punutiy" }, "Shore Shaker", "Prime Punutiy is casting Shore Shaker.", Normal("Expanding ring AoEs from boss. Move between rings.")),
        DungeonRule(new[] { "Treant" }, "Arboreal Storm", "Treant is casting Arboreal Storm.", Normal("Ground AoE. Move out.")),
        DungeonRule(new[] { "Widowmaker" }, "Gnaw", "Widowmaker is casting Gnaw.", Normal("Heavy untelegraphed tank hit. Tank mitigate and healers be ready.")),
        DungeonRule(new[] { "Mimiclot" }, "Flagrant Spread", "Mimiclot is casting Flagrant Spread.", Normal("Marked circle AoE. Spread away from others.")),
        DungeonRule(new[] { "Drowsie" }, "Uppercut", "Drowsie is casting Uppercut.", Normal("Physical tank buster. Tank mitigate.")),
        DungeonRule(new[] { "Drowsie" }, "Sow", "Drowsie is casting Sow.", Normal("Seeds appear around the arena. Watch their arrows for the coming vine lines.")),
        DungeonRule(new[] { "Drowsie" }, "Drowsy Dance", "Drowsie is casting Drowsy Dance.", Normal("Seeds erupt into directional vine lines. Watch arrows and dodge the growing Wallop lines.")),
        DungeonRule(new[] { "Drowsie" }, "Wallop", "Drowsie is casting Wallop.", Normal("Large vine line AoE in the indicated direction. Move out of the line.")),
        DungeonRule(new[] { "Drowsie" }, "Sneeze", "Drowsie is casting Sneeze.", Normal("Wide conal AoE. Move to side or behind.")),
        DungeonRule(new[] { "Drowsie" }, "Spit", "Drowsie is casting Spit.", Normal("Mimiclot adds spawn and players get marked circle AoEs. Tank adds and spread.")),
        DungeonRule(new[] { "Apollyon" }, "Razor Zephyr", "Apollyon is casting Razor Zephyr.", Normal("Line AoE toward a random player. Move out of the line.")),
        DungeonRule(new[] { "Apollyon" }, "Blade", "Apollyon is casting Blade.", Normal("Physical tank buster. Later it becomes an AoE with wind DoT and leaves a donut; tank mitigate, party stay clear.")),
        DungeonRule(new[] { "Apollyon" }, "High Wind", "Apollyon is casting High Wind.", Normal("Raidwide magic damage. Boss may feast on adds to enhance abilities; mitigate and heal.")),
        DungeonRule(new[] { "Apollyon" }, "Swarming Locust", "Apollyon is casting Swarming Locust.", Normal("Boss jumps to arena edge multiple times. Remember edges for Blades of Famine lines.")),
        DungeonRule(new[] { "Apollyon" }, "Blades of Famine", "Apollyon is casting Blades of Famine.", Normal("Large line AoEs from previous jump edges. Dodge away from those lanes.")),
        DungeonRule(new[] { "Apollyon" }, "Levinsickle", "Apollyon is casting Levinsickle.", Normal("Lightning pools make conal AoEs twice. Dodge puddles and cone directions.")),
        DungeonRule(new[] { "Apollyon" }, "Thunder III", "Apollyon is casting Thunder III.", Normal("Marked AoEs on all players. Spread out.")),
        DungeonRule(new[] { "Apollyon" }, "Razor Storm", "Apollyon is casting Razor Storm.", Normal("Large frontal AoE from the arena edge. Move away from boss front.")),
        DungeonRule(new[] { "Apollyon" }, "Windwhistle", "Apollyon is casting Windwhistle.", Normal("Tornado travels and fires lines. Stay out of tornado and dodge line shots.")),

        DungeonRule(new[] { "Ryoqor Terteh" }, "Frosting Fracas", "Ryoqor Terteh is casting Frosting Fracas.", Normal("Party-wide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Ryoqor Terteh" }, "Fluffle Up", "Ryoqor Terteh is casting Fluffle Up.", Normal("Adds telegraph quarter/circle AoEs. Find the safe quadrant.")),
        DungeonRule(new[] { "Ryoqor Terteh" }, "Cold Feat", "Ryoqor Terteh is casting Cold Feat.", Normal("Frozen add casts are delayed. Move from second safe spot into first safe spot after first AoE resolves.")),
        DungeonRule(new[] { "Ryoqor Terteh" }, "Snowscoop", "Ryoqor Terteh is casting Snowscoop.", Normal("Line AoEs fire in sets. Start in second set, then dodge into first.")),
        DungeonRule(new[] { "Ryoqor Terteh" }, "Sparkling Sprinkling", "Ryoqor Terteh is casting Sparkling Sprinkling.", Normal("Marked AoEs on all players. Spread out.")),
        DungeonRule(new[] { "Kahderyor" }, "Wind Unbound", "Kahderyor is casting Wind Unbound.", Normal("Party-wide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Kahderyor" }, "Crystalline Crush", "Kahderyor is casting Crystalline Crush.", Normal("Stack in tower. Crystals left behind affect later shots.")),
        DungeonRule(new[] { "Kahderyor" }, "Wind Shot", "Kahderyor is casting Wind Shot.", Normal("Player AoEs plus donut where crystals are. Stack/stay out as needed and avoid clipping.")),
        DungeonRule(new[] { "Kahderyor" }, "Earthen Shot", "Kahderyor is casting Earthen Shot.", Normal("Player AoEs plus circles where crystals are. Spread out away from crystals.")),
        DungeonRule(new[] { "Kahderyor" }, "Crystalline Storm", "Kahderyor is casting Crystalline Storm.", Normal("Line AoEs leave crystals behind. Dodge lines and remember crystal positions.")),
        DungeonRule(new[] { "Kahderyor" }, "Seed Crystals", "Kahderyor is casting Seed Crystals.", Normal("Marked AoEs leave debris adds. Kill debris to clear the debuff.")),
        DungeonRule(new[] { "Kahderyor" }, "Sharpened Sights", "Kahderyor is casting Sharpened Sights.", Normal("Next two attacks gain gaze rings. Look away when ring reaches the boss.")),
        DungeonRule(new[] { "Kahderyor" }, "Stalagmite Circle", "Kahderyor is casting Stalagmite Circle.", Normal("Point-blank AoE. Move away from boss, and watch for gaze timing if active.")),
        DungeonRule(new[] { "Kahderyor" }, "Cyclonic Ring", "Kahderyor is casting Cyclonic Ring.", Normal("Donut AoE. Move inside the safe spot, and watch for gaze timing if active.")),
        DungeonRule(new[] { "Gurfurlur" }, "Heaving Haymaker", "Gurfurlur is casting Heaving Haymaker.", Normal("Party-wide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Gurfurlur" }, "Stonework", "Gurfurlur is casting Stonework.", Normal("Read tablets. Water = side knockback, Earth = square AoEs/spreads, Wind = center knockback/tornadoes.")),
        DungeonRule(new[] { "Gurfurlur" }, "Sledgehammer", "Gurfurlur is casting Sledgehammer.", Normal("Multi-hit line stack. Stack and mitigate.")),
        DungeonRule(new[] { "Gurfurlur" }, "Arcane Stomp", "Gurfurlur is casting Arcane Stomp.", Normal("Orbs drift to boss and give damage up. Intercept orbs before boss absorbs them.")),
        DungeonRule(new[] { "Gurfurlur" }, "Enduring Glory", "Gurfurlur is casting Enduring Glory.", Normal("Massive party-wide damage. Heavy mitigation and healing.")),

        DungeonRule(new[] { "Feather Ray" }, "Immersion", "Feather Ray is casting Immersion.", Normal("Party-wide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Feather Ray" }, "Troublesome Tail", "Feather Ray is casting Troublesome Tail.", Normal("You mimic the next boss ability. Aim your copied conal/donut safely.")),
        DungeonRule(new[] { "Feather Ray" }, "Worrisome Wave", "Feather Ray is casting Worrisome Wave.", Normal("Frontal conal AoE. Dodge out of front; if copied, aim your cone safely.")),
        DungeonRule(new[] { "Feather Ray" }, "Hydro Ring", "Feather Ray is casting Hydro Ring.", Normal("Donut AoE changes arena shape. Get inside/outside correctly.")),
        DungeonRule(new[] { "Feather Ray" }, "Blowing Bubbles", "Feather Ray is casting Blowing Bubbles.", Normal("Bubbles move out from center. Avoid bubbles to prevent vuln.")),
        DungeonRule(new[] { "Feather Ray" }, "Bubble Bomb", "Feather Ray is casting Bubble Bomb.", Normal("Bubbles spawn and explode. Move away from bubbles before they detonate.")),
        DungeonRule(new[] { "Feather Ray" }, "Rolling Current", "Feather Ray is casting Rolling Current.", Normal("Knocks bubbles around arena. Watch bubble movement before choosing safe spot.")),
        DungeonRule(new[] { "Feather Ray" }, "Trouble Bubbles", "Feather Ray is casting Trouble Bubbles.", Normal("Bubbles spawn from boss and players. Spread and avoid bubble paths.")),
        DungeonRule(new[] { "Firearms" }, "Dynamic Dominance", "Firearms is casting Dynamic Dominance.", Normal("Party-wide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Firearms" }, "Mirror Maneuver", "Firearms is casting Mirror Maneuver.", Normal("Mirrors reflect line AoEs; orbs explode if hit by lines. Stand away from reflected paths.")),
        DungeonRule(new[] { "Firearms" }, "Thunderlight Burst", "Firearms is casting Thunderlight Burst.", Normal("Boss shoots line to mirror. Dodge original and reflected line.")),
        DungeonRule(new[] { "Firearms" }, "Ancient Artillery", "Firearms is casting Ancient Artillery.", Normal("Expanding square AoEs in lines. Move to safe squares.")),
        DungeonRule(new[] { "Firearms" }, "Pummel", "Firearms is casting Pummel.", Normal("Physical tank buster. Tank mitigate.")),
        DungeonRule(new[] { "Firearms" }, "Emergent Artillery", "Firearms is casting Emergent Artillery.", Normal("Delayed marked AoEs. Spread after dodging artillery.")),
        DungeonRule(new[] { "Maulskull" }, "Stonecarver", "Maulskull is casting Stonecarver.", Normal("Fists light up twice. Dodge the marked halves in order.")),
        DungeonRule(new[] { "Maulskull" }, "Skullcrush", "Maulskull is casting Skullcrush.", Normal("Front knockback plus marked AoEs. Get knocked diagonally, then spread.")),
        DungeonRule(new[] { "Maulskull" }, "Maulwork", "Maulskull is casting Maulwork.", Normal("Large circle AoEs, then line AoEs toward indicators. Dodge circles, then move out of lines.")),
        DungeonRule(new[] { "Maulskull" }, "Deep Thunder", "Maulskull is casting Deep Thunder.", Normal("Multi-hit tower stack. Stack together and mitigate.")),
        DungeonRule(new[] { "Maulskull" }, "Ringing Blows", "Maulskull is casting Ringing Blows.", Normal("Front knockback followed by Stonecarver. Position for knockback, then dodge lit fists.")),
        DungeonRule(new[] { "Maulskull" }, "Wrought Fire", "Maulskull is casting Wrought Fire.", Normal("AoE magic tank buster. Tank mitigate; party stay away from tank.")),
        DungeonRule(new[] { "Maulskull" }, "Colossal Impact", "Maulskull is casting Colossal Impact.", Normal("Back-half knockback plus spread/stack sequence. Position for knockback, then resolve marker.")),
        DungeonRule(new[] { "Maulskull" }, "Ashlayer", "Maulskull is casting Ashlayer.", Normal("Party-wide magic damage. Mitigate and heal.")),

        DungeonRule(new[] { "Leptocyon" }, "Levinbite", "Leptocyon is casting Levinbite.", Normal("Heavy tank damage. Tank mitigate.")),
        DungeonRule(new[] { "Vanguard Commander R8" }, "Electrowave", "Vanguard Commander R8 is casting Electrowave.", Normal("Party-wide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Vanguard Commander R8" }, "Enhanced Mobility", "Vanguard Commander R8 is casting Enhanced Mobility.", Normal("Dash path plus half-arena wingblade cleave. Start opposite dash direction, then move into safe space.")),
        DungeonRule(new[] { "Vanguard Commander R8" }, "Dispatch", "Vanguard Commander R8 is casting Dispatch.", Normal("Reads command: firing lines, aerial circles, or electrope side lines plus player AoEs.")),
        DungeonRule(new[] { "Aerostat" }, "Incendiary Ring", "Aerostat is casting Incendiary Ring.", Normal("Donut AoE. Move inside or outside the ring safe spot.")),
        DungeonRule(new[] { "Protector" }, "Electrowave", "Protector is casting Electrowave.", Normal("Party-wide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Protector" }, "Search and Destroy", "Protector is casting Search and Destroy.", Normal("Turrets create many line/circle AoEs. Keep moving through safe gaps.")),
        DungeonRule(new[] { "Protector" }, "Fulminous Fence", "Protector is casting Fulminous Fence.", Normal("Walls spawn in the arena. Plan your route before Battery Circuit starts.")),
        DungeonRule(new[] { "Protector" }, "Battery Circuit", "Protector is casting Battery Circuit.", Normal("Boss spins conal while circles appear. Rotate with boss around walls.")),
        DungeonRule(new[] { "Protector" }, "Rapid Thunder", "Protector is casting Rapid Thunder.", Normal("Magic tank buster. Tank mitigate.")),
        DungeonRule(new[] { "Protector" }, "Motion Sensor", "Protector is casting Motion Sensor.", Normal("Acceleration Bomb. Stop moving when debuff expires while dodging line sequence.")),
        DungeonRule(new[] { "Protector" }, "Tracking Bolt", "Protector is casting Tracking Bolt.", Normal("Large marked AoEs on all players. Spread out.")),
        DungeonRule(new[] { "Zander the Snakeskinner" }, "Electrothermia", "Zander is casting Electrothermia.", Normal("Party-wide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Zander the Snakeskinner" }, "Soulbane Saber", "Zander is casting Soulbane Saber.", Normal("Line AoE then delayed half-arena explosion. Move away from marked half.")),
        DungeonRule(new[] { "Zander the Snakeskinner" }, "Saber Rush", "Zander is casting Saber Rush.", Normal("Physical tank buster. Tank mitigate.")),
        DungeonRule(new[] { "Zander the Snakeskinner" }, "Syntheslean", "Zander is casting Syntheslean.", Normal("Conal AoE on a random player. Move out of the cone.")),
        DungeonRule(new[] { "Zander the Snakeskinner" }, "Syntheslither", "Zander is casting Syntheslither.", Normal("Boss moves east/west with sword path. Stand opposite first cleave, then adjust.")),
        DungeonRule(new[] { "Zander the Snakeskinner" }, "Shade Shot", "Zander is casting Shade Shot.", Normal("Magic tank buster. Tank mitigate.")),
        DungeonRule(new[] { "Zander the Snakeskinner" }, "Slitherbane Rearguard", "Zander is casting Slitherbane Rearguard.", Normal("Soulbane Saber plus rear cleave. Avoid line and behind cleave.")),
        DungeonRule(new[] { "Zander the Snakeskinner" }, "Slitherbane Foreguard", "Zander is casting Slitherbane Foreguard.", Normal("Soulbane Saber plus front cleave. Avoid line and front cleave.")),
        DungeonRule(new[] { "Zander the Snakeskinner" }, "Screech", "Zander is casting Screech.", Normal("Party-wide magic damage. Mitigate and heal.")),

        DungeonRule(new[] { "Herpekaris" }, "Strident Shriek", "Herpekaris is casting Strident Shriek.", Normal("Party-wide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Herpekaris" }, "Vasoconstrictor", "Herpekaris is casting Vasoconstrictor.", Normal("Poison puddles spawn in front. Watch which side will be punched next.")),
        DungeonRule(new[] { "Herpekaris" }, "Venomspill", "Herpekaris is casting Venomspill.", Normal("Raised hand punches poison puddle and explodes that side. Move to opposite side.")),
        DungeonRule(new[] { "Herpekaris" }, "Writhing Riot", "Herpekaris is casting Writhing Riot.", Normal("Left/right/back cleaves in random order. Dodge each after it resolves.")),
        DungeonRule(new[] { "Herpekaris" }, "Collective Agony", "Herpekaris is casting Collective Agony.", Normal("Line stack marker. Stack in line to share damage.")),
        DungeonRule(new[] { "Herpekaris" }, "Convulsive Crush", "Herpekaris is casting Convulsive Crush.", Normal("Physical tank buster. Tank mitigate.")),
        DungeonRule(new[] { "Deceiver" }, "Electrowave", "Deceiver is casting Electrowave.", Normal("Party-wide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Deceiver" }, "Bionic Thrash", "Deceiver is casting Bionic Thrash.", Normal("Arms show conal directions. Stand outside arm cones.")),
        DungeonRule(new[] { "Deceiver" }, "Bionic Trash", "Deceiver is casting Bionic Thrash.", Normal("Arms show conal directions. Stand outside arm cones.")),
        DungeonRule(new[] { "Deceiver" }, "Initialize Androids", "Deceiver is casting Initialize Androids.", Normal("Some android line AoEs are fake. Dodge the real lines, then kill adds.")),
        DungeonRule(new[] { "Deceiver" }, "Initialize Turrets", "Deceiver is casting Initialize Turrets.", Normal("Side turret lines resolve in order; some are fake. Dodge real lines.")),
        DungeonRule(new[] { "Deceiver" }, "Surge", "Deceiver is casting Surge.", Normal("Line knockback from center plus player AoEs. Use real turret blockers to avoid being knocked out.")),
        DungeonRule(new[] { "Automatoise" }, "Hard Stomp", "Automatoise is casting Hard Stomp.", Normal("Very large AoE. Run out immediately.")),
        DungeonRule(new[] { "Ambrose" }, "Psychic Wave", "Ambrose is casting Psychic Wave.", Normal("Party-wide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Ambrose" }, "Overwhelming Charge", "Ambrose is casting Overwhelming Charge.", Normal("Boss-facing cleave. Move away from the direction Ambrose is facing.")),
        DungeonRule(new[] { "Ambrose" }, "Psychokinesis", "Ambrose is casting Psychokinesis.", Normal("Glowing cages fire line AoEs and spawn adds. Dodge lines and kill adds.")),
        DungeonRule(new[] { "Ambrose" }, "Extrasensory Field", "Ambrose is casting Extrasensory Field.", Normal("Quadrant knockbacks. Stand on edge so you are not knocked out.")),
        DungeonRule(new[] { "Ambrose" }, "Voltaic Slash", "Ambrose is casting Voltaic Slash.", Normal("Physical tank buster. Tank mitigate.")),
        DungeonRule(new[] { "Ambrose" }, "Psychokinetic Charge", "Ambrose is casting Psychokinetic Charge.", Normal("Knockback quadrants plus boss cleave. Position for knockback and avoid boss-facing cleave.")),

        DungeonRule(new[] { "Protocol B4" }, "Deadly Bite", "Protocol B4 is casting Deadly Bite.", Normal("Heavy physical tank hit. Tank mitigate.")),
        DungeonRule(new[] { "Antivirus X" }, "Immune Response", "Antivirus X is casting Immune Response.", Normal("Boss either front cones or side cleaves. Watch facing/animation and move to safe arc.")),
        DungeonRule(new[] { "Antivirus X" }, "Foreign Entity Removal", "Antivirus X is casting Foreign Entity Removal.", Normal("Adds spawn as plus or circle. Plus = cross AoE, circle = donut; resolve in spawn order.")),
        DungeonRule(new[] { "Antivirus X" }, "Quarantine", "Antivirus X is casting Quarantine.", Normal("Tank AoE buster plus party stack. Tank away, party stack together.")),
        DungeonRule(new[] { "B8" }, "Laserblade", "B8 is casting Laserblade.", Normal("Huge 270 degree AoE. Stun it if possible or move behind the enemy.")),
        DungeonRule(new[] { "B8" }, "Thunder Beam", "B8 is casting Thunder Beam.", Normal("Heavy magic hit on tank. Tank mitigate.")),
        DungeonRule(new[] { "Amalgam" }, "Electrowave", "Amalgam is casting Electrowave.", Normal("Party-wide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Amalgam" }, "Disassembly", "Amalgam is casting Disassembly.", Normal("Boss splits and lightning lines cleave diagonal halves. Dodge first lines, later spread for markers.")),
        DungeonRule(new[] { "Amalgam" }, "Centralized Current", "Amalgam is casting Centralized Current.", Normal("Line AoE through boss hitbox. Move to sides.")),
        DungeonRule(new[] { "Amalgam" }, "Split Current", "Amalgam is casting Split Current.", Normal("Side cleaves. Move through middle/front-back safe space.")),
        DungeonRule(new[] { "Amalgam" }, "Amalgamight", "Amalgam is casting Amalgamight.", Normal("Physical tank buster. Tank mitigate.")),
        DungeonRule(new[] { "Amalgam" }, "Superbolt", "Amalgam is casting Superbolt.", Normal("Stack marker. Stack together to share damage.")),
        DungeonRule(new[] { "Amalgam" }, "Ternary Charge", "Amalgam is casting Ternary Charge.", Normal("Expanding ring AoEs from boss. Move between rings.")),
        DungeonRule(new[] { "Eliminator" }, "Disruption", "Eliminator is casting Disruption.", Normal("Party-wide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Eliminator" }, "Partition", "Eliminator is casting Partition.", Normal("Sword-side half-arena cleave. Move to side without sword.")),
        DungeonRule(new[] { "Eliminator" }, "Reconfigured Partition", "Eliminator is casting Reconfigured Partition.", Normal("Sword swaps side, then half-arena cleave. Watch new sword side.")),
        DungeonRule(new[] { "Eliminator" }, "Subroutine", "Eliminator is casting Subroutine.", Normal("Hand targets line or donut on a player. Read hand shape and move.")),
        DungeonRule(new[] { "Eliminator" }, "Overexposure", "Eliminator is casting Overexposure.", Normal("Line stack marker. Stack in line to share damage.")),
        DungeonRule(new[] { "Eliminator" }, "Final Attack Sequence", "Eliminator is casting Final Attack Sequence.", Normal("Kill Lightning Generator adds before Charge reaches 100.")),
        DungeonRule(new[] { "Eliminator" }, "Holo Ark", "Eliminator is casting Holo Ark.", Normal("Ultimate after add phase. If Charge reached 100 this wipes; otherwise mitigate and heal.")),
        DungeonRule(new[] { "Eliminator" }, "Elimination", "Eliminator is casting Elimination.", Normal("Heavy damage plus arena lines that explode. Move off lines.")),

        DungeonRule(new[] { "His Royal Headness Leonogg I", "Leonogg" }, "Malicious Mist", "Leonogg is casting Malicious Mist.", Normal("Party-wide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "His Royal Headness Leonogg I", "Leonogg" }, "Falling Nightmare", "Leonogg is casting Falling Nightmare.", Normal("Player-head AoEs drop after delay. Move out or you will be action-locked.")),
        DungeonRule(new[] { "His Royal Headness Leonogg I", "Leonogg" }, "Team Spirit", "Leonogg is casting Team Spirit.", Normal("Puppets spawn. Avoid touching puppets or you get bound.")),
        DungeonRule(new[] { "His Royal Headness Leonogg I", "Leonogg" }, "Spirited Charge", "Leonogg is casting Spirited Charge.", Normal("Puppets march across arena in halves. Avoid their paths.")),
        DungeonRule(new[] { "His Royal Headness Leonogg I", "Leonogg" }, "Evil Scheme", "Leonogg is casting Evil Scheme.", Normal("Circle AoEs fan out from center. Dodge while avoiding puppets.")),
        DungeonRule(new[] { "His Royal Headness Leonogg I", "Leonogg" }, "Looming Nightmare", "Leonogg is casting Looming Nightmare.", Normal("Chasing AoEs on two players. Kite away from party and puppets.")),
        DungeonRule(new[] { "His Royal Headness Leonogg I", "Leonogg" }, "Scream", "Leonogg is casting Scream.", Normal("Three opposing conal sets. Stand in third, then dodge into first.")),
        DungeonRule(new[] { "Jack-in-the-Pot" }, "Troubling Teacups", "Jack-in-the-Pot is casting Troubling Teacups.", Normal("Teacups spawn. Tethered cups explode large; track them during Tea Awhirl.")),
        DungeonRule(new[] { "Jack-in-the-Pot" }, "Tea Awhirl", "Jack-in-the-Pot is casting Tea Awhirl.", Normal("Follow tethered teacup as it spins, then move away when it stops.")),
        DungeonRule(new[] { "Jack-in-the-Pot" }, "Toiling Teapots", "Jack-in-the-Pot is casting Toiling Teapots.", Normal("Teacups fill in order. Start near third set, then move into first puddles after they disappear.")),
        DungeonRule(new[] { "Jack-in-the-Pot" }, "Last Drop", "Jack-in-the-Pot is casting Last Drop.", Normal("Magic tank buster. Tank mitigate.")),
        DungeonRule(new[] { "Stray Doll" }, "Slapstick", "Stray Doll is casting Slapstick.", Normal("Pulsing party-wide magic damage. Mitigate and AoE heal.")),
        DungeonRule(new[] { "Stray Doll" }, "Terrifying Glance", "Stray Doll is casting Terrifying Glance.", Normal("Gaze attack. Look away.")),
        DungeonRule(new[] { "Traumerei" }, "Bitter Regret", "Traumerei is casting Bitter Regret.", Normal("Appendage glow tells line: big middle glow = middle line, side glows = side lines.")),
        DungeonRule(new[] { "Traumerei" }, "Poltergeist", "Traumerei is casting Poltergeist.", Normal("Plus AoE places wall. Spirits can cross, living cannot.")),
        DungeonRule(new[] { "Traumerei" }, "Memorial March", "Traumerei is casting Memorial March.", Normal("Ghosts tether or line AoE. Stretch tether away using correct living/spirit state.")),
        DungeonRule(new[] { "Traumerei" }, "Ghostduster", "Traumerei is casting Ghostduster.", Normal("Kills spirits. Be living before it resolves.")),
        DungeonRule(new[] { "Traumerei" }, "Fleshbuster", "Traumerei is casting Fleshbuster.", Normal("Kills living. Be in Ghostly Guise before it resolves.")),
        DungeonRule(new[] { "Traumerei" }, "Malicious Mist", "Traumerei is casting Malicious Mist.", Normal("Party-wide magic damage with bleed DoT. Mitigate and heal.")),
        DungeonRule(new[] { "Traumerei" }, "Ghostcrusher", "Traumerei is casting Ghostcrusher.", Normal("Line stack magic damage. Stack to share.")),

        DungeonRule(new[] { "Valley Campeador" }, "100,000 Needles", "Valley Campeador is casting 100,000 Needles.", Normal("Huge fixed damage. Stun if possible or move away.")),
        DungeonRule(new[] { "Barreltender" }, "Barbed Bellow", "Barreltender is casting Barbed Bellow.", Normal("Party-wide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Barreltender" }, "Heavyweight Needles", "Barreltender is casting Heavyweight Needles.", Normal("Conal AoEs expand. Stand in the larger gaps.")),
        DungeonRule(new[] { "Barreltender" }, "Tender Drop", "Barreltender is casting Tender Drop.", Normal("Cactuses with flowers become large AoEs, without flowers small AoEs.")),
        DungeonRule(new[] { "Barreltender" }, "Barrel Breaker", "Barreltender is casting Barrel Breaker.", Normal("Knockback from center. Position away from danger.")),
        DungeonRule(new[] { "Barreltender" }, "Succulent Stomp", "Barreltender is casting Succulent Stomp.", Normal("Stack marker. Stack to share.")),
        DungeonRule(new[] { "Barreltender" }, "Prickly Left", "Barreltender is casting Prickly Left.", Normal("Left cleave expands large. Direct right is safe.")),
        DungeonRule(new[] { "Barreltender" }, "Pulp Smash", "Barreltender is casting Pulp Smash.", Normal("Physical tank buster. Tank mitigate.")),
        DungeonRule(new[] { "Anthracite" }, "Anthrabomb", "Anthracite is casting Anthrabomb.", Normal("Grey bombs explode circles; yellow bombs enter holes and fire pipe line AoEs.")),
        DungeonRule(new[] { "Anthracite" }, "Carbonaceous Combustion", "Anthracite is casting Carbonaceous Combustion.", Normal("Party-wide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Anthracite" }, "Carniflagration", "Anthracite is casting Carniflagration.", Normal("Find grey bomb safe gaps first, then dodge pipe line from yellow bomb row. Spread after third set.")),
        DungeonRule(new[] { "Anthracite" }, "Burning Coals", "Anthracite is casting Burning Coals.", Normal("Stack marker. Stack to share.")),
        DungeonRule(new[] { "Anthracite" }, "Chimney Smack", "Anthracite is casting Chimney Smack.", Normal("Magic tank buster. Tank mitigate.")),
        DungeonRule(new[] { "Yok Huy Attestant" }, "Ancient Wrath", "Yok Huy Attestant is casting Ancient Wrath.", Normal("Line AoEs from tethered statues. Hide behind rock formations.")),
        DungeonRule(new[] { "Yok Huy Attestant" }, "Boulder Toss", "Yok Huy Attestant is casting Boulder Toss.", Normal("Moderate tank damage. Tank mitigate.")),
        DungeonRule(new[] { "Yok Huy Attestant" }, "Sun Toss", "Yok Huy Attestant is casting Sun Toss.", Normal("Targeted ground AoE. Move out.")),
        DungeonRule(new[] { "Greatest Serpent of Tural" }, "Dubious Tulidisaster", "Greatest Serpent is casting Dubious Tulidisaster.", Normal("Party-wide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Greatest Serpent of Tural" }, "Bouncy Council", "Greatest Serpent is casting Bouncy Council.", Normal("Adds indicate line or spinning circle AoEs. Read icons and dodge line/circle patterns.")),
        DungeonRule(new[] { "Greatest Serpent of Tural" }, "Screes of Fury", "Greatest Serpent is casting Screes of Fury.", Normal("Magic tank buster. Tank mitigate.")),
        DungeonRule(new[] { "Greatest Serpent of Tural" }, "Greatest Labyrinth", "Greatest Serpent is casting Greatest Labyrinth.", Normal("Follow arrow maze to blue cleanse circle before curse kills you.")),
        DungeonRule(new[] { "Greatest Serpent of Tural" }, "Moist Summoning", "Greatest Serpent is casting Moist Summoning.", Normal("Three stack jumps leave puddles. Stack and move out of puddles.")),
        DungeonRule(new[] { "Greatest Serpent of Tural" }, "Greatest Flood", "Greatest Serpent is casting Greatest Flood.", Normal("Corner knockback. Use knockback immunity or avoid being knocked into puddles.")),
        DungeonRule(new[] { "Greatest Serpent of Tural" }, "Great Torrent", "Greatest Serpent is casting Great Torrent.", Normal("Rotating ground AoEs. Dodge into first hit, then spread for markers.")),

        DungeonRule(new[] { "Lindblum Zaghnal" }, "Electrical Overload", "Lindblum Zaghnal is casting Electrical Overload.", Normal("Party-wide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Lindblum Zaghnal" }, "Caber Toss", "Lindblum Zaghnal is casting Caber Toss.", Normal("Charged pillar fires line AoE sets. Dodge into each safe spot as lines resolve.")),
        DungeonRule(new[] { "Lindblum Zaghnal" }, "Gore", "Lindblum Zaghnal is casting Gore.", Normal("Boss charges pillar or summons Electrope adds. Watch pillar lines or kill/add-dodge.")),
        DungeonRule(new[] { "Lindblum Zaghnal" }, "Lightning Storm", "Lindblum Zaghnal is casting Lightning Storm.", Normal("Marked AoEs on all players. Spread out.")),
        DungeonRule(new[] { "Raw Electrope" }, "Electrify", "Raw Electrope is casting Electrify.", Normal("Adds are charging. Kill adds and avoid follow-up raidwide pulses/AoEs.")),
        DungeonRule(new[] { "Lindblum Zaghnal" }, "Sparking Fissure", "Lindblum Zaghnal is casting Sparking Fissure.", Normal("Ground AoEs spawn during add phase. Move out while handling Electrify adds.")),
        DungeonRule(new[] { "Overseer Kanilokka" }, "Dark Souls", "Overseer Kanilokka is casting Dark Souls.", Normal("Magic tank buster. Tank mitigate.")),
        DungeonRule(new[] { "Overseer Kanilokka" }, "Free Spirits", "Overseer Kanilokka is casting Free Spirits.", Normal("Curved line AoEs appear in quick succession. Keep moving through gaps.")),
        DungeonRule(new[] { "Overseer Kanilokka" }, "Phantom Flood", "Overseer Kanilokka is casting Phantom Flood.", Normal("Donut AoE. Get into middle safe circle.")),
        DungeonRule(new[] { "Overseer Kanilokka" }, "Dark II", "Overseer Kanilokka is casting Dark II.", Normal("Two sets of conal AoEs in succession. Dodge first, then adjust for second.")),
        DungeonRule(new[] { "Overseer Kanilokka" }, "Telltale Tears", "Overseer Kanilokka is casting Telltale Tears.", Normal("Marked AoEs on sets of players. Spread out.")),
        DungeonRule(new[] { "Overseer Kanilokka" }, "Lost Hope", "Overseer Kanilokka is casting Lost Hope.", Normal("Temporary Misdirection plus center proximity. Navigate to edge using reversed/confused movement.")),
        DungeonRule(new[] { "Lunipyati" }, "Raging Claw", "Lunipyati is casting Raging Claw.", Normal("Boss turns and cleaves front. Move behind or to side.")),
        DungeonRule(new[] { "Lunipyati" }, "Leporine Loaf", "Lunipyati is casting Leporine Loaf.", Normal("Moving AoEs follow arrow directions, then rotating circles move outward. Track arrows.")),
        DungeonRule(new[] { "Lunipyati" }, "Crater Carve", "Lunipyati is casting Crater Carve.", Normal("Middle is destroyed. Move to outside ring.")),
        DungeonRule(new[] { "Lunipyati" }, "Beastly Roar", "Lunipyati is casting Beastly Roar.", Normal("Proximity from boss plus rotating circle AoEs around ring. Move far and dodge rotation.")),
        DungeonRule(new[] { "Lunipyati" }, "Jagged Edge", "Lunipyati is casting Jagged Edge.", Normal("Marked circle AoEs. Spread out.")),
        DungeonRule(new[] { "Lunipyati" }, "Turali Stone IV", "Lunipyati is casting Turali Stone IV.", Normal("Magic stack marker. Stack to share.")),
        DungeonRule(new[] { "Lunipyati" }, "Sonic Howl", "Lunipyati is casting Sonic Howl.", Normal("Raidwide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Lunipyati" }, "Slabber", "Lunipyati is casting Slabber.", Normal("Magic tank buster. Tank mitigate.")),

        DungeonRule(new[] { "Gargant" }, "Chilling Chirp", "Gargant is casting Chilling Chirp.", Normal("Raidwide magic damage. Later casts add ground AoEs and marked player AoEs; mitigate and spread.")),
        DungeonRule(new[] { "Gargant" }, "Almighty Racket", "Gargant is casting Almighty Racket.", Normal("Boss faces a direction and front cleaves. Move away from facing.")),
        DungeonRule(new[] { "Gargant" }, "Aerial Ambush", "Gargant is casting Aerial Ambush.", Normal("Boss hides in sand from a pole then charges line AoE. Track destination pole.")),
        DungeonRule(new[] { "Gargant" }, "Earthsong", "Gargant is casting Earthsong.", Normal("Earth orbs explode in sets. Move between orb AoEs.")),
        DungeonRule(new[] { "Gargant" }, "Trap Jaws", "Gargant is casting Trap Jaws.", Normal("Physical tank buster. Tank mitigate.")),
        DungeonRule(new[] { "Bygone Subrunner" }, "High-speed Rotation", "Bygone Subrunner is casting High-speed Rotation.", Normal("Donut AoE. Move inside or outside as the marker shows.")),
        DungeonRule(new[] { "Soldier S0" }, "Field of Scorn", "Soldier S0 is casting Field of Scorn.", Normal("Raidwide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Soldier S0" }, "Thunderous Slash", "Soldier S0 is casting Thunderous Slash.", Normal("Tank buster. Tank mitigate.")),
        DungeonRule(new[] { "Soldier S0" }, "Sector Bisector", "Soldier S0 is casting Sector Bisector.", Normal("Clones vanish in order; final clone cleaves glowing sword side. Watch last clone.")),
        DungeonRule(new[] { "Soldier S0" }, "Ordered Fire", "Soldier S0 is casting Ordered Fire.", Normal("Adds fire line AoEs across arena. Dodge line lanes.")),
        DungeonRule(new[] { "Soldier S0" }, "Static Force", "Soldier S0 is casting Static Force.", Normal("Conal AoEs follow players. Keep cones away from others.")),
        DungeonRule(new[] { "Soldier S0" }, "Electric Excess", "Soldier S0 is casting Electric Excess.", Normal("Marked AoEs on all players. Spread out.")),
        DungeonRule(new[] { "Bygone Roadripper" }, "Road Rage", "Bygone Roadripper is casting Road Rage.", Normal("Dash and frontal AoE after turning. Stun if possible or move away from front.")),
        DungeonRule(new[] { "Bygone Roadripper" }, "Roadkill", "Bygone Roadripper is casting Roadkill.", Normal("Large circle AoE. Move out.")),
        DungeonRule(new[] { "Valia Pira" }, "Entropic Sphere", "Valia Pira is casting Entropic Sphere.", Normal("Raidwide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Valia Pira" }, "Coordinate March", "Valia Pira is casting Coordinate March.", Normal("Purple spheres follow shown paths; if they hit pink orbs, cross AoE happens. Avoid paths/crosses.")),
        DungeonRule(new[] { "Valia Pira" }, "Turrets", "Valia Pira is casting Turrets.", Normal("Turrets shoot line AoEs after spawning. Watch turret facing and dodge lines.")),
        DungeonRule(new[] { "Valia Pira" }, "Electric Field", "Valia Pira is casting Electric Field.", Normal("Expanding conal AoEs on ground/players. Spread into large safe spaces.")),
        DungeonRule(new[] { "Valia Pira" }, "Neutralize Front Lines", "Valia Pira is casting Neutralize Front Lines.", Normal("Frontal AoE plus player marked AoEs. Move behind/side and spread.")),
        DungeonRule(new[] { "Valia Pira" }, "Bloodmarch", "Valia Pira is casting Bloodmarch.", Normal("Pink orb spawns on shown tile. Watch tile preview and avoid later cross interactions.")),
        DungeonRule(new[] { "Valia Pira" }, "Deterrent Pulse", "Valia Pira is casting Deterrent Pulse.", Normal("Healer line stack marker. Stack to share.")),

        DungeonRule(new[] { "Preserved Medic" }, "Alexandrian Gravity", "Preserved Medic is casting Alexandrian Gravity.", Normal("AoE deals 50% of max HP. Avoid overlaps and do not get clipped.")),
        DungeonRule(new[] { "Chirurgeon General" }, "Medicine Field", "Chirurgeon General is casting Medicine Field.", Normal("Raidwide magic damage and bleed wall. Mitigate and avoid edge.")),
        DungeonRule(new[] { "Chirurgeon General" }, "Pungent Aerosol", "Chirurgeon General is casting Pungent Aerosol.", Normal("Knockback from marker. With distorted countdowns, first marker that appeared resolves first.")),
        DungeonRule(new[] { "Chirurgeon General" }, "Sterile Sphere", "Chirurgeon General is casting Sterile Sphere.", Normal("Two large and two small circle AoEs. Remember safe spots when countdowns are distorted.")),
        DungeonRule(new[] { "Chirurgeon General" }, "Biochemical Front", "Chirurgeon General is casting Biochemical Front.", Normal("Frontal half-arena AoE. Move behind boss.")),
        DungeonRule(new[] { "Chirurgeon General" }, "Sensory Deprivation", "Chirurgeon General is casting Sensory Deprivation.", Normal("Countdown markers are distorted. Resolve by remembering spawn/order.")),
        DungeonRule(new[] { "Chirurgeon General" }, "Concentrated Dose", "Chirurgeon General is casting Concentrated Dose.", Normal("Magic tank buster with potent poison DoT. Tank mitigate; heal poison damage.")),
        DungeonRule(new[] { "Preserved Prisoner" }, "Wild Charge", "Preserved Prisoner is casting Wild Charge.", Normal("Point-blank AoE. Move out.")),
        DungeonRule(new[] { "Headsman" }, "Lawless Pursuit", "Headsman is casting Lawless Pursuit.", Normal("Tethers each player into a cell. Prepare to fight your assigned Headsman.")),
        DungeonRule(new[] { "Headsman" }, "Head-splitting Roar", "Headsman is casting Head-splitting Roar.", Normal("Raidwide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Headsman" }, "Shackles of Fate", "Headsman is casting Shackles of Fate.", Normal("Chains bind players in cells until their Headsman dies. Kill your add.")),
        DungeonRule(new[] { "Headsman" }, "Dismemberment", "Headsman is casting Dismemberment.", Normal("Blades around cell point line AoEs. Stand between blade directions.")),
        DungeonRule(new[] { "Headsman" }, "Peal of Judgment", "Headsman is casting Peal of Judgment.", Normal("Lightning walls move across cell. Move with the safe gap.")),
        DungeonRule(new[] { "Headsman" }, "Execution Wheel", "Headsman is casting Execution Wheel.", Normal("Donut AoE. Move inside.")),
        DungeonRule(new[] { "Headsman" }, "Chopping Block", "Headsman is casting Chopping Block.", Normal("Point-blank AoE. Move out.")),
        DungeonRule(new[] { "Headsman" }, "Flaying Flail", "Headsman is casting Flaying Flail.", Normal("Three spiked balls drop AoEs under them. Move away from the impact spots.")),
        DungeonRule(new[] { "Headsman" }, "Will Breaker", "Headsman is casting Will Breaker.", Normal("Interrupt this cast.")),
        DungeonRule(new[] { "Headsman" }, "Death Penalty", "Headsman is casting Death Penalty.", Normal("Cleansable Doom. Esuna/cleanse fast.")),
        DungeonRule(new[] { "Headsman" }, "Relentless Torment", "Headsman is casting Relentless Torment.", Normal("Three-hit physical tank buster. Tank mitigate; healer be ready.")),
        DungeonRule(new[] { "Headsman" }, "Serial Torture", "Headsman is casting Serial Torture.", Normal("Rapid Dismemberment, Flaying Flail, and AoE combo. Keep moving through safe spaces.")),
        DungeonRule(new[] { "Immortal Remains" }, "Recollection", "Immortal Remains is casting Recollection.", Normal("Raidwide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Immortal Remains" }, "Memento", "Immortal Remains is casting Memento.", Normal("Arena memory changes. Watch memory spawn type for line, big/small AoE, or running line pattern.")),
        DungeonRule(new[] { "Immortal Remains" }, "Memory of the Storm", "Immortal Remains is casting Memory of the Storm.", Normal("Line stack. Stack together.")),
        DungeonRule(new[] { "Immortal Remains" }, "Impression", "Immortal Remains is casting Impression.", Normal("Knockback from center. Position so you do not fall.")),
        DungeonRule(new[] { "Immortal Remains" }, "Turmoil", "Immortal Remains is casting Turmoil.", Normal("No castbar arm slam half-room cleave. Move away from raised arm side.")),
        DungeonRule(new[] { "Immortal Remains" }, "Memory of the Pyre", "Immortal Remains is casting Memory of the Pyre.", Normal("Magic tank buster. Tank mitigate.")),

        DungeonRule(new[] { "Treno Catoblepas" }, "Earthquake", "Treno Catoblepas is casting Earthquake.", Normal("Raidwide physical damage. Mitigate and heal.")),
        DungeonRule(new[] { "Treno Catoblepas" }, "Bedeviling Light", "Treno Catoblepas is casting Bedeviling Light.", Normal("Line-of-sight behind rock pillar relative to boss.")),
        DungeonRule(new[] { "Treno Catoblepas" }, "Thunder II", "Treno Catoblepas is casting Thunder II.", Normal("Ground/player AoEs break rocks. Avoid breaking pillars needed for Bedeviling Light.")),
        DungeonRule(new[] { "Treno Catoblepas" }, "Thunder III", "Treno Catoblepas is casting Thunder III.", Normal("Magical tank buster. Tank mitigate.")),
        DungeonRule(new[] { "Treno Catoblepas" }, "Ray of Lightning", "Treno Catoblepas is casting Ray of Lightning.", Normal("Healer line stack. Stack to share.")),
        DungeonRule(new[] { "Treno Catoblepas" }, "Petribreath", "Treno Catoblepas is casting Petribreath.", Normal("Untargeted frontal conal. Move side/behind.")),
        DungeonRule(new[] { "Mistwake Rock" }, "Rockslide", "Mistwake Rock is casting Rockslide.", Normal("Medium conal AoE in a tight hallway. Move out and avoid clipping others.")),
        DungeonRule(new[] { "Amdusias" }, "Thunderclap Concerto", "Amdusias is casting Thunderclap Concerto.", Normal("Electric balls appear. Find area without balls.")),
        DungeonRule(new[] { "Amdusias" }, "Bio II", "Amdusias is casting Bio II.", Normal("Poison orbs explode if boss collides with them. Keep track for dashes.")),
        DungeonRule(new[] { "Amdusias" }, "Galloping Thunder", "Amdusias is casting Galloping Thunder.", Normal("Boss dashes several times. Avoid dash paths and poison orb explosions.")),
        DungeonRule(new[] { "Amdusias" }, "Thunder IV", "Amdusias is casting Thunder IV.", Normal("Raidwide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Amdusias" }, "Shockbolt", "Amdusias is casting Shockbolt.", Normal("Magic tank buster. Tank mitigate.")),
        DungeonRule(new[] { "Amdusias" }, "Thunder", "Amdusias is casting Thunder.", Normal("Marked AoEs on all players. Spread out.")),
        DungeonRule(new[] { "Amdusias" }, "Thunder III", "Amdusias is casting Thunder III.", Normal("Multi-hit healer stack marker. Stack to share.")),
        DungeonRule(new[] { "Mistwake Spirit" }, "Thunderstrike", "Mistwake Spirit is casting Thunderstrike.", Normal("Large point-blank AoE. Move out.")),
        DungeonRule(new[] { "Thundergust Griffin" }, "Thunderspark", "Thundergust Griffin is casting Thunderspark.", Normal("Raidwide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Thundergust Griffin" }, "High Volts", "Thundergust Griffin is casting High Volts.", Normal("Player AoEs plus ground AoEs that spawn lightning orbs. Spread and watch orb positions.")),
        DungeonRule(new[] { "Thundergust Griffin" }, "Thundering Roar", "Thundergust Griffin is casting Thundering Roar.", Normal("Lightning orbs explode into directional line AoEs. Stand outside indicated lines.")),
        DungeonRule(new[] { "Thundergust Griffin" }, "Fulgurous Fall", "Thundergust Griffin is casting Fulgurous Fall.", Normal("Boss dashes across arena and knocks back from middle line, then dashes perpendicular. Position for both.")),
        DungeonRule(new[] { "Thundergust Griffin" }, "Storm Surge", "Thundergust Griffin is casting Storm Surge.", Normal("Line AoE spawns rotating tornado outside arena. Avoid line and tornado path.")),

        DungeonRule(new[] { "Eye of the Scorpion" }, "Eyes on Me", "Eye of the Scorpion is casting Eyes on Me.", Normal("Raidwide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Eye of the Scorpion" }, "Petrifying Beam", "Eye of the Scorpion is casting Petrifying Beam.", Normal("Large conal AoE moves slightly before locking. Wait for lock, then sidestep.")),
        DungeonRule(new[] { "Eye of the Scorpion" }, "Motion Scanner", "Eye of the Scorpion is casting Motion Scanner.", Normal("Scanner gives Motion Tracker. Stop moving and sheathe weapons so you dont do auto attacks.")),
        DungeonRule(new[] { "Eye of the Scorpion" }, "Motion Detector", "Eye of the Scorpion is casting Motion Detector.", Normal("Scanner gives Motion Tracker. Stop moving and sheathe weapons so you dont do auto attacks.")),
        DungeonRule(new[] { "Eye of the Scorpion" }, "Penetrator Missile", "Eye of the Scorpion is casting Penetrator Missile.", Normal("Healer stack marker. Stack to share.")),
        DungeonRule(new[] { "Visitant Trapper" }, "Arachne Web", "Visitant Trapper is casting Arachne Web.", Normal("Large AoE in a small path. Spread carefully and avoid overlapping.")),
        DungeonRule(new[] { "Chort" }, "Mortifying Flesh", "Chort is casting Mortifying Flesh.", Normal("Boss faces, dashes, rolls around, then dashes across again when it stops. Track direction and stay clear.")),
        DungeonRule(new[] { "Chort" }, "Bodyweight Exorcism", "Chort is casting Bodyweight Exorcism.", Normal("Either center knockback or four soak towers. Position for knockback, or stack in towers.")),
        DungeonRule(new[] { "Chort" }, "Basic Vomit", "Chort is casting Basic Vomit.", Normal("Boss faces and casts a conal AoE. Move to side or behind.")),
        DungeonRule(new[] { "Chort" }, "Evil Emission", "Chort is casting Evil Emission.", Normal("Marked AoEs on all players. Spread out.")),
        DungeonRule(new[] { "Chort" }, "Profane Pressure", "Chort is casting Profane Pressure.", Normal("Healer stack marker. Stack to share.")),
        DungeonRule(new[] { "Chort" }, "Ripples of Gloom", "Chort is casting Ripples of Gloom.", Normal("Raidwide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Malphas" }, "Goekinesis", "Malphas is casting Goekinesis.", Normal("Raidwide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Malphas" }, "Puppet Pair", "Malphas is casting Puppet Pair.", Normal("Puppets are being set up. Small puppets make circle AoEs; large puppets make conals through middle.")),
        DungeonRule(new[] { "Malphas", "Scrap Gargoyle" }, "Cast-off Halo", "Malphas is casting Cast-off Halo.", Normal("Large puppet conal through middle. Look for large puppet positions and dodge the lanes.")),
        DungeonRule(new[] { "Malphas", "Scrap Byblos" }, "Metallic Miasma", "Malphas is casting Metallic Miasma.", Normal("Small puppet circle AoEs. Avoid the circle clump, then watch large puppet conals.")),
        DungeonRule(new[] { "Malphas" }, "Rubbish Disposal", "Malphas is casting Rubbish Disposal.", Normal("Raidwide magic damage. Mitigate and heal.")),
        DungeonRule(new[] { "Malphas" }, "Void Dark", "Malphas is casting Void Dark.", Normal("Boss faces a direction and casts a conal AoE. Move away from front.")),
        DungeonRule(new[] { "Malphas" }, "Scrap Meddle", "Malphas is casting Scrap Meddle.", Normal("Line AoEs summon puppets. Dodge lines and note puppet positions.")),
        DungeonRule(new[] { "Malphas" }, "Puppet Strings", "Malphas is casting Puppet Strings.", Normal("Small puppets do circles; large puppets around arena do conals through middle. Avoid circle clumps and conal lanes.")),
        DungeonRule(new[] { "Malphas" }, "Gluttonous Wire", "Malphas is casting Gluttonous Wire.", Normal("Healer stack marker. Stack to share.")),
        DungeonRule(new[] { "Malphas" }, "String Up", "Malphas is casting String Up.", Normal("Keep moving. Players standing still are turned into puppets.")),
        DungeonRule(new[] { "Malphas" }, "Shadow Play", "Malphas is casting Shadow Play.", Normal("AoE tank buster. Tank mitigate and keep away from party.")),
        DungeonRule(new[] { "Malphas" }, "Puppet Mastery", "Malphas is casting Puppet Mastery.", Normal("Puppet Strings with puppets spinning first. Track final puppet positions before dodging.")),
        DungeonRule(new[] { "Malphas" }, "Wrathful Wire", "Malphas is casting Wrathful Wire.", Normal("Marked AoEs on all players. Spread out.")),
    };

    public CastHelperWindow(Plugin plugin, IObjectTable objectTable, ITargetManager targetManager, IDataManager dataManager, ITextureProvider textureProvider, IPluginLog log)
        : base("Boss Helper  --  Drag title bar, then Lock###ChatEchoCastHelper")
    {
        this.plugin = plugin;
        this.objectTable = objectTable;
        this.targetManager = targetManager;
        this.dataManager = dataManager;
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
        if (cfg.CastHelperEnabled)
            RefreshCasts();
        else
        {
            heldCasts.Clear();
            activeMechanicAlerts.Clear();
        }

        Position = cfg.CastHelperPosition;
        PositionCondition = cfg.CastHelperLocked ? ImGuiCond.Always : ImGuiCond.FirstUseEver;
        BgAlpha = cfg.CastHelperLocked ? 0f : 0.7f;
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
              | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize;

        if (cfg.CastHelperLocked)
            Flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs;
        else
            Flags |= ImGuiWindowFlags.NoFocusOnAppearing;
    }

    public void ShowTestCast()
    {
        var ruleSets = new[]
        {
            EnuoRules, DoomtrainExtremeRules, DoomtrainRules, ZeleniaExtremeRules, ZeleniaRules,
            KirinRules, OmegaRules, UltimaRules, KamlanautRules, EaldnarcheRules,
            PrisheRules, FafnirRules, ShadowLordRules, QueenEternalExtremeRules, QueenEternalRules,
            ZoraalJaExtremeRules, ZoraalJaRules, NecronExtremeRules, NecronAddRules, NecronRules,
            ValigarmandaExtremeRules, ValigarmandaRules,
            ShantottoRules, AlexanderRules, PromathiaRules, ShinryuParadoxRules, HollowKingRules,
            GuardianArkveldRules,
            DawntrailDungeonRules.Select(rule => rule.Rule).ToArray(),
        };
        var rules = ruleSets[Random.Shared.Next(ruleSets.Length)];
        testRule = rules[Random.Shared.Next(rules.Length)];
        testCastVisible = true;
        testCastStartedAt = ImGui.GetTime();
    }

    public override bool DrawConditions()
    {
        var cfg = plugin.Configuration;
        return IsTestVisible() || (cfg.CastHelperEnabled && (!cfg.CastHelperLocked || activeCasts.Count > 0 || activeMechanicAlerts.Count > 0 || (cfg.CastHelperKeepLastUntilNextCast && heldCasts.Count > 0)));
    }

    public override void PreDraw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(plugin.Configuration.CastHelperBackgroundPadding));
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar();
    }

    public override void Draw()
    {
        var cfg = plugin.Configuration;
        if (!cfg.CastHelperLocked)
        {
            var pos = ImGui.GetWindowPos();
            if (pos != cfg.CastHelperPosition)
            {
                cfg.CastHelperPosition = pos;
                dragging = true;
            }

            if (dragging && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                cfg.Save();
                dragging = false;
            }
        }

        if (IsTestVisible())
        {
            DrawTestCast(cfg);
            return;
        }

        if (!cfg.CastHelperEnabled)
            return;

        var castsToDraw = new List<CastAlert>();
        castsToDraw.AddRange(activeCasts);
        castsToDraw.AddRange(activeMechanicAlerts.Select(alert => alert.Cast with { RemainingTime = Math.Max(0f, (float)(alert.ExpiresAt - ImGui.GetTime())) }));
        if (castsToDraw.Count == 0 && cfg.CastHelperKeepLastUntilNextCast)
            castsToDraw.AddRange(heldCasts);

        if (castsToDraw.Count == 0)
        {
            if (!cfg.CastHelperLocked)
                DrawStyledText("No matching casts", cfg.CastHelperDetailsColor, cfg.CastHelperDetailsEffect, cfg.CastHelperDetailsEffectColor, cfg.CastHelperDetailsFontSize);
            return;
        }

        activeCastBlocks.Clear();
        foreach (var cast in castsToDraw)
            activeCastBlocks.Add(new CastBlock(() => DrawCast(cast, cfg)));

        DrawCastBlocks(activeCastBlocks, cfg);
    }

    private bool IsTestVisible()
    {
        return testCastVisible && ImGui.GetTime() < testCastStartedAt + TestDurationSeconds + TestFadeSeconds;
    }

    private float TestAlpha()
    {
        var elapsed = ImGui.GetTime() - testCastStartedAt;
        if (elapsed <= TestDurationSeconds)
            return 1f;

        return Math.Clamp((float)(1.0 - ((elapsed - TestDurationSeconds) / TestFadeSeconds)), 0f, 1f);
    }

    private void RefreshCasts()
    {
        var now = ImGui.GetTime();
        if (now < nextRefreshTime)
            return;

        nextRefreshTime = now + RefreshIntervalSeconds;
        activeCasts.Clear();
        activeMechanicAlerts.RemoveAll(alert => alert.ExpiresAt <= now);

        AddCastIfMatched(targetManager.Target as IBattleChara);
        AddCastIfMatched(targetManager.FocusTarget as IBattleChara);

        foreach (var gameObject in objectTable)
        {
            if (gameObject is not IBattleChara battleChara)
                continue;

            if (gameObject.ObjectKind != ObjectKind.BattleNpc)
                continue;

            AddCastIfMatched(battleChara);
        }

        if (plugin.Configuration.CastHelperKeepLastUntilNextCast)
        {
            if (activeCasts.Count > 0)
            {
                heldCasts.Clear();
                heldCasts.AddRange(activeCasts.Select(c => c with { RemainingTime = 0f }));
                heldCastsUpdatedAt = now;
            }
            else if (heldCasts.Count > 0 && now - heldCastsUpdatedAt > HeldCastMaxAgeSeconds)
            {
                heldCasts.Clear();
            }
        }
        else
        {
            heldCasts.Clear();
        }
    }

    public void OnChatMessage(XivChatType type, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        foreach (var rule in ChatMechanicRules)
        {
            if (!rule.Needles.All(needle => Contains(message, needle)))
                continue;

            AddMechanicAlert(rule);
            return;
        }
    }

    private void AddMechanicAlert(ChatMechanicRule rule)
    {
        var now = ImGui.GetTime();
        activeMechanicAlerts.RemoveAll(alert => EqualsText(alert.Key, rule.Key) || alert.ExpiresAt <= now);
        activeMechanicAlerts.Add(new MechanicAlert(
            rule.Key,
            now + rule.DurationSeconds,
            new CastAlert(0, 0, rule.Source, rule.Title, rule.Rule.Details, rule.Rule.Advice, 0, (float)rule.DurationSeconds)));
    }

    private void AddCastIfMatched(IBattleChara? caster)
    {
        if (caster == null)
            return;

        ulong casterObjectId;
        uint actionId;
        string casterName;
        float remaining;
        try
        {
            if (!caster.IsCasting || caster.CastActionId == 0)
                return;

            casterObjectId = caster.GameObjectId;
            actionId = caster.CastActionId;
            casterName = caster.Name.TextValue;
            remaining = Math.Max(0f, caster.TotalCastTime - caster.CurrentCastTime);
        }
        catch (NullReferenceException)
        {
            return;
        }

        if (activeCasts.Exists(c => c.CasterObjectId == casterObjectId && c.ActionId == actionId))
            return;

        var info = GetActionInfo(actionId);
        var rule = MatchRule(casterName, info.Name);
        if (rule == null)
        {
            if (loggedUnknownCasts.Add(actionId))
                log.Information("Boss Helper saw unmatched cast: {Caster} | {ActionId} | {ActionName}", casterName, actionId, info.Name);
            return;
        }

        if (activeCasts.Exists(c => c.ActionId == actionId || EqualsText(c.CastName, info.Name)))
            return;

        activeCasts.Add(new CastAlert(casterObjectId, actionId, casterName, info.Name, rule.Details, rule.Advice, info.IconId, remaining));
    }

    private CastActionInfo GetActionInfo(uint actionId)
    {
        if (actionInfoCache.TryGetValue(actionId, out var info))
            return info;

        var row = dataManager.GetExcelSheet<ActionRow>()?.GetRow(actionId);
        var name = row?.Name.ExtractText() ?? $"Action {actionId}";
        var icon = row?.Icon ?? 0;
        info = new CastActionInfo(name, icon);
        actionInfoCache[actionId] = info;
        return info;
    }

    private static CastRule? MatchRule(string casterName, string actionName)
    {
        var dungeonRule = MatchDungeonRule(casterName, actionName);
        if (dungeonRule != null)
            return dungeonRule;

        if (Contains(casterName, "Enuo"))
        {
            foreach (var rule in EnuoRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Doomtrain") || Contains(casterName, "Doom Train") || Contains(casterName, "Ghost Train"))
        {
            foreach (var rule in DoomtrainExtremeRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }

            foreach (var rule in DoomtrainRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Zelenia"))
        {
            foreach (var rule in ZeleniaExtremeRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }

            foreach (var rule in ZeleniaRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Faithbound Kirin") || Contains(casterName, "Kirin"))
        {
            foreach (var rule in KirinRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Duskbound Byakko") || Contains(casterName, "Byakko"))
        {
            foreach (var rule in ByakkoRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Moonbound Genbu") || Contains(casterName, "Genbu"))
        {
            foreach (var rule in GenbuRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Sunbound Suzaku") || Contains(casterName, "Suzaku"))
        {
            foreach (var rule in SuzakuRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Dawnbound Seiryu") || Contains(casterName, "Seiryu"))
        {
            foreach (var rule in SeiryuRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Detector"))
        {
            foreach (var rule in DetectorRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Omega"))
        {
            foreach (var rule in OmegaRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Ultima"))
        {
            foreach (var rule in UltimaRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Kraken"))
        {
            foreach (var rule in KrakenRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Light Elemental"))
        {
            foreach (var rule in LightElementalRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Banshee"))
        {
            foreach (var rule in BansheeRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Alkyoneus") || Contains(casterName, "Alkyneus"))
        {
            foreach (var rule in AlkyoneusRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Giant Ranger"))
        {
            foreach (var rule in GiantRangerRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Giant Ascetic"))
        {
            foreach (var rule in GiantAsceticRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Kam'lanaut") || Contains(casterName, "Kamlanaut"))
        {
            foreach (var rule in KamlanautRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Eald'narche") || Contains(casterName, "Ealdnarche"))
        {
            foreach (var rule in EaldnarcheRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Prishe"))
        {
            foreach (var rule in PrisheRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Fafnir"))
        {
            foreach (var rule in FafnirRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Darter"))
        {
            foreach (var rule in FafnirRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Goblin Replica")
            || Contains(casterName, "Vanguard")
            || Contains(casterName, "Goobbue")
            || Contains(casterName, "Aquarius")
            || Contains(casterName, "Sprinkler")
            || Contains(casterName, "Groundskeeper")
            || Contains(casterName, "Despot")
            || Contains(casterName, "Flamingo"))
        {
            foreach (var rule in JeunoTrashRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Ark Angel MR"))
        {
            foreach (var rule in ArkAngelMrRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Ark Angel GK"))
        {
            foreach (var rule in ArkAngelGkRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Ark Angel TT"))
        {
            foreach (var rule in ArkAngelTtRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Ark Angel EV"))
        {
            foreach (var rule in ArkAngelEvRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Ark Angel HM"))
        {
            foreach (var rule in ArkAngelHmRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Shadow Lord") || Contains(casterName, "Lordly Shadow"))
        {
            foreach (var rule in ShadowLordRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Queen Eternal"))
        {
            foreach (var rule in QueenEternalExtremeRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }

            foreach (var rule in QueenEternalRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Zoraal Ja"))
        {
            foreach (var rule in ZoraalJaExtremeRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }

            foreach (var rule in ZoraalJaRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Necron"))
        {
            foreach (var rule in NecronExtremeRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }

            foreach (var rule in NecronRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Icy Hands") || Contains(casterName, "Beckoning Hands"))
        {
            foreach (var rule in NecronAddRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Valigarmanda"))
        {
            foreach (var rule in ValigarmandaExtremeRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }

            foreach (var rule in ValigarmandaRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Shantotto"))
        {
            foreach (var rule in ShantottoRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Alexander"))
        {
            foreach (var rule in AlexanderRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Gordius System"))
        {
            foreach (var rule in GordiusSystemRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Promathia"))
        {
            foreach (var rule in PromathiaRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Shinryu Paradox"))
        {
            foreach (var rule in ShinryuParadoxRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Hollow King"))
        {
            foreach (var rule in HollowKingRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        if (Contains(casterName, "Guardian Arkveld") || Contains(casterName, "Arkveld"))
        {
            foreach (var rule in GuardianArkveldRules)
            {
                if (EqualsText(actionName, rule.CastName))
                    return rule;
            }
        }

        return null;
    }

    private static CastRule Rule(string castName, string details, params AdvicePiece[] advice)
        => new(castName, details, advice);

    private static ChatMechanicRule ChatRule(IReadOnlyList<string> needles, string source, string title, params AdvicePiece[] advice)
        => new(string.Join("|", needles), needles, source, title, Rule(title, $"{source}: {title}", advice), 7.0);

    private static DungeonCastRule DungeonRule(string[] casterNames, string castName, string details, params AdvicePiece[] advice)
        => new(casterNames, Rule(castName, details, advice));

    private static AdvicePiece Normal(string text) => new(text, AdviceColor.Normal);
    private static AdvicePiece Green(string text) => new(text, AdviceColor.Green);
    private static AdvicePiece Blue(string text) => new(text, AdviceColor.Blue);
    private static AdvicePiece Yellow(string text) => new(text, AdviceColor.Yellow);

    private static bool Contains(string value, string needle)
        => value.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static bool EqualsText(string left, string right)
        => string.Equals(left.Trim(), right, StringComparison.OrdinalIgnoreCase);

    private static CastRule? MatchDungeonRule(string casterName, string actionName)
    {
        foreach (var dungeonRule in DawntrailDungeonRules)
        {
            if (!dungeonRule.CasterNames.Any(name => Contains(casterName, name)))
                continue;

            if (EqualsText(actionName, dungeonRule.Rule.CastName))
                return dungeonRule.Rule;
        }

        return null;
    }

    private void DrawTestCast(Configuration cfg)
    {
        var alpha = TestAlpha();
        if (alpha <= 0f)
        {
            testCastVisible = false;
            return;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, alpha);
        var remaining = Math.Max(0f, 4.8f - (float)(ImGui.GetTime() - testCastStartedAt));
        var rule = testRule ?? EnuoRules[0];
        var cast = new CastAlert(0, 0, "Enuo", rule.CastName, rule.Details, rule.Advice, 405, remaining);
        activeCastBlocks.Clear();
        activeCastBlocks.Add(new CastBlock(() => DrawCast(cast, cfg)));
        DrawCastBlocks(activeCastBlocks, cfg);
        ImGui.PopStyleVar();
    }

    private void DrawCast(CastAlert cast, Configuration cfg)
    {
        if (cfg.CastHelperShowIcon && cast.IconId > 0)
        {
            var icon = textureProvider.GetFromGameIcon(new GameIconLookup(cast.IconId));
            if (icon.TryGetWrap(out var texture, out _))
            {
                ImGui.Image(texture.Handle, new Vector2(cfg.CastHelperIconSize));
                ImGui.SameLine();
            }
        }

        var title = cfg.CastHelperShowTime
            ? $"{cast.CastName}  {cast.RemainingTime:F1}s"
            : cast.CastName;

        DrawStyledText(title, cfg.CastHelperNameColor, cfg.CastHelperNameEffect, cfg.CastHelperNameEffectColor, cfg.CastHelperNameFontSize);

        if (cfg.CastHelperShowDetails && !string.IsNullOrWhiteSpace(cast.Details))
            DrawStyledText(cast.Details, cfg.CastHelperDetailsColor, cfg.CastHelperDetailsEffect, cfg.CastHelperDetailsEffectColor, cfg.CastHelperDetailsFontSize, cfg.CastHelperWrapWidth);

        if (cfg.CastHelperShowAdvice)
            DrawAdvice(cast, cfg);
    }

    private static void DrawAdvice(CastAlert cast, Configuration cfg)
    {
        DrawStyledPieces(cast.Advice, cfg);
    }

    private static void DrawStyledPieces(IReadOnlyList<AdvicePiece> pieces, Configuration cfg)
    {
        var scale = cfg.CastHelperAdviceFontSize / ImGui.GetFontSize();
        ImGui.SetWindowFontScale(scale);

        var lineStart = ImGui.GetCursorScreenPos();
        var cursor = lineStart;
        var lineHeight = ImGui.CalcTextSize("Ag").Y;
        var maxX = lineStart.X + cfg.CastHelperWrapWidth;

        foreach (var piece in pieces)
        {
            var color = ColorFor(piece.Color, cfg);
            foreach (var token in Tokens(piece.Text))
            {
                var size = ImGui.CalcTextSize(token);
                if (cursor.X > lineStart.X && cursor.X + size.X > maxX)
                {
                    cursor = new Vector2(lineStart.X, cursor.Y + lineHeight + ImGui.GetStyle().ItemSpacing.Y);
                }

                DrawStyledLineAt(cursor, token, color, cfg.CastHelperAdviceEffect, cfg.CastHelperAdviceEffectColor);
                cursor.X += size.X;
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(lineStart.X, cursor.Y + lineHeight + ImGui.GetStyle().ItemSpacing.Y));
        ImGui.SetWindowFontScale(1f);
    }

    private static IEnumerable<string> Tokens(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
            yield return i == words.Length - 1 ? words[i] : words[i] + " ";
    }

    private static Vector4 ColorFor(AdviceColor color, Configuration cfg)
        => color switch
        {
            AdviceColor.Green => cfg.CastHelperImportantColor,
            AdviceColor.Blue => cfg.CastHelperBlueColor,
            AdviceColor.Yellow => cfg.CastHelperYellowColor,
            _ => cfg.CastHelperAdviceColor,
        };

    private static void DrawCastBlocks(List<CastBlock> blocks, Configuration cfg)
    {
        if (blocks.Count == 0)
            return;

        var start = ImGui.GetCursorScreenPos();
        var baseCursor = ImGui.GetCursorPos();
        var spacing = cfg.CastHelperCastSpacing;

        foreach (var block in blocks)
            MeasureCastBlock(block, cfg);

        var width = 0f;
        var totalHeight = 0f;
        for (var i = 0; i < blocks.Count; i++)
        {
            width = Math.Max(width, blocks[i].Size.X);
            totalHeight += blocks[i].Size.Y + (i == blocks.Count - 1 ? 0f : spacing);
        }

        var y = 0f;
        for (var i = 0; i < blocks.Count; i++)
        {
            ImGui.SetCursorScreenPos(start + new Vector2(0f, y));
            DrawCastBlock(blocks[i].DrawContent, cfg);
            y += blocks[i].Size.Y + spacing;
        }

        ImGui.SetCursorPos(baseCursor);
        ImGui.Dummy(new Vector2(width, totalHeight));
    }

    private static void MeasureCastBlock(CastBlock block, Configuration cfg)
    {
        var start = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(new Vector2(-100000f, -100000f));
        ImGui.BeginGroup();
        block.DrawContent();
        ImGui.EndGroup();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        block.Size = (max - min) + new Vector2(cfg.CastHelperBackgroundPadding * 2f);
        ImGui.SetCursorScreenPos(start);
    }

    private static void DrawCastBlock(System.Action drawContent, Configuration cfg)
    {
        var padding = cfg.CastHelperBackgroundPadding;
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

        if (cfg.CastHelperBackgroundOpacity > 0f)
        {
            var color = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, cfg.CastHelperBackgroundOpacity));
            drawList.ChannelsSetCurrent(0);
            drawList.AddRectFilled(min, max, color, 8f);
        }

        drawList.ChannelsMerge();
        ImGui.SetCursorScreenPos(new Vector2(start.X, max.Y));
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
        DrawStyledLineAt(ImGui.GetCursorScreenPos(), text, color, effect, effectColor);
        ImGui.SetWindowFontScale(1f);
    }

    private static void DrawStyledLineAt(Vector2 textPos, string text, Vector4 color, TextEffect effect, Vector4 effectColor)
    {
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

    private sealed record CastActionInfo(string Name, uint IconId);
    private enum AdviceColor { Normal, Green, Blue, Yellow }
    private sealed record AdvicePiece(string Text, AdviceColor Color);
    private sealed record CastRule(string CastName, string Details, IReadOnlyList<AdvicePiece> Advice);
    private sealed record ChatMechanicRule(string Key, IReadOnlyList<string> Needles, string Source, string Title, CastRule Rule, double DurationSeconds);
    private sealed record DungeonCastRule(IReadOnlyList<string> CasterNames, CastRule Rule);
    private sealed record CastAlert(ulong CasterObjectId, uint ActionId, string CasterName, string CastName, string Details, IReadOnlyList<AdvicePiece> Advice, uint IconId, float RemainingTime);
    private sealed record MechanicAlert(string Key, double ExpiresAt, CastAlert Cast);
    private sealed class CastBlock(System.Action drawContent)
    {
        public System.Action DrawContent { get; } = drawContent;
        public Vector2 Size { get; set; }
    }
}
