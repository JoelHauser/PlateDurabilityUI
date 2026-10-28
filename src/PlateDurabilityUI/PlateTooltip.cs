using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using UnityEngine;

namespace PlateDurabilityUI
{
    /// <summary>
    /// A fuller version of the game's own armor plate tooltip.
    ///
    /// Vanilla already has one: hovering the durability numbers on an armor icon shows the
    /// MultiLineTooltip with a header and one row per filled plate and insert ("class icon, slot
    /// name, durability/max"). This keeps that window, its header, rows and separator lines, and
    /// only rewrites the rows: every slot including empty ones, plates in front/back/left/right
    /// order and soft inserts after them, each with its durability in the gauge's colour, a
    /// percentage, broken and repair wear called out, and a "plates fitted" summary. Hovering
    /// the gauge itself opens the same window.
    ///
    /// The tooltip data type and its row type are obfuscated, so they are reached by shape: the
    /// one-parameter MultiLineTooltip.Show whose argument has a Lines array field, that array's
    /// element type with a (string, string) constructor, and the argument type's
    /// (CompoundItem, bool) constructor, which is the one vanilla builds the armor rows with.
    /// </summary>
    internal static class PlateTooltip
    {
        private const string Grey = "#9A978A";
        private const string EmptyColour = "#C4C0AE";
        private const string BrokenColour = "#E5553D";
        private const string WearColour = "#A0705F";

        private static MethodInfo _show;
        private static FieldInfo _lines;
        private static Type _lineType;
        private static FieldInfo _lineLabel;
        private static ConstructorInfo _lineCtor;
        private static ConstructorInfo _armorInfoCtor;
        private static Func<int, string> _classIcon;
        private static bool _loggedFailure;

        private static readonly ArmorReading Reading = new ArmorReading();

        /// <summary>The armor item the pointer is over, if any, and its view. Set on hover, cleared on leave.</summary>
        private static Item _subject;
        private static GridItemView _subjectView;

        internal static void Apply(Harmony harmony)
        {
            _show = typeof(MultiLineTooltip)
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(m => m.Name == "Show"
                                         && m.GetParameters().Length == 1
                                         && m.GetParameters()[0].ParameterType.GetField("Lines") != null)
                    ?? throw new MissingMethodException("MultiLineTooltip", "Show(info with Lines)");

            Type infoType = _show.GetParameters()[0].ParameterType;
            _lines = infoType.GetField("Lines");
            _lineType = _lines.FieldType.GetElementType()
                        ?? throw new MissingMemberException(infoType.Name, "Lines element type");
            _lineCtor = _lineType.GetConstructor(new[] { typeof(string), typeof(string) })
                        ?? throw new MissingMethodException(_lineType.Name, ".ctor(string, string)");
            _lineLabel = _lineType.GetField("Label")
                         ?? throw new MissingFieldException(_lineType.Name, "Label");

            // The same window shows trader prices, ammo penetration, unsellable parts and more,
            // so this constructor is also how a Show is recognised as the armor one (by its header).
            _armorInfoCtor = infoType.GetConstructor(new[] { typeof(CompoundItem), typeof(bool) })
                             ?? throw new MissingMethodException(infoType.Name, ".ctor(CompoundItem, bool)");

            _classIcon = FindClassIconFormatter();

            harmony.Patch(_show, prefix: new HarmonyMethod(typeof(PlateTooltip), nameof(ShowPrefix)));
            harmony.Patch(AccessTools.Method(typeof(GridItemView), nameof(GridItemView.OnPointerEnter)),
                          prefix: new HarmonyMethod(typeof(PlateTooltip), nameof(EnterPrefix)));
            harmony.Patch(AccessTools.Method(typeof(GridItemView), nameof(GridItemView.OnPointerExit)),
                          postfix: new HarmonyMethod(typeof(PlateTooltip), nameof(ExitPostfix)));
        }

