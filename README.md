# Chat Echo

> A Dalamud plugin for Final Fantasy XIV that echoes Party and Raid chat as a draggable HUD overlay — so you never miss a callout while tunnel-visioning through a fight.

![Chat Echo banner showing party chat messages overlaid on the game screen](https://raw.githubusercontent.com/BalthierArt/EchoChat/main/icon.png)

---

## What it does

It brings up chosen chat lines to upper screen so you dont miss callouts even with tunnel visioning or you just want you tells to pop up on your screen.

---

## Features

### Core
- **Draggable overlay** — unlock the banner, drag it anywhere on screen, then lock it back to make it fully click-through during combat
- **Fade out animation** — messages smoothly fade out in the last 0.4 seconds of their display time
- **Last message ghost** — the most recent message lingers at 20% opacity after expiring so you can glance back

### Channels
Enable exactly the channels you care about — each one independently toggled:
- Party, Alliance / Raid, PvP Team
- Say, Shout, Yell, Tell (incoming), Free Company, Novice Network
- Linkshells (all 8), Cross-world Linkshells (all 8)
- Echo, System, Notice, Urgent

Cross-world Party automatically inherits Party's settings — no separate configuration needed.

### Color Modes
Three modes to suit your preference:

| Mode | Description |
|---|---|
| **Per-channel** | Each channel has its own color, set individually |
| **Split** | Separate colors for the sender's name and the message text, per channel |
| **Solid** | One global color for all text regardless of channel |

### Text Effects
- **Outline** — 1px outline in a configurable color (default black) keeps text readable over any background
- **Shadow** — offset drop shadow for a softer look
- **None** — plain text

### Priority Highlighting
- Define a custom keyword list — words like `stack`, `spread`, `tank swap`, `lb3`, `heal`
- Matched keywords are highlighted in a distinct color (default red) within the message
- Uses **whole-word matching** — `out` won't highlight inside `outside`
- **Priority Only mode** — when enabled, messages without any keyword are silently dropped entirely. The status indicator turns red to remind you this filter is active.

### Display Options
- Font size: 10–72 px
- Background opacity and padding adjustable independently
- Show or hide the channel prefix e.g. `(Party)`
- First name only mode — shows `Moenbryda` instead of `Moenbryda Vrai`

---

## Commands

| Command | Effect |
|---|---|
| `/chatecho` | Toggle the settings window open/closed |
| `/chatecho on` | Enable the overlay |
| `/chatecho off` | Disable the overlay |
| `/chatecho test` | Fire a batch of fake raid callouts to preview the banner |

---

## Usage tips

1. Open `/chatecho` and enable the plugin with the toggle at the top
2. Go to the **General** tab and **unlock** the banner position
3. Close settings — the banner will appear with a visible title bar
4. Drag it to where you want it on screen (above your hotbars works well)
5. Reopen settings and **lock** it — the banner is now fully click-through
6. In the **Channels** tab, enable Party and Alliance at minimum
7. In the **Priority** tab, review the default keyword list and add your own callouts
8. Use `/chatecho test` to preview how everything looks without needing someone to talk in party chat

---

REPO-
https://raw.githubusercontent.com/BalthierArt/Leonhart/main/repo.json


---
## License

MIT — do whatever you like with it, credit appreciated but not required.

---

*Made for FFXIV raiders who tunnel vision a little too hard.*
