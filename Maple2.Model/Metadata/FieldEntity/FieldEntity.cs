﻿using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Tools.VectorMath;

namespace Maple2.Model.Metadata.FieldEntity;

public enum FieldEntityType : byte {
    Unknown,
    Vibrate,
    SpawnTile, // for mob spawns
    BoxCollider, // for cube tiles (IsWhiteBox = false) & arbitrarily sized white boxes
    MeshCollider,
    Fluid,
    SellableTile,
    Cell, // for cells culled from the grid & demoted to AABB tree
}

public record FieldEntityId(
    ulong High,
    ulong Low,
    string Id) {
    public bool IsNull { get => High == 0 && Low == 0; }

    public static FieldEntityId FromString(string id) {
        ReadOnlySpan<char> idSpan = id.AsSpan();
        ulong high = ulong.Parse(idSpan.Slice(0, 16), System.Globalization.NumberStyles.HexNumber);
        ulong low = ulong.Parse(idSpan.Slice(16, 16), System.Globalization.NumberStyles.HexNumber);

        return new FieldEntityId(high, low, id);
    }
}

public record FieldEntity(
    FieldEntityId Id,
    Vector3 Position,
    Vector3 Rotation,
    float Scale,
    BoundingBox3 Bounds);

public record FieldVibrateEntity(
    FieldEntityId Id,
    Vector3 Position,
    Vector3 Rotation,
    float Scale,
    BoundingBox3 Bounds,
    int BreakDefense,
    int BreakTick,
    int VibrateIndex) : FieldEntity(Id, Position, Rotation, Scale, Bounds);

public record FieldSpawnTile(
    FieldEntityId Id,
    Vector3 Position,
    Vector3 Rotation,
    float Scale,
    BoundingBox3 Bounds) : FieldEntity(Id, Position, Rotation, Scale, Bounds);

public record FieldBoxColliderEntity(
    FieldEntityId Id,
    Vector3 Position,
    Vector3 Rotation,
    float Scale,
    BoundingBox3 Bounds,
    Vector3 Size,
    bool IsWhiteBox,
    bool IsFluid,
    MapAttribute MapAttribute) : FieldEntity(Id, Position, Rotation, Scale, Bounds);

public record FieldMeshColliderEntity(
    FieldEntityId Id,
    Vector3 Position,
    Vector3 Rotation,
    float Scale,
    BoundingBox3 Bounds,
    uint MeshLlid,
    MapAttribute MapAttribute) : FieldEntity(Id, Position, Rotation, Scale, Bounds);

public record FieldFluidEntity(
    FieldEntityId Id,
    Vector3 Position,
    Vector3 Rotation,
    float Scale,
    LiquidType LiquidType,
    BoundingBox3 Bounds,
    uint MeshLlid,
    bool IsShallow,
    bool IsSurface,
    MapAttribute MapAttribute) : FieldMeshColliderEntity(Id, Position, Rotation, Scale, Bounds, MeshLlid, MapAttribute);

public record FieldCellEntities(
    FieldEntityId Id,
    Vector3 Position,
    Vector3 Rotation,
    float Scale,
    BoundingBox3 Bounds,
    List<FieldEntity> Entities) : FieldEntity(Id, Position, Rotation, Scale, Bounds);

public record FieldSellableTile(
    FieldEntityId Id,
    Vector3 Position,
    Vector3 Rotation,
    float Scale,
    BoundingBox3 Bounds,
    int SellableGroup
) : FieldEntity(Id, Position, Rotation, Scale, Bounds);
