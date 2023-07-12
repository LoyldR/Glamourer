﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Dalamud.Data;
using Dalamud.Interface;
using Glamourer.Designs;
using Glamourer.Services;
using Glamourer.Structs;
using ImGuiNET;
using OtterGui;
using OtterGui.Raii;
using OtterGui.Widgets;
using Penumbra.GameData.Data;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;

namespace Glamourer.Gui.Equipment;

public class EquipmentDrawer
{
    private readonly ItemManager                            _items;
    private readonly FilterComboColors                      _stainCombo;
    private readonly StainData                              _stainData;
    private readonly ItemCombo[]                            _itemCombo;
    private readonly Dictionary<FullEquipType, WeaponCombo> _weaponCombo;
    private readonly CodeService                            _codes;
    private readonly TextureService                         _textures;

    public EquipmentDrawer(DataManager gameData, ItemManager items, CodeService codes, TextureService textures)
    {
        _items     = items;
        _codes     = codes;
        _textures  = textures;
        _stainData = items.Stains;
        _stainCombo = new FilterComboColors(280,
            _stainData.Data.Prepend(new KeyValuePair<byte, (string Name, uint Dye, bool Gloss)>(0, ("None", 0, false))));
        _itemCombo   = EquipSlotExtensions.EqdpSlots.Select(e => new ItemCombo(gameData, items, e, textures)).ToArray();
        _weaponCombo = new Dictionary<FullEquipType, WeaponCombo>(FullEquipTypeExtensions.WeaponTypes.Count * 2);
        foreach (var type in Enum.GetValues<FullEquipType>())
        {
            if (type.ToSlot() is EquipSlot.MainHand)
                _weaponCombo.TryAdd(type, new WeaponCombo(items, type));
            else if (type.ToSlot() is EquipSlot.OffHand)
                _weaponCombo.TryAdd(type, new WeaponCombo(items, type));
        }

        _weaponCombo.Add(FullEquipType.Unknown, new WeaponCombo(items, FullEquipType.Unknown));
    }

    private Vector2 _iconSize;
    private float   _comboLength;

    public void Prepare()
    {
        _iconSize    = new Vector2(2 * ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y);
        _comboLength = 300 * ImGuiHelpers.GlobalScale;
    }

    private bool VerifyRestrictedGear(EquipSlot slot, EquipItem gear, Gender gender, Race race)
    {
        if (slot.IsAccessory())
            return false;

        var (changed, _) = _items.ResolveRestrictedGear(gear.Armor(), slot, race, gender);
        return changed;
    }

    [Flags]
    public enum EquipChange : byte
    {
        None        = 0x00,
        Item        = 0x01,
        Stain       = 0x02,
        ApplyItem   = 0x04,
        ApplyStain  = 0x08,
        Item2       = 0x10,
        Stain2      = 0x20,
        ApplyItem2  = 0x40,
        ApplyStain2 = 0x80,
    }

    public EquipChange DrawEquip(EquipSlot slot, in DesignData designData, out EquipItem rArmor, out StainId rStain, EquipFlag? cApply,
        out bool rApply, out bool rApplyStain, bool locked)
        => DrawEquip(slot, designData.Item(slot), out rArmor, designData.Stain(slot), out rStain, cApply, out rApply, out rApplyStain, locked,
            designData.Customize.Gender, designData.Customize.Race);

    public EquipChange DrawEquip(EquipSlot slot, EquipItem cArmor, out EquipItem rArmor, StainId cStain, out StainId rStain, EquipFlag? cApply,
        out bool rApply, out bool rApplyStain, bool locked, Gender gender = Gender.Unknown, Race race = Race.Unknown)
    {
        if (!locked && _codes.EnabledArtisan)
            return DrawEquipArtisan(slot, cArmor, out rArmor, cStain, out rStain, cApply, out rApply, out rApplyStain);

        var       spacing = ImGui.GetStyle().ItemInnerSpacing with { Y = ImGui.GetStyle().ItemSpacing.Y };
        using var style   = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, spacing);

        var changes = EquipChange.None;
        cArmor.DrawIcon(_textures, _iconSize);
        ImGui.SameLine();
        using var group = ImRaii.Group();
        if (DrawItem(slot, cArmor, out rArmor, out var label, locked))
            changes |= EquipChange.Item;
        if (cApply.HasValue)
        {
            ImGui.SameLine();
            if (DrawApply(slot, cApply.Value, out rApply, locked))
                changes |= EquipChange.ApplyItem;
        }
        else
        {
            rApply = true;
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(label);
        if (DrawStain(slot, cStain, out rStain, locked))
            changes |= EquipChange.Stain;
        if (cApply.HasValue)
        {
            ImGui.SameLine();
            if (DrawApplyStain(slot, cApply.Value, out rApplyStain, locked))
                changes |= EquipChange.ApplyStain;
        }
        else
        {
            rApplyStain = true;
        }

        if (VerifyRestrictedGear(slot, rArmor, gender, race))
        {
            ImGui.SameLine();
            ImGui.TextUnformatted("(Restricted)");
        }

        return changes;
    }

