using System;
using System.Reflection;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using UnityEngine;

namespace PlateDurabilityUI
{
    /// <summary>
    /// One postfix, on <c>GridItemView.UpdateInfo</c>. Every view that draws an item icon with its
    /// numbers is a GridItemView: stash and container grids, equipment slots (SlotItemView),
    /// corpses, traders, the flea market, insurance, mail. UpdateInfo is what the game itself
    /// calls whenever an item's numbers need redrawing: when the view is made, when a child item
    /// (a plate) is added or removed, when the item is examined. Every override calls the base.
    /// </summary>
    internal static class Patches
    {
        private static Func<ItemView, bool> _examined;
        private static bool _loggedFailure;

        internal static void Apply(Harmony harmony)
        {
            // Examined is protected. Unexamined items show "?" for their numbers in vanilla, so
            // the gauge stays off them too, or it would give away what the item is.
            MethodInfo examinedGetter = AccessTools.PropertyGetter(typeof(ItemView), "Examined")
                                        ?? throw new MissingMemberException("ItemView", "Examined");
            _examined = AccessTools.MethodDelegate<Func<ItemView, bool>>(examinedGetter);

            MethodInfo updateInfo = AccessTools.Method(typeof(GridItemView), nameof(GridItemView.UpdateInfo))
                                    ?? throw new MissingMethodException("GridItemView", "UpdateInfo");
            harmony.Patch(updateInfo, postfix: new HarmonyMethod(typeof(Patches), nameof(UpdateInfoPostfix)));

            // The tooltip is extra: if the game has changed under it, the gauge still works.
            try
            {
                PlateTooltip.Apply(harmony);
            }
            catch (Exception e)
            {
                PlateDurabilityPlugin.Log.LogWarning("Detailed plate tooltip unavailable, vanilla tooltip kept: " + e.Message);
            }
        }

        private static void UpdateInfoPostfix(GridItemView __instance, RectTransform ____infoPanel, ItemViewBottomPanel ___BottomPanel)
        {
            try
            {
                bool examined = __instance.Item != null && _examined(__instance);
                PlateGauge.Refresh(__instance, ____infoPanel, ___BottomPanel, examined);
            }
            catch (Exception e)
            {
                // A broken gauge must never take the item view down with it.
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    PlateDurabilityPlugin.Log.LogError("Plate gauge failed (logged once): " + e);
                }
            }
        }
    }
}
