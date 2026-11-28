using System;
using System.Collections.Generic;
using System.Linq;
using Math = System.Math;
using dc;
using dc.en;
using dc.hl.types;
using dc.pr;
using dc.tool;

namespace DeadCellsMultiplayerMod;

internal sealed class NativeLoot(bool host, Func<bool> inRun, Action<string> log)
{
    private const int MaxPending = 2048;
    private readonly Queue<DropBatch> pending = new();
    private Level? level;
    private int nextSequence;
    private int lastApplied;
    private int lastSent;
    private bool warnedOverflow;
    private bool replaying;

    internal DropFrame? Frame { get; private set; }
    internal DropReceipt? Receipt { get; private set; }

    internal void Install() => Hook_Level.dropMobLoots += OnMobDrops;

    private void OnMobDrops(Hook_Level.orig_dropMobLoots original, Level source, int x, int y, ArrayObj loot,
        double? delay)
    {
        if (replaying || !inRun() || !ReferenceEquals(source, Game.Class.ME?.hero?._level))
        {
            original(source, x, y, loot, delay);
            return;
        }

        SetLevel(source);
        var mirrored = new List<MobDrop>();
        var personal = host ? null : (ArrayObj)loot.slice(0, 0);
        for (var i = 0; i < loot.length; i++)
        {
            var entry = loot.getDyn(i);
            if (entry is LootType native && Capture(native) is { IsValid: true } drop)
                mirrored.Add(drop);
            else if (entry is not null)
                personal?.pushDyn(entry);
        }

        if (host)
        {
            original(source, x, y, loot, delay);
            foreach (var chunk in mirrored.Chunk(DropBatch.MaxDrops))
            {
                if (pending.Count >= MaxPending)
                {
                    if (!warnedOverflow)
                        log("Co-op drop delivery queue is full.");
                    warnedOverflow = true;
                    break;
                }
                var batch = new DropBatch(nextSequence + 1, x, y, delay, chunk);
                if (!batch.IsValid)
                    continue;
                pending.Enqueue(batch);
                nextSequence++;
            }
        }
        else if (personal!.length > 0)
        {
            original(source, x, y, personal, delay);
        }
    }

    internal void Update(Hero? hero, PlayerState? player, PeerState? peer)
    {
        if (hero?._level is not { } current || player is null)
            return;
        SetLevel(current);
        var scope = new LootLevel(player.Level, player.LevelSeed, player.Layout);
        if (host)
        {
            if (peer?.DropReceipt is { } receipt && receipt.Level == scope && scope.Matches(peer.Player) &&
                receipt.Sequence <= lastSent)
                while (pending.TryPeek(out var batch) && batch.Sequence <= receipt.Sequence)
                    pending.Dequeue();

            if (!pending.TryPeek(out var first))
            {
                Frame = null;
                return;
            }
            if (Frame?.Batches[0].Sequence != first.Sequence ||
                Frame.Batches.Length != Math.Min(pending.Count, DropFrame.MaxBatches))
            {
                var batches = pending.Take(DropFrame.MaxBatches).ToArray();
                Frame = new DropFrame(scope, batches);
                lastSent = batches[^1].Sequence;
            }
            return;
        }

        if (peer?.DropFrame is not { } frame || frame.Level != scope || !scope.Matches(peer.Player))
            return;
        foreach (var batch in frame.Batches)
        {
            if (batch.Sequence <= lastApplied)
                continue;
            if (batch.X < current.map.wid && batch.Y < current.map.hei)
            {
                var native = (ArrayObj)current.entities.slice(0, 0);
                foreach (var drop in batch.Drops)
                {
                    if (Restore(drop) is { } restored)
                        native.pushDyn(restored);
                    else
                        log($"Dropped item {drop.Item!.Id} is unavailable on this game installation.");
                }
                if (native.length > 0)
                {
                    // Enter through the original queue so native placement, pickup and timing still run.
                    replaying = true;
                    try { current.dropMobLoots(batch.X, batch.Y, native, batch.Delay); }
                    finally { replaying = false; }
                }
            }
            lastApplied = batch.Sequence;
            Receipt = new DropReceipt(scope, lastApplied);
        }
    }

    private void SetLevel(Level current)
    {
        if (ReferenceEquals(level, current))
            return;
        level = current;
        pending.Clear();
        Frame = null;
        Receipt = null;
        lastSent = 0;
        warnedOverflow = false;
    }

    private static MobDrop? Capture(LootType loot) => loot switch
    {
        LootType.MoneyGold gold => new(DropKind.Gold, gold.Param0),
        LootType.MoneyGem gem => new(DropKind.Gem, gem.Param0),
        LootType.GenericCell => new(DropKind.Cell),
        LootType.GenericCellBonus => new(DropKind.BonusCell),
        LootType.HealDrop heal => new(DropKind.Heal, Heal: heal.Param0),
        LootType.MoneyGoldCombo gold => new(DropKind.ComboGold, gold.Param0),
        LootType.MoneyGemCombo gem => new(DropKind.ComboGem, gem.Param0),
        LootType.InvItem entry when entry.Param0 is { altItem: null, overridedMoneyValue: null } item => CaptureItem(item),
        _ => null
    };

    private static MobDrop? CaptureItem(InventItem item)
    {
        var paired = item.get_pairedItem();
        if (paired is { altItem: not null } or { overridedMoneyValue: not null })
            return null;
        return new MobDrop(DropKind.Item, Item: NativeItems.Capture(item),
            Paired: paired is null ? null : NativeItems.Capture(paired));
    }

    private static LootType? Restore(MobDrop drop) => drop.Kind switch
    {
        DropKind.Gold => new LootType.MoneyGold(drop.Value),
        DropKind.Gem => new LootType.MoneyGem(drop.Value),
        DropKind.Cell => new LootType.GenericCell(),
        DropKind.BonusCell => new LootType.GenericCellBonus(),
        DropKind.Heal => new LootType.HealDrop(drop.Heal),
        DropKind.ComboGold => new LootType.MoneyGoldCombo(drop.Value),
        DropKind.ComboGem => new LootType.MoneyGemCombo(drop.Value),
        DropKind.Item => NativeItems.Create(drop.Item!, drop.Paired) is { } item ? new LootType.InvItem(item) : null,
        _ => throw new ArgumentOutOfRangeException(nameof(drop))
    };
}
