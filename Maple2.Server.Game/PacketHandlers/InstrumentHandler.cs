﻿using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class InstrumentHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.PlayInstrument;

    private enum Command : byte {
        StartImprovise = 0,
        Improvise = 1,
        StopImprovise = 2,
        StartScore = 3,
        StopScore = 4,
        JoinEnsemble = 5,
        LeaveEnsemble = 6,
        ComposeScore = 8,
        ViewScore = 10,
        StartPerform = 11,
        EndPerform = 12,
        Stage = 13,
        Fireworks = 14,
        Emote = 15,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 4)]
    public readonly record struct MidiMessage(byte Unknown, byte Note, short Volume);

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required TableMetadataStorage TableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.StartImprovise:
                HandleStartImprovise(session, packet);
                return;
            case Command.Improvise:
                HandleImprovise(session, packet);
                return;
            case Command.StopImprovise:
                HandleStopImprovise(session);
                return;
            case Command.StartScore:
                HandleStartScore(session, packet);
                return;
            case Command.StopScore:
                HandleStopScore(session);
                return;
            case Command.JoinEnsemble:
                HandleJoinEnsemble(session, packet);
                return;
            case Command.LeaveEnsemble:
                HandleLeaveEnsemble(session);
                return;
            case Command.ComposeScore:
                HandleComposeScore(session, packet);
                return;
            case Command.ViewScore:
                HandleViewScore(session, packet);
                return;
            case Command.StartPerform:
                HandleStartPerform(session);
                return;
            case Command.EndPerform:
                HandleEndPerform(session);
                return;
            case Command.Stage:
                HandleEnterExitStage(session);
                return;
            case Command.Fireworks:
                HandleFireworks(session);
                return;
            case Command.Emote:
                HandleEmote(session, packet);
                return;
        }
    }

    private void HandleStartImprovise(GameSession session, IByteReader packet) {
        if (session.Field == null || session.Instrument != null) {
            return;
        }

        long itemUid = packet.ReadLong();
        if (!TryUseInstrument(session, itemUid, Environment.TickCount64, false, out session.Instrument)) {
            return;
        }

        session.Instrument.Improvising = true;
        session.Field.Broadcast(InstrumentPacket.StartImprovise(session.Instrument));
    }

    private void HandleImprovise(GameSession session, IByteReader packet) {
        if (session.Field == null || session.Instrument?.Improvising != true) {
            return;
        }

        var note = packet.Read<MidiMessage>();
        session.Field.Broadcast(InstrumentPacket.Improvise(session.Instrument, note), session);
    }

    private void HandleStopImprovise(GameSession session) {
        if (session.Field == null || session.Instrument?.Improvising != true) {
            return;
        }

        session.Field.Broadcast(InstrumentPacket.StopImprovise(session.Instrument));
        session.Instrument = null;
    }

    private void HandleStartScore(GameSession session, IByteReader packet) {
        if (session.Field == null || session.Instrument != null) {
            return;
        }

        long itemUid = packet.ReadLong();
        long scoreUid = packet.ReadLong();
        if (!TryGetScore(session, scoreUid, out Item? score)) {
            return;
        }
        if (!TryUseInstrument(session, itemUid, Environment.TickCount64, false, out session.Instrument)) {
            return;
        }
        session.Instrument.Score = score;

        session.ConditionUpdate(ConditionType.music_play_score, codeLong: score.Id, targetLong: session.Field.MapId);

        score.RemainUses--;
        session.Field.Broadcast(InstrumentPacket.StartScore(session.Instrument, score));
        session.Send(InstrumentPacket.RemainUses(score.Uid, score.RemainUses));
    }

    private void HandleStopScore(GameSession session) {
        if (session.Field == null || session.Instrument == null) {
            return;
        }

        int masteryValue = session.Instrument.Score?.Metadata.Music?.MasteryValue ?? 1;
        int masteryValueMax = session.Instrument.Score?.Metadata.Music?.MasteryValueMax ?? 1;

        // TODO: Prestige exp
        long totalTickTime = Environment.TickCount64 - session.Instrument.StartTick;
        session.Mastery[MasteryType.Music] += (int) Math.Min(totalTickTime * masteryValue / 1000, masteryValueMax);

        short masteryLevel = session.Mastery.GetLevel(MasteryType.Music);
        ExpType expType = masteryLevel switch {
            1 => ExpType.musicMastery1,
            2 => ExpType.musicMastery2,
            3 => ExpType.musicMastery3,
            4 => ExpType.musicMastery4,
            _ => ExpType.musicMastery1,
        };

        // Subtracting 500ms to account for the delay between the client and server and exp abuse.
        // Modifier is the number of 500ms ticks that have passed.
        session.Exp.AddExp(expType, (float) (totalTickTime - 500) / 500);

        session.ConditionUpdate(ConditionType.music_play_instrument_time, counter: totalTickTime / 1000, targetLong: session.Field.MapId, codeLong: session.Instrument.Value.Id);
        if (session.Instrument.Ensemble) {
            session.ConditionUpdate(ConditionType.play_ensenble_time, counter: totalTickTime / 1000, targetLong: session.Field.MapId);
        }

        session.Field.RemoveInstrument(session.Instrument.ObjectId);
        session.Instrument = null;
    }

    private void HandleJoinEnsemble(GameSession session, IByteReader packet) {
        if (session.Field == null || session.Instrument != null || session.Party.Party == null) {
            return;
        }

        long itemUid = packet.ReadLong();
        long scoreUid = packet.ReadLong();
        if (!TryGetScore(session, scoreUid, out Item? score)) {
            return;
        }

        session.EnsembleReady = true;
        session.StagedInstrumentItem = session.Item.Inventory.Get(itemUid, InventoryType.FishingMusic);
        session.StagedScoreItem = score;

        if (session.Party.Party.LeaderCharacterId != session.CharacterId) {
            return;
        }

        // Store tick to ensure all members instruments have the same start tick
        long startTick = Environment.TickCount64;
        foreach ((long characterId, PartyMember member) in session.Party.Party.Members) {
            if (!session.FindSession(characterId, out GameSession? memberSession) ||
                memberSession.Field == null ||
                !memberSession.EnsembleReady ||
                memberSession.StagedInstrumentItem == null ||
                memberSession.StagedScoreItem == null ||
                memberSession.Player.Value.Character.MapId != session.Player.Value.Character.MapId ||
                memberSession.Player.Value.Character.RoomId != session.Player.Value.Character.RoomId) {
                continue;
            }

            if (!TryUseInstrument(memberSession, memberSession.StagedInstrumentItem.Uid, startTick, true, out memberSession.Instrument)) {
                continue;
            }

            memberSession.StagedScoreItem.RemainUses--;
            memberSession.Field.Broadcast(InstrumentPacket.StartScore(memberSession.Instrument, memberSession.StagedScoreItem));
            memberSession.Send(InstrumentPacket.RemainUses(memberSession.StagedScoreItem.Uid, memberSession.StagedScoreItem.RemainUses));
            memberSession.ConditionUpdate(ConditionType.music_play_ensemble);
            memberSession.ConditionUpdate(ConditionType.music_play_ensemble_in, codeLong: memberSession.Field.MapId, targetLong: memberSession.Field.MapId);
        }
    }

    private void HandleLeaveEnsemble(GameSession session) {
        if (session.Field == null) {
            return;
        }

        session.EnsembleReady = false;
        session.StagedInstrumentItem = null;
        session.StagedScoreItem = null;
        session.Send(InstrumentPacket.LeaveEnsemble());
    }

    private void HandleComposeScore(GameSession session, IByteReader packet) {
        long scoreUid = packet.ReadLong();
        if (!TryGetScore(session, scoreUid, out Item? score) || score.Music == null) {
            return;
        }

        if (score.Music.AuthorId != 0) {
            Logger.Warning("CustomMusicScore {Uid} has already been composed", score.Uid);
            return;
        }

        int length = packet.ReadInt();
        var instrument = packet.Read<Instrument>();
        string title = packet.ReadUnicodeString();
        string mml = packet.ReadString();

        score.Music.Length = length;
        score.Music.Instrument = instrument;
        score.Music.Title = title;
        score.Music.Author = session.PlayerName;
        score.Music.AuthorId = session.AccountId;
        score.Music.Mml = mml;

        session.Send(InstrumentPacket.ComposeScore(score));
    }

    private void HandleViewScore(GameSession session, IByteReader packet) {
        long scoreUid = packet.ReadLong();
        Item? score = session.Item.Inventory.Get(scoreUid, InventoryType.FishingMusic);
        if (score?.Music == null || score.Music.AuthorId == 0) {
            return;
        }

        session.Send(InstrumentPacket.ViewScore(score.Uid, score.Music.Mml));
    }

    private void HandleStartPerform(GameSession session) {
        if (session.Field?.PerformanceStage is null) {
            return;
        }
    }

    private void HandleEndPerform(GameSession session) {
        if (session.Field?.PerformanceStage is null) {
            return;
        }
    }

    private void HandleEnterExitStage(GameSession session) => session.Field?.PerformanceStage?.EnterExitStage(session);

    private void HandleFireworks(GameSession session) {
        if (session.Field?.PerformanceStage is null) {
            return;
        }

        session.Send(InstrumentPacket.Fireworks(session.Player.ObjectId));
    }

    private void HandleEmote(GameSession session, IByteReader packet) {
        if (session.Field?.MapId != Constant.PerformanceMapId) {
            return;
        }

        int skillId = packet.ReadInt();
        switch (skillId) {
            case 90210001: // Applaud
                break;
            case 90210002: // Glowstick
                break;
        }
    }

    private bool TryUseInstrument(GameSession session, long itemUid, long startTick, bool ensemble, [NotNullWhen(true)] out FieldInstrument? fieldInstrument) {
        Item? instrument = session.Item.Inventory.Get(itemUid, InventoryType.FishingMusic);
        if (instrument == null || instrument.Metadata.Function?.Type != ItemFunction.OpenInstrument || session.Field is null) {
            fieldInstrument = null;
            return false;
        }

        if (!int.TryParse(instrument.Metadata.Function.Parameters, out int instrumentId)) {
            Logger.Warning("Invalid parameters for OpenInstrument:{Params}", instrument.Metadata.Function.Parameters);
            fieldInstrument = null;
            return false;
        }

        if (!TableMetadata.InstrumentTable.Entries.TryGetValue(instrumentId, out InstrumentMetadata? metadata)) {
            fieldInstrument = null;
            return false;
        }

        fieldInstrument = session.Field.SpawnInstrument(session.Player, metadata);
        fieldInstrument.StartTick = startTick;
        fieldInstrument.Ensemble = ensemble;
        return true;
    }

    private bool TryGetScore(GameSession session, long scoreUid, [NotNullWhen(true)] out Item? score) {
        score = session.Item.Inventory.Get(scoreUid, InventoryType.FishingMusic);
        if (score is not { RemainUses: > 0 } || score.IsExpired()) {
            return false;
        }

        return true;
    }
}
