// SPDX-FileCopyrightText: 2022 Moony
// SPDX-FileCopyrightText: 2022 Paul Ritter
// SPDX-FileCopyrightText: 2022 Sam Weaver
// SPDX-FileCopyrightText: 2022 ShadowCommander
// SPDX-FileCopyrightText: 2022 metalgearsloth
// SPDX-FileCopyrightText: 2022 mirrorcult
// SPDX-FileCopyrightText: 2022 wrexbe
// SPDX-FileCopyrightText: 2023 Darkie
// SPDX-FileCopyrightText: 2023 DrSmugleaf
// SPDX-FileCopyrightText: 2024 LordCarve
// SPDX-FileCopyrightText: 2024 themias
// SPDX-FileCopyrightText: 2025 Hannah Giovanna Dawson
// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 Leon Friedrich
// SPDX-FileCopyrightText: 2025 slarticodefast
// SPDX-FileCopyrightText: 2026 github_actions[bot]
// SPDX-FileCopyrightText: 2026 nabegator220
//
// SPDX-License-Identifier: MPL-2.0

using System.Linq;
using System.Collections.Generic; //KS14
using System.Text.Json.Serialization;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Dictionary;
using Robust.Shared.Utility;
using Robust.Shared.Serialization;

namespace Content.Shared.Damage
{
    /// <summary>
    ///     This class represents a collection of damage types and damage values.
    /// </summary>
    /// <remarks>
    ///     The actual damage information is stored in <see cref="DamageDict"/>. This class provides
    ///     functions to apply resistance sets and supports basic math operations to modify this dictionary.
    /// </remarks>
    [DataDefinition, Serializable, NetSerializable]
    public sealed partial class DamageSpecifier : IEquatable<DamageSpecifier>
    {
        // For the record I regret so many of the decisions i made when rewriting damageable
        // Why is it just shitting out dictionaries left and right
        // One day Arrays, stackalloc spans, and SIMD will save the day.
        // TODO DAMAGEABLE REFACTOR

        // These exist solely so the wiki works. Please do not touch them or use them.
        [JsonPropertyName("types")]
        [DataField("types", customTypeSerializer: typeof(PrototypeIdDictionarySerializer<FixedPoint2, DamageTypePrototype>))]
        [UsedImplicitly]
        private Dictionary<string, FixedPoint2>? _damageTypeDictionary;

        [JsonPropertyName("groups")]
        [DataField("groups", customTypeSerializer: typeof(PrototypeIdDictionarySerializer<FixedPoint2, DamageGroupPrototype>))]
        [UsedImplicitly]
        private Dictionary<string, FixedPoint2>? _damageGroupDictionary;

        /// <summary>
        ///     Main DamageSpecifier dictionary. Most DamageSpecifier functions exist to somehow modifying this.
        /// </summary>
        [JsonIgnore]
        [ViewVariables(VVAccess.ReadWrite)]
        [IncludeDataField(customTypeSerializer: typeof(DamageSpecifierDictionarySerializer), readOnly: true)]
        public Dictionary<string, FixedPoint2> DamageDict { get; set; } = new();

        /// <summary>
        ///     KS14 addition
        ///     This is just a way for you to specify that a projectile/whatever gets through the percentile reduction given by armor.
        ///     It works via a percentile system - 0 is no AP, 1 is full AP.
        ///     If you go over 1 or under -1 the system will make stupid mistakes, please do not do it
        ///     I removed the clamp because I trust the sanity of my contribs and I can squeeze some minimal perf out by removing it
        ///     Why -1? Because you can set it to -1 to make armor twice as effective against it - could be fun for hollow points!
        /// </summary>

        [DataField("percentilePenetration", customTypeSerializer: typeof(PrototypeIdDictionarySerializer<float, DamageTypePrototype>))]
        public Dictionary<string, float>? PercentilePenetration;

        /// <summary>
        ///     KS14
        ///     This is just a way for you to specify that a projectile/whatever ignores some flat reduction given by armor.
        ///     It works by subtracting these reductions from the armor's reductions, of course that can't go below zero.
        ///     If you set this to a negative number it actually adds to the flat resistances - could be useful for something.
        /// </summary>

