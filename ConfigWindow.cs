using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace Godlike;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin Plugin;
    private readonly Configuration Configuration;

    private const float LabelWidth = 130f;
    private const float ValueWidth = 200f;

    public ConfigWindow(Plugin plugin) : base(
        "GODLIKE",
        ImGuiWindowFlags.NoCollapse)
    {
        this.Size = new Vector2(400, 320);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 240),
            MaximumSize = new Vector2(1000, 1400),
        };

        this.Plugin = plugin;
        this.Configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.TextColored(new Vector4(1f, 0.84f, 0.0f, 1f), "PvP Killstreak Announcer");
        ImGui.Spacing();

        if (ImGui.BeginTable("GodlikeTable", 2, ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.None, LabelWidth);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.None, ValueWidth);

            ImGui.TableNextColumn();
            ImGui.Text("Enabled:");
            ImGui.TableNextColumn();
            var enabled = Configuration.KillStreakEnabled;
            if (ImGui.Checkbox("##KillStreakEnabled", ref enabled))
            {
                Configuration.KillStreakEnabled = enabled;
                Configuration.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("CoD-style PvP killstreak announcer with escalating voice callouts.\nStreak resets when you die; resets each match.");

            ImGui.TableNextColumn();
            ImGui.Text("Announcer:");
            ImGui.TableNextColumn();
            var showMilestones = Configuration.KillStreakShowMilestones;
            if (ImGui.Checkbox("##KillStreakShowMilestones", ref showMilestones))
            {
                Configuration.KillStreakShowMilestones = showMilestones;
                Configuration.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show escalating streak callouts (KILL, DOUBLE KILL,\nMONSTER KILL, ...) as your streak climbs.");

            ImGui.TableNextColumn();
            ImGui.Text("Reset on Death:");
            ImGui.TableNextColumn();
            var resetOnDeath = Configuration.KillStreakResetOnDeath;
            if (ImGui.Checkbox("##KillStreakResetOnDeath", ref resetOnDeath))
            {
                Configuration.KillStreakResetOnDeath = resetOnDeath;
                Configuration.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("When on, dying ends your streak. When off, the streak\nkeeps climbing for the whole match (resets each match).");

            ImGui.TableNextColumn();
            ImGui.Text("Voice Announcer:");
            ImGui.TableNextColumn();
            var voiceEnabled = Configuration.KillStreakVoiceEnabled;
            if (ImGui.Checkbox("##KillStreakVoiceEnabled", ref voiceEnabled))
            {
                Configuration.KillStreakVoiceEnabled = voiceEnabled;
                Configuration.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Play the bundled escalating voice-line callouts (echo included).");

            if (Configuration.KillStreakVoiceEnabled)
            {
                ImGui.TableNextColumn();
                ImGui.Text("Voice Volume:");
                ImGui.TableNextColumn();
                ImGui.PushItemWidth(ValueWidth);
                var volume = Configuration.KillStreakVoiceVolume * 100f;
                if (ImGui.DragFloat("##KillStreakVoiceVolume", ref volume, 1f, 0f, 100f, "%.0f%%"))
                {
                    Configuration.KillStreakVoiceVolume = Math.Clamp(volume / 100f, 0f, 1f);
                    Configuration.Save();
                }
            }

            ImGui.EndTable();
        }

#if DEBUG
        // Dev-only preview tool: present in Debug builds, compiled out of Release.
        ImGui.Spacing();
        if (ImGui.Button("Test announcer", new Vector2(LabelWidth + ValueWidth, 22)))
            Plugin.KillStreak.TriggerTest();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Preview the streak callout. Click repeatedly\nto escalate the tier.");
#endif

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextDisabled("/godlike - settings   |   help via the plugin's main button");
    }
}