        internal static bool CanOpenFromGauge => _armorInfoCtor != null;

        // ---- hover tracking --------------------------------------------------------------

        private static void EnterPrefix(GridItemView __instance)
        {
            _subject = __instance.Item;
            _subjectView = __instance;
        }

        private static void ExitPostfix(GridItemView __instance)
        {
            if (_subjectView == __instance)
            {
                _subject = null;
                _subjectView = null;
            }
        }

        /// <summary>
        /// The hovered item, while its view is still on screen showing it. A view closed, pooled
        /// or moved under the pointer never gets its exit, so the subject alone can be stale.
        /// </summary>
        private static Item Hovered()
        {
            if (_subject == null || _subjectView == null || !_subjectView.isActiveAndEnabled || _subjectView.Item != _subject)
            {
                _subject = null;
                _subjectView = null;
            }
            return _subject;
        }

        /// <summary>From the gauge: open the plate tooltip the way hovering the numbers does.</summary>
        internal static void OpenFor(GridItemView view, Item item)
        {
            ItemUiContext context = ItemUiContext.Instance;
            if (_armorInfoCtor == null || context == null || context.MultiLineTooltip == null || !(item is CompoundItem compound))
            {
                return;
            }
            try
            {
                _subject = item;
                _subjectView = view;
                object info = _armorInfoCtor.Invoke(new object[] { compound, false });
                if (context.Tooltip != null)
                {
                    context.Tooltip.Close();
                }
                _show.Invoke(context.MultiLineTooltip, new[] { info });
            }
            catch (Exception e)
            {
                LogOnce(e);
            }
        }

        internal static void Close()
        {
            ItemUiContext context = ItemUiContext.Instance;
            if (context != null && context.MultiLineTooltip != null && context.MultiLineTooltip.Displayed)
            {
                context.MultiLineTooltip.Close();
            }
        }

        // ---- rewriting the rows ----------------------------------------------------------

        private static void ShowPrefix(object __0)
        {
            try
            {
                // Its own setting: the gauge's on/off switch does not turn it off.
                if (__0 == null || !PlateDurabilityPlugin.DetailedTooltip.Value)
                {
                    return;
                }
                Item subject = Hovered();
                if (!(subject is CompoundItem compound) || !(_lines.GetValue(__0) is Array vanilla) || vanilla.Length == 0)
                {
                    return;
                }
                object header = vanilla.GetValue(0);
                if (!IsArmorTooltip(header, compound))
                {
                    // Another tooltip in the same window (a trader price, unsellable parts, the
                    // flea's removable plates) that opened over armor: left as it is.
                    return;
                }
                if (!PlateReader.TryRead(subject, Reading, log: false))
                {
                    // Not armor we draw a gauge for (a soft vest, a helmet): vanilla rows stay.
                    return;
                }
                _lines.SetValue(__0, Build(header));
            }
            catch (Exception e)
            {
                LogOnce(e);
            }
        }

        /// <summary>
        /// Whether <paramref name="header"/> is the first row of the game's armor plate tooltip:
        /// the same label the game's own (CompoundItem, false) constructor starts it with.
        /// </summary>
        private static bool IsArmorTooltip(object header, CompoundItem item)
        {
            if (header == null)
            {
                return false;
            }
            object reference = _armorInfoCtor.Invoke(new object[] { item, false });
            if (!(_lines.GetValue(reference) is Array lines) || lines.Length == 0 || lines.GetValue(0) == null)
            {
                return false;
            }
            return string.Equals(_lineLabel.GetValue(header) as string, _lineLabel.GetValue(lines.GetValue(0)) as string,
                                 StringComparison.Ordinal);
        }

