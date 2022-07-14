﻿using System.Collections.Generic;
using System.Numerics;
using Maple2.Model.Enum;

namespace Maple2.Model.Game;

public class DamageRecord {
    public long Uid { get; init; }
    public int AttackCounter;
    public int CasterId;
    public int OwnerId;
    public int SkillId;
    public short Level;
    public byte MotionPoint;
    public byte AttackPoint;

    public Vector3 Position;
    public Vector3 Rotation;
    public Vector3 Direction;

    public List<DamageRecordTarget> Targets;

    public DamageRecord() {
        Targets = new List<DamageRecordTarget>();
    }
}

public class DamageRecordTarget {
    public int ObjectId { get; init; }
    public Vector3 Position;
    public Vector3 Direction;

    private readonly List<(DamageType, long)> damage;
    public IReadOnlyList<(DamageType Type, long Amount)> Damage => damage;

    public DamageRecordTarget() {
        damage = new List<(DamageType Type, long Amount)>();
    }

    public void AddDamage(DamageType type, long amount) {
        damage.Add((type, amount));
    }
}
