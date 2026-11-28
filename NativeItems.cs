using System;
using System.Linq;
using dc;
using dc.hl.types;
using dc.tool;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod;

internal static class NativeItems
{
    internal static ItemStock Capture(InventItem item)
        => new((int)item.kind.Index, item.getItemKind()?.ToString() ?? "", item.getRawItemLevel(), item.ammo,
            CaptureAffixes(item.affixes), CaptureAffixes(item.inactiveAffixes));

    private static string[] CaptureAffixes(ArrayObj? list)
    {
        if (list is null)
            return [];
        var result = new string[list.length];
        for (var i = 0; i < result.Length; i++)
            result[i] = list.getDyn(i)?.ToString() ?? "";
        return result;
    }

    internal static InventItem? Create(ItemStock state, ItemStock? paired)
    {
        var item = Create(state);
        if (item is null || paired is null)
            return item;
        if (Create(paired) is not { } companion)
            return null;
        item.setPairedItem(companion);
        return item;
    }

    private static InventItem? Create(ItemStock state)
    {
        var id = state.Id.AsHaxeString();
        if (Data.Class.item.byId.get(id) is null ||
            state.Affixes.Concat(state.Inactive).Any(affix => Data.Class.affix.byId.get(affix.AsHaxeString()) is null))
            return null;

        InventItemKind kind = state.Kind switch
        {
            0 => new InventItemKind.Weapon(id),
            1 => new InventItemKind.Active(id),
            2 => new InventItemKind.Talisman(id),
            5 => new InventItemKind.Consumable(id),
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        var item = new InventItem(kind);
        item.setItemLevel(state.Level);
        foreach (var affix in state.Affixes)
            item.affixes.pushDyn(affix.AsHaxeString());
        foreach (var affix in state.Inactive)
            item.inactiveAffixes.pushDyn(affix.AsHaxeString());
        item.ammo = state.Ammo;
        return item;
    }
}
