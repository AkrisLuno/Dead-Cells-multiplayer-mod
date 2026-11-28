using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Math = System.Math;
using dc;
using dc.en;
using dc.en.inter;
using dc.level;
using dc.pr;
using dc.tool;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod;

internal sealed class NativeShops(bool host, Func<bool> connected, Action<string> log)
{
    private readonly Dictionary<ShopKey, ShopStock> stocks = [];
    private readonly Dictionary<ShopKey, int> applied = [];
    private Level? level;
    private ShopStock[]? outgoingStocks;
    private long requestTime;
    private RerollRequest? handledRequest;

    internal RerollRequest? Request { get; private set; }
    internal ShopStock[]? Stocks => outgoingStocks;

    internal void Install() => Hook_ShopReroll.onActivate += OnActivate;

    private void OnActivate(Hook_ShopReroll.orig_onActivate original, ShopReroll shop, Hero hero, bool local)
    {
        if (!local || !ReferenceEquals(hero, Game.Class.ME?.hero) || shop._level is not { } current ||
            GetKey(current, shop) is not { } key || !ReferenceEquals(current, hero._level))
        {
            original(shop, hero, local);
            return;
        }

        SetLevel(current);
        if (shop.destroyed || shop.remainUses <= 0 ||
            current.map.getRoomAt(key.X, key.Y)?.merchant is not { } merchant)
            return;
        if (!CanSyncMerchant(merchant))
        {
            shop.popError("This shop cannot reroll during co-op".AsHaxeString(), null);
            return;
        }
        if (!connected())
        {
            shop.popError("Waiting for your co-op partner".AsHaxeString(), null);
            return;
        }

        if (host)
            Roll(shop, key, () => original(shop, hero, true));
        else
        {
            Request = new RerollRequest(key, applied.GetValueOrDefault(key));
            requestTime = Stopwatch.GetTimestamp();
        }
    }

    internal void Update(Hero? hero, PeerState? peer)
    {
        SetLevel(hero?._level);
        if (level?.map is not { } map || hero is null)
            return;

        if (host)
        {
            if (peer?.Reroll is null)
                handledRequest = null;
            if (peer?.Reroll is not { } request || peer.Player is not { } player ||
                handledRequest == request ||
                map.id.ToString() != request.Shop.Level || map.seed != request.Shop.Seed ||
                player.Level != request.Shop.Level || player.LevelSeed != request.Shop.Seed ||
                Math.Abs(player.X / 24 - request.Shop.X) > 3 || Math.Abs(player.Y / 24 - request.Shop.Y) > 3 ||
                (stocks.GetValueOrDefault(request.Shop)?.Revision ?? 0) != request.KnownRevision)
                return;

            if (FindShop(level, request.Shop) is { } shop)
            {
                handledRequest = request;
                shop.onActivate(hero, true);
            }
            return;
        }

        if (Request is not null && Stopwatch.GetElapsedTime(requestTime).TotalSeconds >= 4)
            Request = null;
        if (peer?.ShopStocks is not { } snapshots)
            return;

        foreach (var stock in snapshots)
        {
            if (stock.Shop.Level != map.id.ToString() || stock.Shop.Seed != map.seed ||
                applied.GetValueOrDefault(stock.Shop) >= stock.Revision)
                continue;
            if (!ApplyStock(level, stock))
                continue;
            applied[stock.Shop] = stock.Revision;
            if (Request?.Shop == stock.Shop)
                Request = null;
            log($"Shop stock applied room={stock.Shop.Room} revision={stock.Revision} uses={stock.Uses} booths={stock.Booths.Length}.");
        }
    }

    private void SetLevel(Level? current)
    {
        if (ReferenceEquals(level, current))
            return;
        level = current;
        stocks.Clear();
        applied.Clear();
        Request = null;
        handledRequest = null;
        outgoingStocks = null;
    }

    private void Roll(ShopReroll shop, ShopKey key, Action activate)
    {
        if (level is not { } current || !stocks.ContainsKey(key) && stocks.Count == ShopStock.MaxShops)
            return;
        var uses = shop.remainUses;
        activate();
        if (uses == shop.remainUses && !shop.destroyed)
            return;

        var booths = CaptureBooths(current, key.Room);
        var stock = new ShopStock(key, (stocks.GetValueOrDefault(key)?.Revision ?? 0) + 1,
            shop.remainUses, shop.destroyed, booths);
        if (!stock.IsValid)
        {
            log($"Unsupported shop stock in room {key.Room}.");
            return;
        }

        stocks[key] = stock;
        outgoingStocks = stocks.Values.ToArray();
        log($"Shop rerolled room={key.Room} revision={stock.Revision} uses={stock.Uses} booths={booths.Length}.");
    }

    private static bool ApplyStock(Level current, ShopStock stock)
    {
        var pedestal = FindShop(current, stock.Shop);
        if (pedestal is null || current.map.getRoomAt(stock.Shop.X, stock.Shop.Y)?.merchant is not { isMimic: false })
            return false;

        var items = new InventItem[stock.Booths.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var booth = stock.Booths[i];
            if (current.map.getRoomAt(booth.X, booth.Y)?.id != stock.Shop.Room ||
                NativeItems.Create(booth.Item, booth.Paired) is not { } item)
                return false;
            items[i] = item;
        }

        foreach (var booth in GetBooths(current, stock.Shop.Room))
            booth.destroy();
        for (var i = 0; i < items.Length; i++)
        {
            var booth = stock.Booths[i];
            new ShopBooth(current, booth.X, booth.Y, items[i], booth.Price).init();
        }
        pedestal.remainUses = stock.Uses;
        if (stock.Removed)
            pedestal.destroyWithFx();
        return true;
    }

    private static bool CanSyncMerchant(MerchantData merchant)
        => !merchant.isMimic && merchant.type is MerchantType.Weapons or MerchantType.Actives or MerchantType.Talismans;

    internal static ShopKey? GetKey(Level current, ShopReroll shop)
        => current.map is { } map && map.getRoomAt(shop.cx, shop.cy) is { } room
            ? new ShopKey(map.id.ToString(), map.seed, room.id, shop.cx, shop.cy)
            : null;

    private static ShopReroll? FindShop(Level current, ShopKey key)
    {
        var entities = current.entities;
        for (var i = 0; i < entities.length; i++)
            if (entities.getDyn(i) is ShopReroll { destroyed: false } shop && shop.cx == key.X &&
                shop.cy == key.Y && GetKey(current, shop) == key)
                return shop;
        return null;
    }

    internal static List<ShopBooth> GetBooths(Level current, int roomId)
    {
        var result = new List<ShopBooth>();
        var entities = current.entities;
        for (var i = 0; i < entities.length; i++)
            if (entities.getDyn(i) is ShopBooth { destroyed: false } booth &&
                current.map.getRoomAt(booth.cx, booth.cy)?.id == roomId)
                result.Add(booth);
        return result;
    }

    internal static BoothStock[] CaptureBooths(Level current, int roomId)
        => GetBooths(current, roomId).OrderBy(booth => booth.cx).Select(booth =>
            new BoothStock(booth.cx, booth.cy, booth.price, NativeItems.Capture(booth.item),
                booth.item.get_pairedItem() is { } paired ? NativeItems.Capture(paired) : null)).ToArray();
}