        [DataField("flatPenetration", customTypeSerializer: typeof(PrototypeIdDictionarySerializer<float, DamageTypePrototype>))]
        public Dictionary<string, float>? FlatPenetration;

        /// <summary>
        ///     KS14
        ///     Right now percentile damage penetration applies to flat damage resistances
        ///     If an armor has 10 flat slash resistance but you have 40% slash ap then it decreases that resist to 6 slash
        ///     This is so its more intuitive (having percentile ap ruins everyone). You can however disable it
        /// </summary>
        [DataField("disableCrossInteraction")]
        public bool disableCrossInteraction = false;

        /// <summary>
        ///     Returns a sum of the damage values.
        /// </summary>
        /// <remarks>
        ///     Note that this being zero does not mean this damage has no effect. Healing in one type may cancel damage
        ///     in another. Consider using <see cref="AnyPositive"/> or <see cref="Empty"/> instead.
        /// </remarks>
        public FixedPoint2 GetTotal()
        {
            var total = FixedPoint2.Zero;
            foreach (var value in DamageDict.Values)
            {
                total += value;
            }
            return total;
        }

        /// <summary>
        /// Returns true if the specifier contains any positive damage values.
        /// Differs from <see cref="Empty"/> as a damage specifier might contain entries with zeroes.
        /// This also returns false if the specifier only contains negative values.
        /// </summary>
        public bool AnyPositive()
        {
            foreach (var value in DamageDict.Values)
            {
                if (value > FixedPoint2.Zero)
                    return true;
            }

            return false;
        }

        /// <summary>
        ///     Whether this damage specifier has any entries.
        /// </summary>
        [JsonIgnore]
        public bool Empty => DamageDict.Count == 0;

        public override string ToString()
        {
            return "DamageSpecifier(" + string.Join("; ", DamageDict.Select(x => x.Key + ":" + x.Value)) + ")";
        }

        #region constructors
        /// <summary>
        ///     Constructor that just results in an empty dictionary.
        /// </summary>
        public DamageSpecifier() { }

        /// <summary>
        ///     Constructor that takes another DamageSpecifier instance and copies it.
        /// </summary>
        public DamageSpecifier(DamageSpecifier damageSpec)
        {
            DamageDict = new(damageSpec.DamageDict);
            //KS14 start
            if (damageSpec.PercentilePenetration != null)
                PercentilePenetration = new(damageSpec.PercentilePenetration);
            if (damageSpec.FlatPenetration != null)
                FlatPenetration = new(damageSpec.FlatPenetration);
            //KS14 end
        }

        /// <summary>
        ///     Constructor that takes a single damage type prototype and a damage value.
        /// </summary>
        public DamageSpecifier(DamageTypePrototype type, FixedPoint2 value)
        {
            DamageDict = new() { { type.ID, value } };
        }

        /// <summary>
        ///     Constructor that takes a single damage group prototype and a damage value. The value is divided between members of the damage group.
        /// </summary>
        public DamageSpecifier(DamageGroupPrototype group, FixedPoint2 value)
        {
            // Simply distribute evenly (except for rounding).
            // We do this by reducing remaining the # of types and damage every loop.
            var remainingTypes = group.DamageTypes.Count;
            var remainingDamage = value;
            foreach (var damageType in group.DamageTypes)
            {
                var damage = remainingDamage / FixedPoint2.New(remainingTypes);
                DamageDict.Add(damageType, damage);
                remainingDamage -= damage;
                remainingTypes -= 1;
            }
        }
        #endregion constructors

