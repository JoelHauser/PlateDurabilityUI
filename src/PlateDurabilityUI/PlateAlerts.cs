using System;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace PlateDurabilityUI
{
    /// <summary>
    /// In raid, a game message ("Front plate broken (Kirasa-N)") the moment a plate in the armor
    /// or rig the player is wearing is shot to zero.
    ///
    /// It polls the two equipment slots twice a second rather than hooking the damage code: the
    /// reading is the same one the gauge uses, so any plate the gauge shows is covered, and a
    /// change in how the game applies damage cannot break it. Only a change seen while polling
    /// counts, so armor that was already broken at raid start, or picked up broken, says nothing.
    /// </summary>
    internal static class PlateAlerts
    {
        private const float PollInterval = 0.5f;

        private static readonly EquipmentSlot[] Worn = { EquipmentSlot.ArmorVest, EquipmentSlot.TacticalVest };
        private static readonly ArmorReading Reading = new ArmorReading();
        private static readonly Dictionary<string, PlateState> Seen = new Dictionary<string, PlateState>();

        private static Player _player;
        private static float _nextPoll;
        private static bool _failed;
        private static MethodInfo _display;

        internal static void Tick()
        {
            if (_failed || !PlateDurabilityPlugin.AlertOnBreak.Value)
            {
                return;
            }
            float now = Time.unscaledTime;
            if (now < _nextPoll)
            {
                return;
            }
            _nextPoll = now + PollInterval;

            try
            {
                Poll();
            }
            catch (Exception e)
            {
                // The gauge must not go down with it.
                _failed = true;
                PlateDurabilityPlugin.Log.LogWarning("Plate break messages are off until the game restarts: " + e);
            }
        }

        private static void Poll()
        {
            Player player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
            if (player != _player)
            {
                // A new raid, or out of one: start again from what this player is wearing now.
                Seen.Clear();
                _player = player;
            }
            if (player == null)
            {
                return;
            }

            foreach (EquipmentSlot slot in Worn)
            {
                // Through the profile: Player.Equipment is only public in SPT's patched assembly.
                Item item = player.Profile.Inventory.Equipment.GetSlot(slot).ContainedItem;
                if (item == null || !PlateReader.TryRead(item, Reading, log: false, anyArmor: true))
                {
                    continue;
                }
                foreach (PlateReading plate in Reading.Plates)
                {
                    string key = item.Id + "/" + plate.SlotId;
                    bool known = Seen.TryGetValue(key, out PlateState before);
                    Seen[key] = plate.State;
                    if (known && before == PlateState.Fitted && plate.State == PlateState.Broken)
                    {
                        Show(plate.Slot.LocalizedName() + " broken (" + item.ShortName.Localized() + ")");
                    }
                }
            }
        }

        private static void Show(string message)
        {
            // NotificationManager has an SPT-remapped name, so it is looked up by name and not
            // compiled against: the source keeps building against the unpatched assembly too.
            if (_display == null)
            {
                Type manager = AccessTools.TypeByName("EFT.Communications.NotificationManager")
                               ?? throw new MissingMemberException("EFT.Communications.NotificationManager");
                _display = AccessTools.Method(manager, "DisplayMessageNotification",
                                              new[] { typeof(string), typeof(ENotificationDurationType), typeof(ENotificationIconType), typeof(Color?) })
                           ?? throw new MissingMethodException("NotificationManager", "DisplayMessageNotification");
            }
            _display.Invoke(null, new object[] { message, ENotificationDurationType.Default, ENotificationIconType.Alert, null });
        }
    }
}
