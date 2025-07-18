﻿using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Game.Shop;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Game.Session;

public partial class GameSession {
    private const int ITEM_LOCK_MAX_STAGED_ITEMS = 18;
    public readonly long[] ItemLockStaging = new long[ITEM_LOCK_MAX_STAGED_ITEMS];

    private const int DISMANTLE_MAX_STAGED_ITEMS = 100;
    public bool DismantleOpened = false;
    public readonly (long Uid, int Amount)[] DismantleStaging = new (long, int)[DISMANTLE_MAX_STAGED_ITEMS];

    public Item? StagedUgcItem = null;
    public GuildPoster? StagedGuildPoster = null;

    public Item? StagedInstrumentItem = null;
    public Item? StagedScoreItem = null;
    public bool EnsembleReady = false;

    public Item? ChangeAttributesItem = null;

    public TradeManager? Trade;
    public StorageManager? Storage;

    public PetManager? Pet;
    public FieldInstrument? Instrument;

    public FieldGuideObject? GuideObject;
    public HeldCube? HeldCube;

    public LiftupWeapon? HeldLiftup;

    public NpcScriptManager? NpcScript { get; set; }

    public BeautyShop? BeautyShop;

    public int BonusGameId;

    public MiniGameUserRecord? MiniGameRecord;

    public int SuperChatId;
    public int SuperChatItemId;

    public long ReportCooldown = 0;

    public bool CanHold() {
        return GuideObject == null
               && Ride.Ride == null
               && HeldCube == null;
    }
}