        /// <summary>
        ///     Reduce (or increase) damages by applying a damage modifier set.
        /// </summary>
        /// <remarks>
        ///     Only applies resistance to a damage type if it is dealing damage, not healing.
        ///     This will never convert damage into healing.
        /// </remarks>
        public static DamageSpecifier ApplyModifierSet(DamageSpecifier damageSpec, DamageModifierSet modifierSet)
        {
            // Make a copy of the given data. Don't modify the one passed to this function. I did this before, and weapons became
            // duller as you hit walls. Neat, but not FixedPoint2ended. And confusing, when you realize your fists don't work no
            // more cause they're just bloody stumps.
            DamageSpecifier newDamage = new();
            newDamage.DamageDict.EnsureCapacity(damageSpec.DamageDict.Count);

            //basically all of this is KS modified past this point
            //KS14 START

            // Capture nullable AP dictionaries into locals to satisfy nullable analysis and avoid repeated lookups.
            var percentilePenDict = damageSpec.PercentilePenetration ?? new Dictionary<string, float>();
            var flatPenDict = damageSpec.FlatPenetration ?? new Dictionary<string, float>();

            foreach (var (key, value) in damageSpec.DamageDict)
            {
                if (value == 0)
                    continue;

                if (value < 0)
                {
                    newDamage.DamageDict[key] = value;
                    continue;
                }

                float newValueFlat = value.Float();
                float newValuePercentile = value.Float();

                if (modifierSet.FlatReduction.TryGetValue(key, out var reduction))
                {
                    if (!damageSpec.disableCrossInteraction && percentilePenDict.TryGetValue(key, out var percentileDecreaseOfFlatRes)) // If you have 40% pierce pen it makes every flat resist only 60% of its former status - this is done to be more intuitive as per _uranium's request - KS14
                    {
                        var multiplier = 1 - percentileDecreaseOfFlatRes;
                        reduction *= multiplier;
                    }
                    if (flatPenDict.TryGetValue(key, out var flatPenetrationForDamType))
                    {
                        if (reduction > 0f)
                            reduction = Math.Max(0f, reduction - flatPenetrationForDamType); // dont go into the negatives
                        else
                        {
                            reduction -= flatPenetrationForDamType; //this fixes negative reductions that are supposed to increase damage and lets them stack. truly an unholy combo
                        }
                    }
                    newValueFlat = Math.Max(0f, newValueFlat - reduction); // flat reductions can't heal you
                }

                if (modifierSet.Coefficients.TryGetValue(key, out var coefficient))
                {
                    var percentileReduction = 1-coefficient; //coefficient is how much of the dmg you take. if its .7, that means the actual damage reduction is 30%
                    if (percentilePenDict.TryGetValue(key, out var percentilePenetrationForDamType))
                    {
                        var multiplier = 1 - percentilePenetrationForDamType; //penetration is similarly a coefficient. if its .2, we strip away 8O% of the enemys defense
                        percentileReduction *= multiplier;  //if the enemy reduces 30% of incoming damage, and we punch through half that, that means the result is 15%
                    }
                    newValuePercentile *= (1-percentileReduction); //if we now reduce 15% of incoming damage, that means the damage needs to be multiplied by .85
                    // coefficients can also heal you, e.g. cauterizing bleeding
                }

                if (newValueFlat != 0 || newValuePercentile != 0)
                    newDamage.DamageDict[key] = FixedPoint2.New(Math.Min(newValueFlat, newValuePercentile));
            }
            //KS14 END

            return newDamage;
        }

        /// <summary>
        ///     Reduce (or increase) damages by applying multiple modifier sets.
        /// </summary>
        /// <param name="damageSpec"></param>
        /// <param name="modifierSets"></param>
        /// <returns></returns>
        public static DamageSpecifier ApplyModifierSets(DamageSpecifier damageSpec, IEnumerable<DamageModifierSet> modifierSets)
        {
            bool any = false;
            DamageSpecifier newDamage = damageSpec;
            foreach (var set in modifierSets)
            {
                // This creates a new damageSpec for each modifier when we really onlt need to create one.
                // This is quite inefficient, but hopefully this shouldn't ever be called frequently.
                newDamage = ApplyModifierSet(newDamage, set);
                any = true;
            }

            if (!any)
                newDamage = new DamageSpecifier(damageSpec);

            return newDamage;
        }

