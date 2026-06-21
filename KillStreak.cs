using Dalamud.Bindings.ImGui;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace Godlike;

/// <summary>
/// Frontline kill streak tracker with an escalating on-screen announcer.
///
/// Kills come straight from the game's live "knockout" (killing-blow) counter inside the Frontline
/// content director. It increments the instant the game credits you a kill - direct OR
/// damage-over-time - so it's both faster (the death packet lags ~2-3s in PvP) and exactly accurate
/// (matches the scoreboard). We drive the streak off its per-frame deltas and reset it when the
/// local player dies. Totals reset each match (territory change).
/// </summary>
public unsafe class KillStreak : IDisposable
{
    private readonly Plugin Plugin;
    private readonly IFontHandle? announcerFont;
    private readonly VoicePlayer voice = new();

    // Live "knockout" (killing-blow) counter inside the Frontline content director. It increments
    // the instant the game credits you a kill - direct OR damage-over-time - so polling it is far
    // faster and more accurate than the death packet (~2s late). It's the match total (never resets
    // on death), so we drive the streak off its deltas.
    //
    // PATCH-FRAGILE: this is a hardcoded struct offset, not a stable API. A game patch can shift the
    // director layout and silently move this field. If kills stop registering (the streak never
    // climbs during Frontline), this offset is the first suspect - re-locate the knockout counter in
    // the current client and update the value here.
    private const int KnockoutOffset = 0x33B8;
    private int lastKnockouts = -1;

    // The local player's Frontline "Battle High" gauge - a single byte in the same content director,
    // sitting immediately before the knockout counter. (PvpStats' FrontlineContentDirector maps
    // PlayerBattleHigh at 0x24D2 + 0xEE5 = 0x33B7, i.e. KnockoutOffset - 1.) Tied to KnockoutOffset so
    // re-locating the knockout counter on a patch carries Battle High along with it - same
    // PATCH-FRAGILE caveat applies.
    //
    // The byte holds POINTS (0-100), NOT the rank: every 20 points is one rank, so rank = points / 20
    // (20 -> I, 40 -> II, 60 -> III, 80 -> IV, 100 -> V). We fire a (queued) callout each time the
    // rank climbs.
    private const int BattleHighOffset = KnockoutOffset - 1;
    private const int BattleHighPointsPerRank = 20;
    private const int BattleHighMaxPoints = 100;
    private int lastBattleHighRank = -1;

    // Tracks the local player's alive/dead state so we can reset the streak the moment we die.
    private bool wasDead;

    // Cache of resolved per-tier voice files, rebuilt whenever the configured folder changes.
    private string loadedVoiceFolder = string.Empty;
    private readonly Dictionary<int, string> voiceFiles = new();

    // Resolved Battle High voice files, keyed by rank 1-5 (files BATTLE_HIGH_ONE..FIVE).
    private readonly Dictionary<int, string> battleHighFiles = new();
    private static readonly string[] BattleHighWords = { "ONE", "TWO", "THREE", "FOUR", "FIVE" };

    private static readonly string[] VoiceExtensions = { ".wav", ".mp3" };

    // Streak at which callouts go full unhinged (rainbow + pulse + screen-shake).
    private const int ExtremeThreshold = 30;

