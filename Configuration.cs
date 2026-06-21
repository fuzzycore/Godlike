using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace Godlike;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    // PvP killstreak announcer with escalating voice callouts.
    public bool KillStreakEnabled { get; set; } = true;
    public bool KillStreakShowMilestones { get; set; } = true; // escalating streak announcer banners
    public bool KillStreakResetOnDeath { get; set; } = false;  // default off: dying keeps the streak going; when true, dying ends it
    public bool KillStreakVoiceEnabled { get; set; } = true;   // play the bundled voice-announcer pack
    public float KillStreakVoiceVolume { get; set; } = 1.0f;   // 0..1

    [NonSerialized]
    private IDalamudPluginInterface? PluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface) => this.PluginInterface = pluginInterface;

    public void Save() => this.PluginInterface!.SavePluginConfig(this);
}
