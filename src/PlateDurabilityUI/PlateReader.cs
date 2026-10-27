using System;
using System.Collections.Generic;
using System.Text;
using EFT.InventoryLogic;
using UnityEngine;

namespace PlateDurabilityUI
{
    internal enum PlateZone
    {
        Front,
        Back,
        Left,
        Right,
        Other
    }

    internal enum PlateState
    {
        Fitted,
        Broken,
        Empty
    }

    internal struct PlateReading
    {
        public PlateZone Zone;
        public PlateState State;

        /// <summary>Durability left, 0..1 of the chosen scale.</summary>
        public float Fraction;

        /// <summary>Maximum lost to repairs, 0..1 of the original maximum. 0 on CurrentMaximum.</summary>
        public float Wear;

        public string SlotId;

        /// <summary>For the tooltip: its localized name. Never null.</summary>
        public Slot Slot;

        /// <summary>For the tooltip: class and exact numbers. Null for an empty slot.</summary>
        public ArmorComponent Armor;
    }

    /// <summary>Everything one gauge draws, read from one armor item.</summary>
    internal sealed class ArmorReading
    {
        public readonly List<PlateReading> Plates = new List<PlateReading>();

        /// <summary>Every soft insert, in slot order. The gauge only draws the weakest; the tooltip lists them all.</summary>
        public readonly List<PlateReading> Softs = new List<PlateReading>();

        public bool HasSoft;

        /// <summary>The weakest soft insert. Empty beats broken beats any fraction.</summary>
        public PlateReading Soft;

        /// <summary>Changes whenever anything drawn would change. Polled to catch damage in raid.</summary>
        public int Signature;

        public void Clear()
        {
            Plates.Clear();
            Softs.Clear();
            HasSoft = false;
            Soft = default;
            Signature = 0;
        }

        public bool AllAtOrAbove(float fraction)
        {
            foreach (PlateReading plate in Plates)
            {
                if (plate.State != PlateState.Fitted || plate.Fraction < fraction)
                {
                    return false;
                }
            }
            return !HasSoft || (Soft.State == PlateState.Fitted && Soft.Fraction >= fraction);
        }
    }

    /// <summary>
    /// Reads plate slots off an armor item.
    ///
    /// No item ids, no slot names from a list: a slot counts as armor when the game itself would
    /// treat it so. That is any slot with plate colliders (front, back and side plates) or body
    /// colliders (built-in soft armor), any slot the item's ArmorHolderComponent lists, and any
    /// slot holding an item with an ArmorComponent. Plates and carriers added by other mods are
    /// built from the same template fields, so they are read the same way. Helmets are left out:
    /// anything with a head collider in any armor slot.
    /// </summary>
    internal static class PlateReader
    {
        private static readonly HashSet<Slot> HolderSlots = new HashSet<Slot>();

        /// <param name="log">False for the gauge's own polling, which would flood the log.</param>
        /// <param name="anyArmor">True for the raid alert: read rigs and body armor whatever the gauge's own switches say.</param>
        internal static bool TryRead(Item item, ArmorReading reading, bool log, bool anyArmor = false)
        {
            reading.Clear();
            if (!(item is CompoundItem compound) || compound.Slots == null || compound.Slots.Length == 0)
            {
                return false;
            }

            // A rig carries a grid for its pouches; body armor has none.
            bool isRig = compound.Grids != null && compound.Grids.Length > 0;
            if (!anyArmor && (isRig ? !PlateDurabilityPlugin.OnRigs.Value : !PlateDurabilityPlugin.OnBodyArmor.Value))
            {
                return false;
            }

            HolderSlots.Clear();
            if (item.TryGetItemComponent<ArmorHolderComponent>(out ArmorHolderComponent holder))
            {
                foreach (Slot slot in (IEnumerable<Slot>)holder.ArmorSlots)
                {
                    HolderSlots.Add(slot);
                }
            }

            bool originalScale = PlateDurabilityPlugin.Scale.Value == DurabilityScale.OriginalMaximum;
            bool anySoft = false;
            PlateReading weakestSoft = default;

            foreach (Slot slot in compound.Slots)
            {
                if (slot == null)
                {
                    continue;
                }

                Item contained = slot.ContainedItem;
                ArmorComponent armor = contained?.GetItemComponent<ArmorComponent>();
                bool plateColliders = slot.ArmorPlateColliderMask != 0;
                EBodyPartColliderType[] bodyColliders = slot.ArmorColliders;
                bool softColliders = bodyColliders != null && bodyColliders.Length > 0;

                if (!plateColliders && !softColliders && armor == null && !HolderSlots.Contains(slot))
                {
                    continue;
                }
                if (contained != null && armor == null)
                {
                    // Something that is not armor sits in an armor slot. Nothing to measure.
                    continue;
                }
                if (IsHead(bodyColliders) || (armor != null && IsHead(armor.Template.ArmorColliders)))
                {
                    return false;
                }

                bool hard = plateColliders
                            || (armor != null && armor.Template.ArmorPlateColliders != 0)
                            || (!softColliders && !slot.Locked);

                PlateReading plate = Measure(armor, originalScale);
                plate.SlotId = slot.ID;
                plate.Slot = slot;
                plate.Armor = armor;

                if (hard)
                {
                    plate.Zone = ZoneOf(slot, armor);
                    reading.Plates.Add(plate);
                }
                else
                {
                    reading.Softs.Add(plate);
                    if (!anySoft || Weaker(plate, weakestSoft))
                    {
                        weakestSoft = plate;
                    }
                    anySoft = true;
                }
            }

            if (reading.Plates.Count == 0)
            {
                // Soft armor alone (a soft vest) takes no plates: no gauge, the vanilla numbers say it all.
                return false;
            }

            reading.Plates.Sort((a, b) =>
            {
                int byZone = a.Zone.CompareTo(b.Zone);
                return byZone != 0 ? byZone : string.CompareOrdinal(a.SlotId, b.SlotId);
            });
            reading.HasSoft = anySoft;
            reading.Soft = weakestSoft;
            reading.Signature = SignatureOf(reading);

            if (log && PlateDurabilityPlugin.DebugLogging.Value)
            {
                PlateDurabilityPlugin.Debug(Describe(item, reading));
            }
            return true;
        }