        /// <summary>
        /// Returns a new DamageSpecifier that only contains the entries with positive value.
        /// </summary>
        public static DamageSpecifier GetPositive(DamageSpecifier damageSpec)
        {
            DamageSpecifier newDamage = new();

            foreach (var (key, value) in damageSpec.DamageDict)
            {
                if (value > 0)
                    newDamage.DamageDict[key] = value;
            }

            return newDamage;
        }

        /// <summary>
        /// Returns a new DamageSpecifier that only contains the entries with negative value.
        /// </summary>
        public static DamageSpecifier GetNegative(DamageSpecifier damageSpec)
        {
            DamageSpecifier newDamage = new();

            foreach (var (key, value) in damageSpec.DamageDict)
            {
                if (value < 0)
                    newDamage.DamageDict[key] = value;
            }

            return newDamage;
        }

        /// <summary>
        ///     Remove any damage entries with zero damage.
        /// </summary>
        public void TrimZeros()
        {
            foreach (var (key, value) in DamageDict)
            {
                if (value == 0)
                {
                    DamageDict.Remove(key);
                }
            }
        }

        /// <summary>
        ///     Clamps each damage value to be within the given range.
        /// </summary>
        public void Clamp(FixedPoint2 minValue, FixedPoint2 maxValue)
        {
            DebugTools.Assert(minValue < maxValue);
            ClampMax(maxValue);
            ClampMin(minValue);
        }

        /// <summary>
        ///     Sets all damage values to be at least as large as the given number.
        /// </summary>
        /// <remarks>
        ///     Note that this only acts on damage types present in the dictionary. It will not add new damage types.
        /// </remarks>
        public void ClampMin(FixedPoint2 minValue)
        {
            foreach (var (key, value) in DamageDict)
            {
                if (value < minValue)
                {
                    DamageDict[key] = minValue;
                }
            }
        }

        /// <summary>
        ///     Sets all damage values to be at most some number. Note that if a damage type is not present in the
        ///     dictionary, these will not be added.
        /// </summary>
        public void ClampMax(FixedPoint2 maxValue)
        {
            foreach (var (key, value) in DamageDict)
            {
                if (value > maxValue)
                {
                    DamageDict[key] = maxValue;
                }
            }
        }

        /// <summary>
        ///     This adds the damage values of some other <see cref="DamageSpecifier"/> to the current one without
        ///     adding any new damage types.
        /// </summary>
        /// <remarks>
        ///     This is used for <see cref="DamageableComponent"/>s, such that only "supported" damage types are
        ///     actually added to the component. In most other instances, you can just use the addition operator.
        /// </remarks>
        public void ExclusiveAdd(DamageSpecifier other)
        {
            foreach (var (type, value) in other.DamageDict)
            {
                // CollectionsMarshal my beloved.
                if (DamageDict.TryGetValue(type, out var existing))
                {
                    DamageDict[type] = existing + value;
                }
            }
        }

        /// <summary>
        ///     Add up all the damage values for damage types that are members of a given group.
        /// </summary>
        /// <remarks>
        ///     If no members of the group are included in this specifier, returns false.
        /// </remarks>
        public bool TryGetDamageInGroup(DamageGroupPrototype group, out FixedPoint2 total)
        {
            bool containsMemeber = false;
            total = FixedPoint2.Zero;

            foreach (var type in group.DamageTypes)
            {
                if (DamageDict.TryGetValue(type, out var value))
                {
                    total += value;
                    containsMemeber = true;
                }
            }
            return containsMemeber;
        }

        /// <summary>
        ///     Returns a dictionary using <see cref="DamageGroupPrototype.ID"/> keys, with values calculated by adding
        ///     up the values for each damage type in that group
        /// </summary>
        /// <remarks>
        ///     If a damage type is associated with more than one supported damage group, it will contribute to the
        ///     total of each group. If no members of a group are present in this <see cref="DamageSpecifier"/>, the
        ///     group is not included in the resulting dictionary.
        /// </remarks>
        public Dictionary<string, FixedPoint2> GetDamagePerGroup(IPrototypeManager protoManager)
        {
            var dict = new Dictionary<string, FixedPoint2>();
            GetDamagePerGroup(protoManager, dict);
            return dict;
        }