    // Escalating announcer ladder. Each entry is the minimum streak needed for that callout; the
    // announcer shows the highest tier the current streak qualifies for. Past the final tier it
    // keeps counting with an "xN" suffix so it never runs out.
    private static readonly (int Threshold, string Name)[] StreakTiers =
    {
        (1,  "KILL"),
        (2,  "DOUBLE KILL"),
        (3,  "TRIPLE KILL"),
        (4,  "MULTIKILL"),
        (5,  "MULTIKILL"),
        (6,  "MULTIKILL"),
        (7,  "MULTIKILL"),
        (8,  "KILLING FRENZY"),
        (9,  "MONSTER KILL"),
        (10, "GODLIKE"),
        (11, "WICKED SICK"),
        (12, "UNSTOPPABLE"),
        (13, "LEGENDARY"),
        (15, "RAMPAGE"),
        (17, "BEYOND GODLIKE"),
        (20, "APOCALYPSE"),
        (22, "ONE-PERSON ARMY"),
        (24, "IS THIS EVEN LEGAL?"),
        (26, "REPORTED FOR HACKING"),
        (28, "SOMEBODY CALL AN AMBULANCE"),
        (30, "DELETE THE SERVER"),
        (31, "THIS IS NOT FAIR"),
        (32, "BRO WHAT ARE YOU COMPENSATING FOR"),
        (33, "YOU NEED HELP"),
        (34, "TOUCH GRASS, PLEASE"),
        (35, "YOU'RE GOING TO GAOL"),
        (36, "ARE YOU EVEN HUMAN?"),
        (37, "STOP, THEY'RE ALREADY DEAD"),
        (38, "WHO HURT YOU"),
        (40, "YOSHI-P IS SCARED OF YOU"),
        (42, "UNINSTALL AFTER THIS MATCH"),
        (44, "THE ENEMY TEAM CALLED THE POLICE"),
        (46, "YOU ARE LEGALLY A NATURAL DISASTER"),
        (48, "SQUARE ENIX WANTS A WORD"),
        (50, "YOU HAVE BROKEN THE SIMULATION"),
    };

    private static readonly Vector3[] StreakColors =
    {
        new(1.00f, 1.00f, 1.00f), // 0  KILL - white
        new(0.60f, 0.90f, 1.00f), // 1  DOUBLE KILL - light blue
        new(0.40f, 1.00f, 0.45f), // 2  TRIPLE KILL - green
        new(0.70f, 1.00f, 0.30f), // 3  MULTIKILL - lime
        new(1.00f, 0.90f, 0.30f), // 4  MULTIKILL - yellow
        new(1.00f, 0.65f, 0.20f), // 5  MULTIKILL - amber
        new(1.00f, 0.40f, 0.15f), // 6  MULTIKILL - orange-red
        new(1.00f, 0.25f, 0.65f), // 7  KILLING FRENZY - pink
        new(0.70f, 0.35f, 1.00f), // 8  MONSTER KILL - purple
        new(1.00f, 0.84f, 0.00f), // 9  GODLIKE - gold
        new(0.30f, 0.85f, 1.00f), // 10 WICKED SICK - cyan
        new(1.00f, 0.45f, 0.10f), // 11 UNSTOPPABLE - molten orange
        new(1.00f, 0.10f, 0.55f), // 12 LEGENDARY - hot magenta
        new(1.00f, 0.45f, 0.10f), // 13 RAMPAGE - molten orange
        new(1.00f, 0.10f, 0.55f), // 14 BEYOND GODLIKE - hot magenta
        new(1.00f, 0.20f, 0.10f), // 15 APOCALYPSE - blood red
        new(0.85f, 0.05f, 0.20f), // 16 ONE-PERSON ARMY - crimson
        new(1.00f, 0.30f, 0.10f), // 17 IS THIS EVEN LEGAL - ember
        // 30+ tiers render as rainbow; entries past here clamp to the last color above.
    };

    private int streak;

    private int testCount;
    private DateTime lastTestTime = DateTime.MinValue;

    private string bannerText = string.Empty;
    private int bannerTier;
    private bool bannerExtreme;
    private DateTime bannerUntil = DateTime.MinValue;
    private DateTime bannerStart = DateTime.MinValue;

    public KillStreak(Plugin plugin)
    {
        this.Plugin = plugin;

        try
        {
            // Bake a large, crisp display font (the game's condensed zone-title face) so the
            // announcer isn't a blurry upscale of the small default font atlas.
            this.announcerFont = Plugin.PluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(
                new GameFontStyle(GameFontFamilyAndSize.TrumpGothic68));
        }
        catch (Exception ex)
        {
            Plugin.Log($"KillStreak: failed to create announcer font: {ex.Message}");
        }
    }

    private void OnKill()
    {
        streak++;

        if (Plugin.Configuration.KillStreakShowMilestones)
            ShowAnnouncer(streak);
    }

