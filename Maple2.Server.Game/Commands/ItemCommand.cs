﻿using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Commands;

public class ItemCommand : GameCommand {
    private const string NAME = "item";
    private const string DESCRIPTION = "Item spawning.";
    public const AdminPermissions RequiredPermission = AdminPermissions.SpawnItem;

    private const int MAX_RARITY = 6;
    private const int MAX_SOCKET = 5;

    private readonly GameSession session;
    private readonly ItemMetadataStorage itemStorage;

    public ItemCommand(GameSession session, ItemMetadataStorage itemStorage) : base(RequiredPermission, NAME, DESCRIPTION) {
        this.session = session;
        this.itemStorage = itemStorage;

        var id = new Argument<int>("id", "Id of item to spawn.");
        var amount = new Option<int>(["--amount", "-a"], () => 1, "Amount of the item.");
        var rarity = new Option<int>(["--rarity", "-r"], () => 1, "Rarity of the item.");
        var drop = new Option<bool>(["--drop"], "Drop item instead of adding to inventory");
        var rollMax = new Option<bool>(["--roll-max"], () => false, "Max roll stats.");

        AddArgument(id);
        AddOption(amount);
        AddOption(rarity);
        AddOption(drop);
        AddOption(rollMax);
        this.SetHandler<InvocationContext, int, int, int, bool, bool>(Handle, id, amount, rarity, drop, rollMax);
    }

    private void Handle(InvocationContext ctx, int itemId, int amount, int rarity, bool drop, bool rollMax) {
        try {
            rarity = Math.Clamp(rarity, 1, MAX_RARITY);
            Item? item = session.Field.ItemDrop.CreateItem(itemId, rarity, rollMax: rollMax);
            if (item == null) {
                ctx.Console.Error.WriteLine($"Invalid Item: {itemId}");
                return;
            }

            if (!item.IsCurrency()) {
                if (item.Metadata.Property.SlotMax == 0) {
                    ctx.Console.Error.WriteLine($"{itemId} has SlotMax of 0, ignoring...");
                    amount = Math.Clamp(amount, 1, int.MaxValue);
                }
            }
            item.Amount = amount;

            if (drop && session.Field != null) {
                FieldItem fieldItem = session.Field.SpawnItem(session.Player, item);
                session.Field.Broadcast(FieldPacket.DropItem(fieldItem));
            } else if (!session.Item.Inventory.Add(item, true)) {
                session.Item.Inventory.Discard(item);
                ctx.Console.Error.WriteLine($"Failed to add item:{item.Id} to inventory");
                ctx.ExitCode = 1;
                return;
            }

            ctx.ExitCode = 0;
        } catch (SystemException ex) {
            ctx.Console.Error.WriteLine(ex.Message);
            ctx.ExitCode = 1;
        }
    }
}
