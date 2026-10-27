using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace PlateDurabilityUI
{
    /// <summary>What a bar's 100% stands for.</summary>
    public enum DurabilityScale
    {
        /// <summary>The plate as it left the factory. What the game's protection maths uses.</summary>
        OriginalMaximum,

        /// <summary>The plate's maximum after repairs, as the vanilla tooltip numbers read.</summary>
        CurrentMaximum
    }

    /// <summary>
    /// Draws a small gauge on every piece of body armor and every armored rig: one bar per plate
    /// slot (front, back, left, right), plus a thin bar for the weakest soft insert. Each bar's
    /// height and colour follow that plate's durability, so a worn, broken or missing plate shows
    /// at a glance in the stash, on the character, in a corpse or at a trader.
    ///
    /// Nothing about the items changes; the gauge only reads them. See <see cref="PlateReader"/>
    /// for which slots count as plates, and <see cref="PlateGauge"/> for the drawing.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class PlateDurabilityPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mybutthasarash.platedurabilityui";
        public const string PluginName = "Plate Durability UI";

        /// <summary>Must match the csproj's Version. Two places, and they have to agree.</summary>
        public const string PluginVersion = "0.0.1";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> OnBodyArmor;
        internal static ConfigEntry<bool> OnRigs;
        internal static ConfigEntry<bool> ShowSoftBar;
        internal static ConfigEntry<bool> ShowLetters;
        internal static ConfigEntry<bool> DetailedTooltip;
        internal static ConfigEntry<bool> HoverGauge;
        internal static ConfigEntry<DurabilityScale> Scale;
        internal static ConfigEntry<bool> ShowWear;
        internal static ConfigEntry<bool> HideHealthy;
        internal static ConfigEntry<int> HealthyAbove;
        internal static ConfigEntry<float> Size;
        internal static ConfigEntry<bool> Animate;
        internal static ConfigEntry<bool> DebugLogging;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind(
                "1. General",
                "Show plate gauge",
                true,
                "A small gauge in the corner of body armor and armored rigs: one bar per plate, "
                + "in the order front, back, left, right.");

            OnBodyArmor = Config.Bind(
                "1. General",
                "On body armor",
                true,
                "Plate carriers and armor vests.");

            OnRigs = Config.Bind(
                "1. General",
                "On armored rigs",
                true,
                "Tactical rigs that take plates.");

            ShowSoftBar = Config.Bind(
                "1. General",
                "Show soft armor bar",
                true,
                "A thin bar under the plates for the built-in soft armor (front, back, sides, "
                + "collar, groin). It shows the weakest of them.");

            ShowLetters = Config.Bind(
                "1. General",
                "Show slot letters",
                true,
                "F, B, L and R under the bars.");

            DetailedTooltip = Config.Bind(
                "1. General",
                "Detailed plate tooltip",
                true,
                "The game's plate tooltip (hover the durability numbers on an armor icon) lists every "
                + "slot, empty ones too, in front, back, left, right order, with each plate's "
                + "durability in colour, a percentage, broken plates and repair wear. Off: the "
                + "vanilla rows.");

            HoverGauge = Config.Bind(
                "1. General",
                "Hover the gauge for details",
                true,
                "Pointing at the gauge opens the plate tooltip too, not only pointing at the numbers.");

            Scale = Config.Bind(
                "2. Durability",
                "Full bar means",
                DurabilityScale.OriginalMaximum,
                "OriginalMaximum: a full bar is the plate as new, the same scale as the durability "
                + "bar on equipped weapons and the one the game's protection maths uses, so a plate "
                + "repaired down to 40/40 from 55 reads 73%. CurrentMaximum: a full bar is the "
                + "plate's maximum after repairs, as the tooltip numbers read.");

            ShowWear = Config.Bind(
                "2. Durability",
                "Show repair wear",
                true,
                "Mark the maximum durability a plate has lost to repairs as a dark cap on top of "
                + "its bar, like the wear segment on weapon durability bars. Only with "
                + "OriginalMaximum.");

            HideHealthy = Config.Bind(
                "2. Durability",
                "Hide on healthy armor",
                false,
                "Show the gauge only once a plate is worn, broken or missing.");

            HealthyAbove = Config.Bind(
                "2. Durability",
                "Healthy above (%)",
                90,
                new ConfigDescription(
                    "With Hide on healthy armor: armor counts as healthy while every plate and soft "
                    + "insert is at or above this.",
                    new AcceptableValueRange<int>(50, 100)));

            Size = Config.Bind(
                "3. Look",
                "Size",
                1f,
                new ConfigDescription(
                    "Gauge size. It also shrinks by itself on small items so it never runs into "
                    + "the durability numbers.",
                    new AcceptableValueRange<float>(0.75f, 1.5f)));

            Animate = Config.Bind(
                "3. Look",
                "Animate changes",
                true,
                "Bars slide to their new level when a plate takes damage, is repaired or swapped.");

            DebugLogging = Config.Bind(
                "4. Debug",
                "Log armor readings",
                false,
                "Write each armor's plate slots and durabilities to LogOutput.log when its gauge is "
                + "drawn.");

            Config.SettingChanged += (sender, args) => PlateGauge.MarkAllDirty();

            Harmony harmony = new Harmony(PluginGuid);
            try
            {
                Patches.Apply(harmony);
            }
            catch (Exception e)
            {
                harmony.UnpatchSelf();
                Logger.LogError(PluginName + " " + PluginVersion
                                + " could not apply its patches, so the game is unmodified: " + e);
                return;
            }

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        internal static void Debug(string message)
        {
            if (DebugLogging != null && DebugLogging.Value)
            {
                Log.LogInfo(message);
            }
        }
    }
}
