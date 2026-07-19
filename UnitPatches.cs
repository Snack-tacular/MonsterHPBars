using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace MonsterHPBars
{
    /// <summary>
    /// Harmony patches that hook into the game's Unit lifecycle to spawn and
    /// update HP bars without modifying any game files.
    /// </summary>
    internal static class UnitPatches
    {
        // ─────────────────────────────────────────────────────────────────────
        // Patch: UnitManager.RegisterUnit — called when any unit (including
        //        lair-spawned and pooled ones) is registered into active gameplay.
        // ─────────────────────────────────────────────────────────────────────
        [HarmonyPatch(typeof(UnitManager), nameof(UnitManager.RegisterUnit))]
        [HarmonyPostfix]
        private static void UnitManager_RegisterUnit_Postfix(Unit unit, Collider collider)
        {
            TryAddHPBar(unit);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Patch: Unit.OnNetworkSpawn — called when a Unit becomes active on the
        //        network.
        // ─────────────────────────────────────────────────────────────────────
        [HarmonyPatch(typeof(Unit), nameof(Unit.OnNetworkSpawn))]
        [HarmonyPostfix]
        private static void Unit_OnNetworkSpawn_Postfix(Unit __instance)
        {
            TryAddHPBar(__instance);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Patch: Unit.OnEnable — fallback for units that spawn outside NGO
        // ─────────────────────────────────────────────────────────────────────
        [HarmonyPatch(typeof(Unit), "OnEnable")]
        [HarmonyPostfix]
        private static void Unit_OnEnable_Postfix(Unit __instance)
        {
            TryAddHPBar(__instance);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Patch: Unit.OnNetworkDespawn — clean up when a unit is removed
        // ─────────────────────────────────────────────────────────────────────
        [HarmonyPatch(typeof(Unit), nameof(Unit.OnNetworkDespawn))]
        [HarmonyPostfix]
        private static void Unit_OnNetworkDespawn_Postfix(Unit __instance)
        {
            RemoveBar(__instance);
        }

        [HarmonyPatch(typeof(Unit), "OnDisable")]
        [HarmonyPostfix]
        private static void Unit_OnDisable_Postfix(Unit __instance)
        {
            RemoveBar(__instance);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Patch: SimpleDamageable.TakeDamage — notify bar that damage occurred
        //        so the visibility timer resets even when AlwaysVisible is off.
        // ─────────────────────────────────────────────────────────────────────
        [HarmonyPatch(typeof(SimpleDamageable), nameof(SimpleDamageable.TakeDamage))]
        [HarmonyPostfix]
        private static void SimpleDamageable_TakeDamage_Postfix(SimpleDamageable __instance)
        {
            if (__instance.Owner == null) return;
            var bar = __instance.Owner.GetComponent<HPBarComponent>();
            if (bar != null)
                bar.NotifyDamage();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────
        public static void TryAddHPBar(Unit unit)
        {
            try
            {
                if (unit == null) return;

                // Filter out players immediately
                if (unit.isPlayerCharacter) return;

                // Avoid duplicates
                var existing = unit.GetComponent<HPBarComponent>();
                if (existing != null) return;

                // If Damageable is not initialized yet (common in pooled objects), 
                // defer initialization by 3 frames instead of running a global periodic scan.
                if (unit.Damageable == null)
                {
                    if (MonsterHPBarsPlugin.Instance != null)
                    {
                        MonsterHPBarsPlugin.Instance.StartCoroutine(DeferredAddHPBar(unit));
                    }
                    return;
                }

                AddHPBarComponent(unit, unit.Damageable);
            }
            catch (System.Exception ex)
            {
                MonsterHPBarsPlugin.Log.LogWarning($"HP bar creation failed: {ex.Message}");
            }
        }

        private static IEnumerator DeferredAddHPBar(Unit unit)
        {
            // Wait for 3 frames to allow object pool component setup to complete
            yield return null;
            yield return null;
            yield return null;

            if (unit == null) yield break;

            var existing = unit.GetComponent<HPBarComponent>();
            if (existing != null) yield break;

            IDamageable? dmg = unit.Damageable;
            if (dmg != null)
            {
                AddHPBarComponent(unit, dmg);
            }
        }

        private static void AddHPBarComponent(Unit unit, IDamageable dmg)
        {
            var comp = unit.gameObject.AddComponent<HPBarComponent>();
            comp.Init(dmg, unit.UnitName, unit.isBoss, unit.isEliteUnit);
            MonsterHPBarsPlugin.Log.LogDebug($"HP bar component attached to '{unit.UnitName}'");
        }

        private static void RemoveBar(Unit unit)
        {
            if (unit == null) return;
            var bar = unit.GetComponent<HPBarComponent>();
            if (bar != null)
            {
                GameObject.Destroy(bar);
            }
        }
    }
}