    public EquipChange DrawWeapons(in DesignData designData, out EquipItem rMainhand, out EquipItem rOffhand, out StainId rMainhandStain,
        out StainId rOffhandStain, EquipFlag? cApply, out bool rApplyMainhand, out bool rApplyMainhandStain, out bool rApplyOffhand,
        out bool rApplyOffhandStain, bool locked)
        => DrawWeapons(designData.Item(EquipSlot.MainHand), out rMainhand, designData.Item(EquipSlot.OffHand), out rOffhand,
            designData.Stain(EquipSlot.MainHand),           out rMainhandStain, designData.Stain(EquipSlot.OffHand), out rOffhandStain, cApply,
            out rApplyMainhand,                             out rApplyMainhandStain, out rApplyOffhand, out rApplyOffhandStain, locked);

    public EquipChange DrawWeapons(EquipItem cMainhand, out EquipItem rMainhand, EquipItem cOffhand, out EquipItem rOffhand,
        StainId cMainhandStain, out StainId rMainhandStain, StainId cOffhandStain, out StainId rOffhandStain, EquipFlag? cApply,
        out bool rApplyMainhand, out bool rApplyMainhandStain, out bool rApplyOffhand, out bool rApplyOffhandStain, bool locked)
    {
        var changes = EquipChange.None;

        using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing,
            ImGui.GetStyle().ItemInnerSpacing with { Y = ImGui.GetStyle().ItemSpacing.Y });

        cMainhand.DrawIcon(_textures, _iconSize);
        ImGui.SameLine();
        using (var group = ImRaii.Group())
        {
            rOffhand = cOffhand;
            if (DrawMainhand(cMainhand, cApply.HasValue, out rMainhand, out var mainhandLabel, locked))
            {
                changes |= EquipChange.Item;
                if (rMainhand.Type.ValidOffhand() != cMainhand.Type.ValidOffhand())
                {
                    rOffhand =  _items.GetDefaultOffhand(rMainhand);
                    changes  |= EquipChange.Item2;
                }
            }

            if (cApply.HasValue)
            {
                ImGui.SameLine();
                if (DrawApply(EquipSlot.MainHand, cApply.Value, out rApplyMainhand, locked))
                    changes |= EquipChange.ApplyItem;
            }
            else
            {
                rApplyMainhand = true;
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(mainhandLabel);

            if (DrawStain(EquipSlot.MainHand, cMainhandStain, out rMainhandStain, locked))
                changes |= EquipChange.Stain;
            if (cApply.HasValue)
            {
                ImGui.SameLine();
                if (DrawApplyStain(EquipSlot.MainHand, cApply.Value, out rApplyMainhandStain, locked))
                    changes |= EquipChange.ApplyStain;
            }
            else
            {
                rApplyMainhandStain = true;
            }
        }

        if (rOffhand.Type is FullEquipType.Unknown)
        {
            rOffhandStain      = cOffhandStain;
            rApplyOffhand      = false;
            rApplyOffhandStain = false;
            return changes;
        }

        rOffhand.DrawIcon(_textures, _iconSize);
        ImGui.SameLine();
        using (var group = ImRaii.Group())
        {
            if (DrawOffhand(rMainhand, rOffhand, out rOffhand, out var offhandLabel, locked))
                changes |= EquipChange.Item2;
            if (cApply.HasValue)
            {
                ImGui.SameLine();
                if (DrawApply(EquipSlot.OffHand, cApply.Value, out rApplyOffhand, locked))
                    changes |= EquipChange.ApplyItem2;
            }
            else
            {
                rApplyOffhand = true;
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(offhandLabel);

            if (DrawStain(EquipSlot.OffHand, cOffhandStain, out rOffhandStain, locked))
                changes |= EquipChange.Stain2;
            if (cApply.HasValue)
            {
                ImGui.SameLine();
                if (DrawApplyStain(EquipSlot.OffHand, cApply.Value, out rApplyOffhandStain, locked))
                    changes |= EquipChange.ApplyStain2;
            }
            else
            {
                rApplyOffhandStain = true;
            }
        }

        return changes;
    }


    public bool DrawMainhand(EquipItem current, bool drawAll, out EquipItem weapon, out string label, bool locked)
    {
        weapon = current;
        if (!_weaponCombo.TryGetValue(drawAll ? FullEquipType.Unknown : current.Type, out var combo))
        {
            label = string.Empty;
            return false;
        }

        label = combo.Label;
        using var disabled = ImRaii.Disabled(locked);
        if (!combo.Draw(weapon.Name, weapon.ItemId, _comboLength))
            return false;

        weapon = combo.CurrentSelection;
        return true;
    }

    public bool DrawOffhand(EquipItem mainhand, EquipItem current, out EquipItem weapon, out string label, bool locked)
    {
        weapon = current;
        if (!_weaponCombo.TryGetValue(current.Type, out var combo))
        {
            label = string.Empty;
            return false;
        }

        label = combo.Label;
        using var disabled = ImRaii.Disabled(locked);
        var       change   = combo.Draw(weapon.Name, weapon.ItemId, _comboLength);
        if (change)
            weapon = combo.CurrentSelection;

        if (!locked)
        {
            var defaultOffhand = _items.GetDefaultOffhand(mainhand);
            if (defaultOffhand.Id != weapon.Id)
            {
                ImGuiUtil.HoverTooltip("Right-click to set to Default.");
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    change = true;
                    weapon = defaultOffhand;
                }
            }
        }

        return change;
    }

