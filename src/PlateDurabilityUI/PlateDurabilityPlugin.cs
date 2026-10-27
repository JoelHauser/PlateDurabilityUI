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

    /// <summary>How the gauge is drawn. See <see cref="GaugeLook"/> for what each one sets.</summary>
    public enum GaugeStyle
    {
        /// <summary>A soft see-through shadow behind borderless bars, muted colours.</summary>
        Smoke,

        /// <summary>The 0.0.1 look: a dark framed panel, framed bars, bright colours.</summary>
        Classic,

        /// <summary>No backing: outlined bars and outlined letters straight on the item.</summary>
        Bare,

        /// <summary>A light silver chip like the game's armor class icons, dark bars and letters.</summary>
        Silver,

        /// <summary>No backing, no letters: thin outlined bars, half the size.</summary>
        Slim
    }

    /// <summary>Which corner of the item icon the gauge sits in.</summary>
    public enum GaugeCorner
    {
        BottomLeft,
        TopLeft,
        TopRight
    }

    /// <summary>What is written under each bar.</summary>
    public enum SlotLabel
    {
        /// <summary>F, B, L, R: which slot the bar is.</summary>
        Letters,

        /// <summary>The plate's armor class, a dash for an empty slot.</summary>
        ArmorClass
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
        public const string PluginVersion = "0.0.2";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> OnBodyArmor;
        internal static ConfigEntry<bool> OnRigs;
        internal static ConfigEntry<bool> InInventory;
        internal static ConfigEntry<bool> AtTraders;
        internal static ConfigEntry<bool> OnFlea;
        internal static ConfigEntry<bool> ShowSoftBar;
        internal static ConfigEntry<bool> ShowLetters;
        internal static ConfigEntry<SlotLabel> Labels;
        internal static ConfigEntry<bool> DetailedTooltip;
        internal static ConfigEntry<bool> HoverGauge;
        internal static ConfigEntry<DurabilityScale> Scale;
        internal static ConfigEntry<bool> ShowWear;
        internal static ConfigEntry<bool> HideHealthy;
        internal static ConfigEntry<int> HealthyAbove;
        internal static ConfigEntry<GaugeStyle> Style;
        internal static ConfigEntry<GaugeCorner> Corner;
        internal static ConfigEntry<float> Size;
        internal static ConfigEntry<bool> Animate;
        internal static ConfigEntry<bool> AlertOnBreak;
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

            InInventory = Config.Bind(
                "1. General",
                "In the stash and inventory",
                true,
                "Your stash, equipment, containers, loot and every other screen that is not a "
                + "trader or the flea market.");

            AtTraders = Config.Bind(
                "1. General",
                "At traders",
                true,
                "The trader's goods and your own items on the trade screen.");

            OnFlea = Config.Bind(
                "1. General",
                "On the flea market",
                true,
                "Flea market offers and the add-offer window.");

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
                "A label under each bar: the slot (F, B, L, R) or the plate's class, see Slot labels.");

            Labels = Config.Bind(
                "1. General",
                "Slot labels",
                SlotLabel.Letters,
                "Letters: F, B, L and R. ArmorClass: each plate's armor class (a dash for an empty "
                + "slot). Not drawn in the Slim style.");

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

            Style = Config.Bind(
                "3. Look",
                "Style",
                GaugeStyle.Smoke,
                "Smoke: a soft see-through shadow behind the bars, muted colours. "
                + "Classic: a dark framed panel, bright colours. "
                + "Bare: no backing, outlined bars and letters. "
                + "Silver: a light chip that matches the game's armor class icons. "
                + "Slim: no backing and no letters, thin bars, half the size.");

            Corner = Config.Bind(
                "3. Look",
                "Corner",
                GaugeCorner.BottomLeft,
                "Where on the icon the gauge sits. It moves clear of the game's own icons and text "
                + "in that corner (and other mods' icons): right of them at the bottom, under the "
                + "item name, quick-slot number or trader price at the top.");

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

            AlertOnBreak = Config.Bind(
                "4. In raid",
                "Message when a plate breaks",
                true,
                "In raid, when a plate in the armor or rig you are wearing is shot to zero, the game "
                + "shows a message in the top right saying which one.");

            DebugLogging = Config.Bind(
                "5. Debug",
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

        private void Update()
        {
            PlateAlerts.Tick();
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