        /// <inheritdoc cref="GetDamagePerGroup(Robust.Shared.Prototypes.IPrototypeManager)"/>
        public void GetDamagePerGroup(IPrototypeManager protoManager, Dictionary<string, FixedPoint2> dict)
        {
            dict.Clear();
            foreach (var group in protoManager.EnumeratePrototypes<DamageGroupPrototype>())
            {
                if (TryGetDamageInGroup(group, out var value))
                    dict.Add(group.ID, value);
            }
        }

        #region Operators
        public static DamageSpecifier operator *(DamageSpecifier damageSpec, FixedPoint2 factor)
        {
            //KS14 start
            DamageSpecifier newDamage = new(damageSpec);
            var keys = new List<string>(newDamage.DamageDict.Keys);
            foreach (var key in keys)
            {
                newDamage.DamageDict[key] = newDamage.DamageDict[key] * factor;
            }
            return newDamage;
            //KS14 end
        }

        public static DamageSpecifier operator *(DamageSpecifier damageSpec, float factor)
        {
            //KS14 start
            DamageSpecifier newDamage = new(damageSpec);
            var keys = new List<string>(newDamage.DamageDict.Keys);
            foreach (var key in keys)
            {
                newDamage.DamageDict[key] = newDamage.DamageDict[key] * factor;
            }
            return newDamage;
            //KS14 end
        }

        public static DamageSpecifier operator /(DamageSpecifier damageSpec, FixedPoint2 factor)
        {
            DamageSpecifier newDamage = new();
            foreach (var entry in damageSpec.DamageDict)
            {
                newDamage.DamageDict.Add(entry.Key, entry.Value / factor);
            }
            return newDamage;
        }

        public static DamageSpecifier operator /(DamageSpecifier damageSpec, float factor)
        {
            //KS14 start
            DamageSpecifier newDamage = new(damageSpec);
            var keys = new List<string>(newDamage.DamageDict.Keys);
            foreach (var key in keys)
            {
                newDamage.DamageDict[key] = newDamage.DamageDict[key] / factor;
            }
            return newDamage;
            //KS14 end
        }

        public static DamageSpecifier operator +(DamageSpecifier damageSpecA, DamageSpecifier damageSpecB)
        {
            // Copy existing dictionary from dataA
            DamageSpecifier newDamage = new(damageSpecA);

            // Then just add types in B
            foreach (var entry in damageSpecB.DamageDict)
            {
                if (!newDamage.DamageDict.TryAdd(entry.Key, entry.Value))
                {
                    // Key already exists, add values
                    newDamage.DamageDict[entry.Key] += entry.Value;
                }
            }
            return newDamage;
        }

        // Here we define the subtraction operator explicitly, rather than implicitly via something like X + (-1 * Y).
        // This is faster because FixedPoint2 multiplication is somewhat involved.
        public static DamageSpecifier operator -(DamageSpecifier damageSpecA, DamageSpecifier damageSpecB)
        {
            DamageSpecifier newDamage = new(damageSpecA);

            foreach (var entry in damageSpecB.DamageDict)
            {
                if (!newDamage.DamageDict.TryAdd(entry.Key, -entry.Value))
                {
                    newDamage.DamageDict[entry.Key] -= entry.Value;
                }
            }
            return newDamage;
        }

        public static DamageSpecifier operator +(DamageSpecifier damageSpec) => damageSpec;

        public static DamageSpecifier operator -(DamageSpecifier damageSpec) => damageSpec * -1;

        public static DamageSpecifier operator *(float factor, DamageSpecifier damageSpec) => damageSpec * factor;

        public static DamageSpecifier operator *(FixedPoint2 factor, DamageSpecifier damageSpec) => damageSpec * factor;

        public bool Equals(DamageSpecifier? other)
        {
            if (other == null || DamageDict.Count != other.DamageDict.Count)
                return false;

            foreach (var (key, value) in DamageDict)
            {
                if (!other.DamageDict.TryGetValue(key, out var otherValue) || value != otherValue)
                    return false;
            }

            return true;
        }

        public FixedPoint2 this[string key] => DamageDict[key];
    }
    #endregion
}