    public bool DrawApply(EquipSlot slot, EquipFlag flags, out bool enabled, bool locked)
        => UiHelpers.DrawCheckbox($"##apply{slot}", "Apply this item when applying the Design.", flags.HasFlag(slot.ToFlag()), out enabled,
            locked);

    public bool DrawApplyStain(EquipSlot slot, EquipFlag flags, out bool enabled, bool locked)
        => UiHelpers.DrawCheckbox($"##applyStain{slot}", "Apply this dye when applying the Design.", flags.HasFlag(slot.ToStainFlag()),
            out enabled, locked);

    private bool DrawItem(EquipSlot slot, EquipItem current, out EquipItem armor, out string label, bool locked)
    {
        Debug.Assert(slot.IsEquipment() || slot.IsAccessory(), $"Called {nameof(DrawItem)} on {slot}.");
        var combo = _itemCombo[slot.ToIndex()];
        label = combo.Label;
        armor = current;
        using var disabled = ImRaii.Disabled(locked);
        var       change   = combo.Draw(armor.Name, armor.ItemId, _comboLength);
        if (change)
            armor = combo.CurrentSelection;

        if (!locked && armor.ModelId.Value != 0)
        {
            ImGuiUtil.HoverTooltip("Right-click to clear.");
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                change = true;
                armor  = ItemManager.NothingItem(slot);
            }
        }

        return change;
    }

    private bool DrawStain(EquipSlot slot, StainId current, out StainId ret, bool locked)
    {
        var       found    = _stainData.TryGetValue(current, out var stain);
        using var disabled = ImRaii.Disabled(locked);
        var       change   = _stainCombo.Draw($"##stain{slot}", stain.RgbaColor, stain.Name, found, stain.Gloss, _comboLength);
        ret = current;
        if (change && _stainData.TryGetValue(_stainCombo.CurrentSelection.Key, out stain))
            ret = stain.RowIndex;

        if (!locked && ret != Stain.None.RowIndex)
        {
            ImGuiUtil.HoverTooltip("Right-click to clear.");
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                ret    = Stain.None.RowIndex;
                change = true;
            }
        }

        return change;
    }

    /// <summary> Draw an input for armor that can set arbitrary values instead of choosing items. </summary>
    private bool DrawArmorArtisan(EquipSlot slot, EquipItem current, out EquipItem armor)
    {
        int setId   = current.ModelId.Value;
        int variant = current.Variant;
        var ret     = false;
        armor = current;
        ImGui.SetNextItemWidth(80 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("##setId", ref setId, 0, 0))
        {
            var newSetId = (SetId)Math.Clamp(setId, 0, ushort.MaxValue);
            if (newSetId.Value != current.ModelId.Value)
            {
                armor = _items.Identify(slot, newSetId, current.Variant);
                ret   = true;
            }
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(40 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("##variant", ref variant, 0, 0))
        {
            var newVariant = (byte)Math.Clamp(variant, 0, byte.MaxValue);
            if (newVariant != current.Variant)
            {
                armor = _items.Identify(slot, current.ModelId, newVariant);
                ret   = true;
            }
        }

        return ret;
    }

    /// <summary> Draw an input for stain that can set arbitrary values instead of choosing valid stains. </summary>
    private bool DrawStainArtisan(EquipSlot slot, StainId current, out StainId stain)
    {
        int stainId = current.Value;
        ImGui.SetNextItemWidth(40 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("##stain", ref stainId, 0, 0))
        {
            var newStainId = (StainId)Math.Clamp(stainId, 0, byte.MaxValue);
            if (newStainId != current)
            {
                stain = newStainId;
                return true;
            }
        }

        stain = current;
        return false;
    }

    private EquipChange DrawEquipArtisan(EquipSlot slot, EquipItem cArmor, out EquipItem rArmor, StainId cStain, out StainId rStain,
        EquipFlag? cApply, out bool rApply, out bool rApplyStain)
    {
        var changes = EquipChange.None;
        if (DrawStainArtisan(slot, cStain, out rStain))
            changes |= EquipChange.Stain;
        ImGui.SameLine();
        if (DrawArmorArtisan(slot, cArmor, out rArmor))
            changes |= EquipChange.Item;
        if (cApply.HasValue)
        {
            ImGui.SameLine();
            if (DrawApply(slot, cApply.Value, out rApply, false))
                changes |= EquipChange.ApplyItem;
            ImGui.SameLine();
            if (DrawApplyStain(slot, cApply.Value, out rApplyStain, false))
                changes |= EquipChange.ApplyStain;
        }
        else
        {
            rApply      = false;
            rApplyStain = false;
        }

        return changes;
    }
}