        private static Array Build(object vanillaHeader)
        {
            List<object> rows = new List<object> { vanillaHeader, null };

            int fitted = 0;
            foreach (PlateReading plate in Reading.Plates)
            {
                rows.Add(Row(plate));
                if (plate.State != PlateState.Empty)
                {
                    fitted++;
                }
            }

            if (Reading.Softs.Count > 0)
            {
                rows.Add(null);
                foreach (PlateReading soft in Reading.Softs)
                {
                    rows.Add(Row(soft));
                }
            }

            rows.Add(null);
            int slots = Reading.Plates.Count;
            string count = fitted + "/" + slots;
            rows.Add(Line(Text.PlatesFitted, fitted < slots ? Colour(count, BrokenColour) : count));

            Array array = Array.CreateInstance(_lineType, rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                array.SetValue(rows[i], i);
            }
            return array;
        }

        private static object Row(PlateReading plate)
        {
            string name = SlotName(plate.Slot);
            if (plate.State == PlateState.Empty || plate.Armor == null)
            {
                return Line(name, Colour(Text.Empty, EmptyColour));
            }

            string icon = _classIcon != null ? _classIcon(plate.Armor.ArmorClass) : null;
            string label = string.IsNullOrEmpty(icon) ? name : icon + " " + name;

            RepairableComponent repairable = plate.Armor.Repairable;
            float full = PlateDurabilityPlugin.Scale.Value == DurabilityScale.OriginalMaximum
                ? Mathf.Max(repairable.TemplateDurability, repairable.MaxDurability)
                : repairable.MaxDurability;
            string numbers = Number(repairable.Durability) + Colour("/" + Number(full), Grey);

            if (plate.State == PlateState.Broken)
            {
                return Line(label, Colour(Text.Broken, BrokenColour) + "  " + numbers);
            }

            string hex = "#" + ColorUtility.ToHtmlStringRGB(Sprites.Ramp(plate.Fraction));
            string value = Colour(numbers, hex) + "  " + Colour(Mathf.RoundToInt(plate.Fraction * 100f) + "%", hex);
            if (plate.Wear > 0.005f && PlateDurabilityPlugin.ShowWear.Value)
            {
                value += "  " + Colour(Text.Max + " " + Number(repairable.MaxDurability), WearColour);
            }
            return Line(label, value);
        }

        private static object Line(string label, string value)
        {
            return _lineCtor.Invoke(new object[] { label, value });
        }

        private static string SlotName(Slot slot)
        {
            string name = slot.LocalizedName();
            return string.IsNullOrEmpty(name) ? slot.ID : name;
        }

        private static string Number(float value)
        {
            return Math.Round(value, 1).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string Colour(string text, string hex)
        {
            return "<color=" + hex + ">" + text + "</color>";
        }

        /// <summary>
        /// The game's own armor class icon tag, the one its plate tooltips start every row with.
        /// A public static FormatArmorClassIcon(int) on an obfuscated class: found by name.
        /// </summary>
        private static Func<int, string> FindClassIconFormatter()
        {
            Type[] types;
            try
            {
                types = typeof(Slot).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray();
            }
            foreach (Type type in types)
            {
                MethodInfo method = type.GetMethod("FormatArmorClassIcon", BindingFlags.Public | BindingFlags.Static,
                                                   null, new[] { typeof(int) }, null);
                if (method != null && method.ReturnType == typeof(string))
                {
                    return (Func<int, string>)Delegate.CreateDelegate(typeof(Func<int, string>), method);
                }
            }
            PlateDurabilityPlugin.Log.LogWarning("FormatArmorClassIcon not found: plate rows will have no class icon.");
            return null;
        }

        private static void LogOnce(Exception e)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                PlateDurabilityPlugin.Log.LogError("Plate tooltip failed (logged once), vanilla rows kept: " + e);
            }
        }

        /// <summary>English only for now; the slot names themselves come from the game's locale.</summary>
        private static class Text
        {
            internal const string Empty = "Empty";
            internal const string Broken = "Broken";
            internal const string Max = "max";
            internal const string PlatesFitted = "Plates fitted";
        }
    }
}
