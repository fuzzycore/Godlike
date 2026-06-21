using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace Godlike;

public class HelpWindow : Window, IDisposable
{
    public HelpWindow(Plugin plugin) : base(
        "GODLIKE - Help",
        ImGuiWindowFlags.NoCollapse)
    {
        this.Size = new Vector2(640, 460);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(440, 320),
            MaximumSize = new Vector2(1200, 1400),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);

        ImGui.TextColored(new Vector4(1f, 0.84f, 0.0f, 1f), "GODLIKE");
        ImGui.TextWrapped("An unhinged PvP killstreak announcer. As your kill streak climbs, GODLIKE pops escalating arena-announcer callouts on screen - from a simple KILL all the way up to absurd 30+ tiers - with baked-in voice lines.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "How it works");

        ImGui.BulletText("Killing-blow accurate");
        ImGui.Indent();
        ImGui.TextWrapped("Kills are read straight from the game's own death messages - you get credit for the killing blow (including off-screen DoT kills), exactly matching the scoreboard.");
        ImGui.Unindent();

        ImGui.BulletText("Escalating callouts");
        ImGui.Indent();
        ImGui.TextWrapped("The banner climbs an escalating ladder (KILL, DOUBLE KILL, MONSTER KILL, GODLIKE, ...). Past 30 kills it goes full chaos: rainbow color, pulsing, and screen-shake.");
        ImGui.Unindent();

        ImGui.BulletText("Voice Announcer");
        ImGui.Indent();
        ImGui.TextWrapped("Plays the bundled escalating voice-line callouts (echo baked in). Adjust the volume or turn it off in settings. Use the Test announcer button to preview - click repeatedly to climb the tiers.");
        ImGui.Unindent();

        ImGui.BulletText("Reset on Death");
        ImGui.Indent();
        ImGui.TextWrapped("By default the streak resets when you die. Turn this off to let it build for the whole match. Either way it resets at the start of each match.");
        ImGui.Unindent();

        ImGui.Spacing();
        ImGui.TextWrapped("Use /godlike to open settings. This is a PvP-only feature.");

        ImGui.PopTextWrapPos();
    }
}
