using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace MonsterHPBars
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class MonsterHPBarsPlugin : BaseUnityPlugin
    {
        public static MonsterHPBarsPlugin Instance { get; private set; } = null!;
        internal static ManualLogSource Log { get; private set; } = null!;

        // Toggle state
        public static bool IsModEnabled { get; private set; } = true;

        // Config entries
        public static ConfigEntry<KeyboardShortcut> ToggleKey { get; private set; } = null!;
        public static ConfigEntry<bool>  ShowOnlyEnemies   { get; private set; } = null!;
        public static ConfigEntry<bool>  ShowBossOnly       { get; private set; } = null!;
        public static ConfigEntry<float> BarWidth           { get; private set; } = null!;
        public static ConfigEntry<float> BarHeight          { get; private set; } = null!;
        public static ConfigEntry<float> BarHeightPadding   { get; private set; } = null!;
        public static ConfigEntry<bool>  ShowLabel          { get; private set; } = null!;
        public static ConfigEntry<Color> HealthyColor       { get; private set; } = null!;
        public static ConfigEntry<Color> DamagedColor       { get; private set; } = null!;
        public static ConfigEntry<Color> CriticalColor      { get; private set; } = null!;
        public static ConfigEntry<Color> EliteColor         { get; private set; } = null!;
        public static ConfigEntry<Color> BossColor          { get; private set; } = null!;
        public static ConfigEntry<float> DamagedThreshold   { get; private set; } = null!;
        public static ConfigEntry<float> CriticalThreshold  { get; private set; } = null!;
        public static ConfigEntry<bool>  AlwaysVisible      { get; private set; } = null!;
        public static ConfigEntry<float> VisibilityDuration { get; private set; } = null!;

        private Harmony _harmony = null!;

        private void Awake()
        {
            Instance = this;
            Log = base.Logger;

            BindConfig();

            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            _harmony.PatchAll(typeof(UnitPatches));

            Log.LogInfo("Monster HP Bars loaded successfully! Press F4 to toggle.");
        }

        private void Update()
        {
            if (ToggleKey.Value.IsDown())
            {
                ToggleMod();
            }
        }

        private void ToggleMod()
        {
            IsModEnabled = !IsModEnabled;
            Log.LogInfo($"Monster HP Bars toggled {(IsModEnabled ? "ON" : "OFF")}");
        }

        private void BindConfig()
        {
            const string sGeneral    = "1 - General";
            const string sAppearance = "2 - Appearance";
            const string sColors     = "3 - Colors";

            ToggleKey         = Config.Bind(sGeneral, "ToggleKey",         new KeyboardShortcut(KeyCode.F4), "Key to toggle the mod on/off.");
            ShowOnlyEnemies   = Config.Bind(sGeneral, "ShowOnlyEnemies",   true,  "Only show HP bars on enemy (Neutral team) units.");
            ShowBossOnly      = Config.Bind(sGeneral, "ShowBossOnly",       false, "Only show HP bars on boss units.");
            AlwaysVisible     = Config.Bind(sGeneral, "AlwaysVisible",      true,  "Always show bars, even when unit is at full health.");
            VisibilityDuration= Config.Bind(sGeneral, "VisibilityDuration", 5f,    "Seconds the bar stays visible after damage (when AlwaysVisible is false).");

            BarWidth          = Config.Bind(sAppearance, "BarWidth",         120f,  "Width of the HP bar in pixels.");
            BarHeight         = Config.Bind(sAppearance, "BarHeight",        10f,   "Height of the HP bar in pixels.");
            BarHeightPadding  = Config.Bind(sAppearance, "BarHeightPadding_v3", 1.5f,  "Additional vertical padding above the unit's head.");
            ShowLabel         = Config.Bind(sAppearance, "ShowLabel",        true,  "Show unit name label above the bar.");

            HealthyColor      = Config.Bind(sColors, "HealthyColor",   new Color(0.15f, 0.85f, 0.25f, 0.90f), "Bar color at high HP.");
            DamagedColor      = Config.Bind(sColors, "DamagedColor",  new Color(0.95f, 0.75f, 0.10f, 0.90f), "Bar color at medium HP.");
            CriticalColor     = Config.Bind(sColors, "CriticalColor", new Color(0.95f, 0.15f, 0.10f, 0.90f), "Bar color at low HP.");
            EliteColor        = Config.Bind(sColors, "EliteColor",    new Color(0.75f, 0.20f, 0.95f, 0.90f), "Accent color for elite units.");
            BossColor         = Config.Bind(sColors, "BossColor",     new Color(0.95f, 0.40f, 0.05f, 0.90f), "Accent color for boss units.");
            DamagedThreshold  = Config.Bind(sColors, "DamagedThreshold",  0.60f, "HP fraction below which the 'DamagedColor' is used.");
            CriticalThreshold = Config.Bind(sColors, "CriticalThreshold", 0.25f, "HP fraction below which the 'CriticalColor' is used.");
        }

        private void OnDestroy() => _harmony.UnpatchSelf();
    }
}
