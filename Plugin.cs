using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Godlike;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "GODLIKE";
    public const string CommandConfig = "/godlike";

    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IPluginLog PluginLog { get; private set; } = null!;
    [PluginService] public static IClientState Client { get; private set; } = null!;
    [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;

    public Configuration Configuration { get; init; }
    internal KillStreak KillStreak { get; private set; }

    private ConfigWindow ConfigWindow { get; init; }
    private HelpWindow HelpWindow { get; init; }
    private readonly WindowSystem WindowSystem = new("GODLIKE");

    public Plugin()
    {
        this.Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.Configuration.Initialize(PluginInterface);

        Client.TerritoryChanged += OnTerritoryChanged;

        ConfigWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(ConfigWindow);
        HelpWindow = new HelpWindow(this);
        WindowSystem.AddWindow(HelpWindow);

        this.KillStreak = new KillStreak(this);

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleHelpUI;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;

        CommandManager.AddHandler(CommandConfig, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open GODLIKE settings.",
        });
    }

    public void Dispose()
    {
        Client.TerritoryChanged -= OnTerritoryChanged;
        CommandManager.RemoveHandler(CommandConfig);
        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleHelpUI;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUI;
        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        HelpWindow.Dispose();
        KillStreak.Dispose();
    }

    public static void Log(string message) => PluginLog.Debug(message);

    private void DrawUI()
    {
        WindowSystem.Draw();
        KillStreak.Draw();
    }

    private void ToggleHelpUI() => HelpWindow.Toggle();
    private void ToggleConfigUI() => ConfigWindow.Toggle();
    private void OnCommand(string command, string args) => ToggleConfigUI();

    private void OnTerritoryChanged(uint territory) => KillStreak.Reset();
}
