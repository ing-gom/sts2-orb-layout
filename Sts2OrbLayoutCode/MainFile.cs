using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace Sts2OrbLayout;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "Sts2OrbLayout";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; }
        = new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        try
        {
            var harmony = new Harmony(ModId);
            harmony.PatchAll(typeof(MainFile).Assembly);
            Logger.Info("[OrbLayout] Harmony patches applied.");

            if (Engine.GetMainLoop() is SceneTree tree)
                OrbDragEditor.Install(tree);

            Logger.Info("[OrbLayout] initialized. Ctrl=show curve | Ctrl+drag waypoint=move | Ctrl+click on curve=add waypoint there | Ctrl+RMB=remove. Saves persist across restarts.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[OrbLayout] init failed: {ex.Message}");
        }
    }
}