        private static PlateReading Measure(ArmorComponent armor, bool originalScale)
        {
            RepairableComponent repairable = armor?.Repairable;
            if (repairable == null)
            {
                return new PlateReading { State = PlateState.Empty };
            }

            float current = repairable.MaxDurability;
            float full = originalScale ? Mathf.Max(repairable.TemplateDurability, current) : current;
            PlateReading plate = new PlateReading
            {
                State = armor.IsDestroyed ? PlateState.Broken : PlateState.Fitted,
                Fraction = full > 0f ? Mathf.Clamp01(repairable.Durability / full) : 0f,
                Wear = originalScale && full > 0f ? Mathf.Clamp01((full - current) / full) : 0f
            };
            return plate;
        }

        private static bool Weaker(PlateReading a, PlateReading b)
        {
            if (a.State != b.State)
            {
                return a.State > b.State; // Empty > Broken > Fitted
            }
            return a.Fraction < b.Fraction;
        }

        private static PlateZone ZoneOf(Slot slot, ArmorComponent armor)
        {
            EArmorPlateCollider mask = slot.ArmorPlateColliderMask;
            if (mask == 0 && armor != null)
            {
                mask = armor.Template.ArmorPlateColliders;
            }
            if (mask != 0)
            {
                PlateZone best = PlateZone.Other;
                foreach (EArmorPlateCollider flag in (EArmorPlateCollider[])Enum.GetValues(typeof(EArmorPlateCollider)))
                {
                    if ((mask & flag) != 0)
                    {
                        PlateZone zone = ZoneFromName(flag.ToString());
                        if (zone < best)
                        {
                            best = zone;
                        }
                    }
                }
                if (best != PlateZone.Other)
                {
                    return best;
                }
            }
            return ZoneFromName(slot.ID);
        }

        private static PlateZone ZoneFromName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return PlateZone.Other;
            }
            string n = name.ToLowerInvariant();
            if (n.Contains("chest") || n.Contains("front"))
            {
                return PlateZone.Front;
            }
            if (n.Contains("back") || n.Contains("rear"))
            {
                return PlateZone.Back;
            }
            if (n.Contains("left"))
            {
                return PlateZone.Left;
            }
            if (n.Contains("right"))
            {
                return PlateZone.Right;
            }
            return PlateZone.Other;
        }

        private static bool IsHead(EBodyPartColliderType[] colliders)
        {
            if (colliders == null)
            {
                return false;
            }
            foreach (EBodyPartColliderType collider in colliders)
            {
                switch (collider)
                {
                    case EBodyPartColliderType.HeadCommon:
                    case EBodyPartColliderType.ParietalHead:
                    case EBodyPartColliderType.BackHead:
                    case EBodyPartColliderType.Ears:
                    case EBodyPartColliderType.Eyes:
                    case EBodyPartColliderType.Jaw:
                        return true;
                }
            }
            return false;
        }

        private static int SignatureOf(ArmorReading reading)
        {
            unchecked
            {
                int hash = 17;
                foreach (PlateReading plate in reading.Plates)
                {
                    hash = hash * 31 + Hash(plate);
                }
                hash = hash * 31 + (reading.HasSoft ? Hash(reading.Soft) : 0);
                return hash;
            }
        }

        private static int Hash(PlateReading plate)
        {
            unchecked
            {
                // Thousandths: finer than any bar can show, coarse enough to ignore float noise.
                return ((int)plate.Zone * 7 + (int)plate.State) * 1000003
                       + Mathf.RoundToInt(plate.Fraction * 1000f) * 1009
                       + Mathf.RoundToInt(plate.Wear * 1000f);
            }
        }

        private static string Describe(Item item, ArmorReading reading)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Gauge for ").Append(item.TemplateId).Append(':');
            foreach (PlateReading plate in reading.Plates)
            {
                sb.Append(' ').Append(plate.SlotId).Append('=').Append(plate.Zone).Append('/')
                  .Append(plate.State).Append(' ').Append(Mathf.RoundToInt(plate.Fraction * 100f)).Append('%');
                if (plate.Wear > 0f)
                {
                    sb.Append(" wear ").Append(Mathf.RoundToInt(plate.Wear * 100f)).Append('%');
                }
            }
            if (reading.HasSoft)
            {
                sb.Append(" | weakest soft ").Append(reading.Soft.SlotId).Append(' ')
                  .Append(reading.Soft.State).Append(' ').Append(Mathf.RoundToInt(reading.Soft.Fraction * 100f)).Append('%');
            }
            return sb.ToString();
        }
    }
}