    private void ShowAnnouncer(int count)
    {
        // Pick the highest tier whose threshold the streak has reached.
        var tier = 0;
        for (var i = 0; i < StreakTiers.Length; i++)
            if (count >= StreakTiers[i].Threshold)
                tier = i;

        var name = StreakTiers[tier].Name;

        // Past the final named tier, keep escalating with a multiplier.
        if (count > StreakTiers[^1].Threshold)
            name = $"{name} x{count}";

        bannerText = name;
        bannerTier = tier;
        bannerExtreme = count >= ExtremeThreshold;
        bannerStart = DateTime.UtcNow;
        bannerUntil = DateTime.UtcNow.AddSeconds(bannerExtreme ? 2.8 : 2.2);

        PlayStreakAudio(tier);
    }

    private void PlayStreakAudio(int tier)
    {
        if (Plugin.Configuration.KillStreakVoiceEnabled)
            TryPlayVoice(tier);
    }

    private bool TryPlayVoice(int tier)
    {
        try
        {
            var folder = ResolveVoiceFolder();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return false;

            if (folder != loadedVoiceFolder)
                RebuildVoiceCache(folder);

            if (!voiceFiles.TryGetValue(tier, out var path) || !File.Exists(path))
                return false;

            voice.Play(path, Plugin.Configuration.KillStreakVoiceVolume);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the "voicelines" pack bundled alongside the plugin DLL.
    /// </summary>
    private string ResolveVoiceFolder()
    {
        var dllDir = Plugin.PluginInterface.AssemblyLocation.DirectoryName;
        return dllDir == null ? string.Empty : Path.Combine(dllDir, "voicelines");
    }

    /// <summary>
    /// Scans the configured folder once and maps each ladder tier to an audio file. Files may be
    /// named by streak number (e.g. "1.wav", "12.mp3") or by the callout name with non-letters
    /// replaced by underscores (e.g. "DOUBLE_KILL.wav", "IS_THIS_EVEN_LEGAL.mp3").
    /// </summary>
    private void RebuildVoiceCache(string folder)
    {
        voiceFiles.Clear();
        battleHighFiles.Clear();
        loadedVoiceFolder = folder;

        for (var i = 0; i < StreakTiers.Length; i++)
        {
            var path = ResolveAudioFile(folder, StreakTiers[i].Threshold.ToString(), Slug(StreakTiers[i].Name));
            if (path != null)
                voiceFiles[i] = path;
        }

        for (var level = 1; level <= BattleHighWords.Length; level++)
        {
            var path = ResolveAudioFile(folder, $"BATTLE_HIGH_{BattleHighWords[level - 1]}");
            if (path != null)
                battleHighFiles[level] = path;
        }
    }

    /// <summary>Returns the first existing audio file matching any of the given base names (in order),
    /// trying each supported extension, or null if none exist.</summary>
    private static string? ResolveAudioFile(string folder, params string[] baseNames)
    {
        foreach (var baseName in baseNames)
            foreach (var ext in VoiceExtensions)
            {
                var path = Path.Combine(folder, baseName + ext);
                if (File.Exists(path))
                    return path;
            }
        return null;
    }

    private static string Slug(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_');
        return sb.ToString().Trim('_');
    }

    /// <summary>Resets the streak the moment the local player dies (alive -> dead transition).</summary>
    private void PollLocalDeath()
    {
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null)
        {
            wasDead = false;
            return;
        }

        var dead = localPlayer.CurrentHp == 0;
        if (dead && !wasDead && Plugin.Configuration.KillStreakResetOnDeath)
            streak = 0;

        wasDead = dead;
    }

    public void Reset()
    {
        streak = 0;
        testCount = 0;
        bannerUntil = DateTime.MinValue;
        lastKnockouts = -1;
        lastBattleHighRank = -1;
        wasDead = false;
    }

    /// <summary>
    /// Previews the announcer (used by the settings test button). Repeated clicks climb the
    /// ladder just like a growing streak; it resets after a few idle seconds.
    /// </summary>
    public void TriggerTest()
    {
        var now = DateTime.UtcNow;
        if (now - lastTestTime > TimeSpan.FromSeconds(3))
            testCount = 0;
        // Advance one ladder rung per click (wrapping) so every tier - including the
        // absurd 30+ callouts - can be previewed without racking up 50 clicks.
        testCount = testCount % StreakTiers.Length + 1;
        lastTestTime = now;

        ShowAnnouncer(StreakTiers[testCount - 1].Threshold);
    }

    public void Draw()
    {
        // Drive kills straight off the game's live Frontline knockout counter (instant + exact),
        // and reset the streak the instant we die.
        if (Plugin.Configuration.KillStreakEnabled)
        {
            // Order matters: process kills first so their callout is already playing when a
            // resulting Battle High rank-up queues its line up behind it.
            PollKnockoutCounter();
            PollBattleHigh();
            PollLocalDeath();
        }

        // The announcer banner draws whenever active, so the settings test button works
        // outside of PvP too.
        if (DateTime.UtcNow < bannerUntil)
            DrawBanner();
    }

    /// <summary>Returns true while the local player is in a Frontline match (where the knockout
    /// counter lives). Reading deep into the content director is only safe here - other PvP modes
    /// have a smaller/different layout.</summary>
    private bool IsFrontline()
    {
        var gm = GameMain.Instance();
        return gm != null && gm->CurrentTerritoryIntendedUseId == TerritoryIntendedUse.Frontline;
    }

    /// <summary>Base pointer of the live Frontline content director, or null if unavailable/not Frontline.</summary>
    private byte* GetFrontlineDirector()
    {
        if (!Plugin.Client.IsPvP || !IsFrontline())
            return null;

        var ef = EventFramework.Instance();
        if (ef == null)
            return null;

        return (byte*)(IntPtr)ef->GetInstanceContentDirector();
    }

    /// <summary>Drives the streak directly off the game's live knockout counter. Each increment is a
    /// confirmed killing blow (instant, exact, multi-kill aware); the counter is the match total, so
    /// we react to its deltas. We baseline silently on first read / counter reset.</summary>
    private void PollKnockoutCounter()
    {
        var dir = GetFrontlineDirector();
        if (dir == null)
        {
            lastKnockouts = -1;
            return;
        }

        try
        {
            int knockouts = *(dir + KnockoutOffset);

            if (lastKnockouts < 0 || knockouts < lastKnockouts)
            {
                // First read this match, or the counter reset for a new match - sync without firing.
                lastKnockouts = knockouts;
                return;
            }

            if (knockouts > lastKnockouts)
            {
                var newKills = knockouts - lastKnockouts;
                lastKnockouts = knockouts;
                for (var i = 0; i < newKills; i++)
                    OnKill();
            }
        }
        catch
        {
            // Offset may drift across patches - never fatal.
        }
    }

    /// <summary>Watches the local player's Battle High rank and fires a callout each time it climbs.
    /// The gauge byte is points (0-100); every 20 points is one rank, so rank = points / 20. The
    /// callout is queued behind any in-flight kill callout (see <see cref="OnBattleHighRankUp"/>)
    /// because the same kill that earned the rank-up should be announced in full first. Decay (points
    /// dropping over time) is tracked silently so climbing back up re-triggers.</summary>
    private void PollBattleHigh()
    {
        var dir = GetFrontlineDirector();
        if (dir == null)
        {
            lastBattleHighRank = -1;
            return;
        }

        try
        {
            int points = *(dir + BattleHighOffset);

            // The gauge runs 0-100 points; anything outside that means we're reading the wrong bytes.
            if (points is < 0 or > BattleHighMaxPoints)
                return;

            var rank = points / BattleHighPointsPerRank;   // 0-5 (20 points per rank)

            if (lastBattleHighRank < 0)
            {
                // First read this match - baseline silently.
                lastBattleHighRank = rank;
                return;
            }

            if (rank > lastBattleHighRank)
                OnBattleHighRankUp(rank);

            lastBattleHighRank = rank;
        }
        catch
        {
            // Offset may drift across patches - never fatal.
        }
    }

    private void OnBattleHighRankUp(int level)
    {
        if (!Plugin.Configuration.KillStreakVoiceEnabled)
            return;

        try
        {
            var folder = ResolveVoiceFolder();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return;

            if (folder != loadedVoiceFolder)
                RebuildVoiceCache(folder);

            if (!battleHighFiles.TryGetValue(level, out var path) || !File.Exists(path))
                return;

            // Enqueue (not Play) so a kill callout already sounding plays to the end first.
            voice.Enqueue(path, Plugin.Configuration.KillStreakVoiceVolume);
        }
        catch
        {
            // Never let an announcer hiccup disrupt the frame.
        }
    }

    private void DrawBanner()
    {
        var io = ImGui.GetIO();
        var display = io.DisplaySize;

        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(display);
        ImGui.Begin("GODLIKE_Banner",
            ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoBringToFrontOnFocus);

        var useCustomFont = this.announcerFont is { Available: true };
        var pushed = useCustomFont ? this.announcerFont!.Push() : null;
        try
        {
            var drawList = ImGui.GetWindowDrawList();

            var now = DateTime.UtcNow;
            // Fade out over the last portion of the banner's lifetime.
            var remaining = (float)(bannerUntil - now).TotalSeconds;
            var alpha = Math.Clamp(remaining / 0.6f, 0f, 1f);

            // Quick "pop-in" overshoot scale at the start for impact.
            var age = (float)(now - bannerStart).TotalSeconds;
            var pop = age < 0.18f ? 1f + (0.18f - age) / 0.18f * 0.25f : 1f;

            // Extreme (30+) tiers go full chaos: cycling rainbow, a pulsing scale, and a shake.
            Vector3 color;
            var extraScale = 1f;
            var shake = Vector2.Zero;
            if (bannerExtreme)
            {
                color = HsvToRgb(age * 0.7f, 0.85f, 1f);
                extraScale = 1f + 0.05f * MathF.Sin(age * 14f);
                var amp = 6f;
                shake = new Vector2(MathF.Sin(age * 53f) * amp, MathF.Cos(age * 47f) * amp);
            }
            else
            {
                color = StreakColors[Math.Clamp(bannerTier, 0, StreakColors.Length - 1)];
            }

            var font = ImGui.GetFont();
            var native = font.FontSize;

            // Scale relative to the baked font size: bigger callouts as the tier climbs, but stay
            // near (or below) the native size so the glyphs render crisp instead of upscaled.
            var tierScale = Math.Min(0.72f + bannerTier * 0.06f, 1.15f) * pop * extraScale;
            var fontSize = native * tierScale;
            var textSize = ImGui.CalcTextSize(bannerText) * tierScale;
            var center = new Vector2(display.X * 0.5f, display.Y * 0.22f);
            var textPos = new Vector2(center.X - textSize.X / 2, center.Y - textSize.Y / 2) + shake;

            var outlineColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, alpha));
            var textColor = ImGui.ColorConvertFloat4ToU32(new Vector4(color.X, color.Y, color.Z, alpha));

            var outline = Math.Min(2 + bannerTier / 3, 4);
            for (var dx = -outline; dx <= outline; dx++)
                for (var dy = -outline; dy <= outline; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    drawList.AddText(font, fontSize, textPos + new Vector2(dx, dy), outlineColor, bannerText);
                }

            drawList.AddText(font, fontSize, textPos, textColor, bannerText);
        }
        finally
        {
            pushed?.Dispose();
            ImGui.End();
        }
    }

    private static Vector3 HsvToRgb(float h, float s, float v)
    {
        h = (h % 1f + 1f) % 1f * 6f;
        var i = (int)h;
        var f = h - i;
        var p = v * (1f - s);
        var q = v * (1f - f * s);
        var t = v * (1f - (1f - f) * s);
        return (i % 6) switch
        {
            0 => new Vector3(v, t, p),
            1 => new Vector3(q, v, p),
            2 => new Vector3(p, v, t),
            3 => new Vector3(p, q, v),
            4 => new Vector3(t, p, v),
            _ => new Vector3(v, p, q),
        };
    }

    public void Dispose()
    {
        this.announcerFont?.Dispose();
        this.voice.Dispose();
    }
}
