using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Orbs;

namespace Sts2OrbLayout;

// NOrbManager.TweenLayout() 을 Prefix 로 가로채고, 저장된 waypoint 곡선이 있으면
// arc-length 기반 균등 분포로 모든 슬롯을 곡선 위에 배치한다. 저장 없으면 원본(부채형) 사용.
[HarmonyPatch(typeof(NOrbManager), "TweenLayout")]
public static class TweenLayoutPatch
{
    public static bool Prefix(NOrbManager __instance)
    {
        try
        {
            var player = __instance._creatureNode?.Entity?.Player;
            var combatState = player?.PlayerCombatState;
            if (combatState == null) return true;

            int capacity = combatState.OrbQueue.Capacity;
            if (capacity == 0) return true;

            if (!OrbLayoutStore.TryGetWaypoints(out var waypoints))
                return true;

            var orbs = __instance._orbs;
            if (orbs == null || orbs.Count != capacity) return true;

            var positions = OrbCurve.Distribute(waypoints, capacity);
            if (positions.Length != capacity) return true;

            __instance._curTween?.Kill();
            var tween = __instance.CreateTween().SetParallel();
            __instance._curTween = tween;

            float radiusScale = __instance.IsLocal ? 1f : 0.75f;
            for (int i = 0; i < capacity; i++)
            {
                Vector2 pos = positions[i] * radiusScale;
                tween.TweenProperty(orbs[i], "position", pos, 0.45)
                     .SetEase(Tween.EaseType.InOut)
                     .SetTrans(Tween.TransitionType.Sine);
            }
            return false;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[OrbLayout] TweenLayout prefix error: {ex.Message}");
            return true;
        }
    }
}
