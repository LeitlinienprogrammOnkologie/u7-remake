namespace U7.Data;

public enum ObjectKind
{
    TerrainRle,
    Ifix,
    Ireg,
    Actor
}

/// <summary>
/// A world object: ifix scenery, ireg items, terrain RLE (trees/rocks in
/// chunk defs), or the avatar. Positions are absolute tiles.
/// </summary>
public sealed class U7Object
{
    static int _nextId;

    public int Id { get; } = System.Threading.Interlocked.Increment(ref _nextId);
    public int Tx;
    public int Ty;
    public int Tz;
    public int Shape;
    public int Frame;
    public int Quality;
    public ObjectKind Kind;
    public int DimX = 1;
    public int DimY = 1;
    public int DimZ;
    public bool Solid;
    public bool IsActor;
    public uint Flags;
    public uint Flags2;
    public U7Object? Container;
    public List<U7Object> Contents { get; } = new();
    /// <summary>Last gump-window hotspot. <see cref="int.MinValue"/> means unset.</summary>
    public int GumpX = int.MinValue;
    public int GumpY = int.MinValue;
    /// <summary>Exult <c>Ready_type_Exult</c> index, or -1 if not worn.</summary>
    public int ReadySlot = -1;
    public bool Removed;
    /// <summary>Exult render dependencies: objects that must paint before / after this one.</summary>
    public HashSet<U7Object>? Dependencies;
    public HashSet<U7Object>? Dependors;
    public uint RenderSeq;
    /// <summary>Frame number (high 32 bits) and paint index of the last draw; later paint = on top.</summary>
    public long PaintStamp;
    /// <summary>Exult "flat": lift 0 and no height; painted before every non-flat object.</summary>
    public bool IsFlat => Tz == 0 && DimZ == 0;
    public string BarkText = "";
    public ulong BarkUntilMsec;

    /// <summary>NPC index from npc.dat, or -1 if this is a world object.</summary>
    public int NpcNum = -1;
    /// <summary>Exult <c>Actor::Item_properties</c> (strength … missile_weapon).</summary>
    public int[] Props { get; } = new int[U7.Actors.ActorProp.Count];
    public int ScheduleType;
    public int Alignment;
    public string NpcName = "";
    public int WalkFrameIndex;
    /// <summary>Exult actor type flags (walk/swim/fly...) from npc.dat, kept for saving.</summary>
    public int TypeFlags;
    /// <summary>Exult <c>Actor::frame_time</c>: milliseconds per step while walking, 0 when not moving.</summary>
    public int FrameTime;
    /// <summary>For a corpse (Exult <c>Dead_body</c>): the NPC it belongs to, or -1.</summary>
    public int LiveNpcNum = -1;
    public bool Unused;
    public int FaceNum;
    public int PendingSchedule = -1;
    public int ScheduleDestTx;
    public int ScheduleDestTy;
    public int ScheduleDestTz;

    /// <summary>True for IREG hatchable eggs (Exult <c>Egg_object</c>).</summary>
    public bool IsEgg;
    public int EggType;
    public int EggCriteria;
    public int EggDistance;
    public int EggProbability = 100;
    public int EggData1;
    public int EggData2;
    public int EggData3;
    public int EggFlags;
    public int EggAreaX;
    public int EggAreaY;
    public int EggAreaW;
    public int EggAreaH;

    /// <summary>Exult <c>Barge_object</c> (shape class barge): its footprint in tiles and facing (0 N, 1 E, 2 S, 3 W).</summary>
    public bool IsBarge;
    public int BargeXTiles;
    public int BargeYTiles;
    public int BargeDir;

    /// <summary>Exult <c>Virtue_stone_object</c>: where the stone takes the party (0,0 until marked), and on which map.</summary>
    public U7.Core.TileCoord VirtueTarget;
    public int VirtueMap;

    /// <summary>Exult <c>Spellbook_object::circles</c>: a bit per spell for each of the 9 circles (null: none yet).</summary>
    public byte[]? SpellCircles;
    /// <summary>Exult <c>Spellbook_object::bookmark</c>: the marked spell, or -1.</summary>
    public int SpellBookmark = -1;

    public bool HasSavedGumpPos => GumpX != int.MinValue && GumpY != int.MinValue;
    public bool IsContained => Container is not null;
    public bool IsNpc => NpcNum >= 0;
    /// <summary>1×1 hatchable (shapes 200/275). Hidden unless painting eggs.</summary>
    public bool InvisibleEgg => IsEgg && DimX <= 1 && DimY <= 1;
    public bool IsMonster;
    /// <summary>Exult <c>Actor::target</c>: whom it fights.</summary>
    public U7Object? CombatTarget;
    /// <summary>Exult <c>Actor::oppressor</c>: the numbered NPC (or avatar) attacking it, if any.</summary>
    public U7Object? Oppressor;
    /// <summary>Exult <c>Actor::target_object</c> / <c>target_tile</c> / <c>attack_weapon</c>: what usecode's <c>set_to_attack</c> set up.</summary>
    public U7Object? AttackTargetObj;
    public U7.Core.TileCoord? AttackTargetTile;
    public int AttackWeapon = -1;
    public ulong HitUntilMsec;
    public bool IsDead => GetFlag(U7.Actors.ObjFlag.Dead);
    /// <summary>Exult <c>Actor::Attack_mode</c> (combat-mode button).</summary>
    public int AttackMode;
    /// <summary>Exult <c>did_user_set_attack</c>: the player chose the mode, so combat keeps a flee.</summary>
    public bool UserSetAttack;
    /// <summary>Exult halo / <c>is_combat_protected</c>.</summary>
    public bool CombatProtected;

    public int ChunkX => Tx / U7.Core.U7Constants.TilesPerChunk;
    public int ChunkY => Ty / U7.Core.U7Constants.TilesPerChunk;

    public bool Occupies(int tx, int ty)
    {
        var x0 = Tx - DimX + 1;
        var y0 = Ty - DimY + 1;
        return tx >= x0 && tx <= Tx && ty >= y0 && ty <= Ty;
    }

    /// <summary>Whether this object counts in <see cref="ChunkBlocking"/> at the tile and lift: solid, with height.</summary>
    public bool BlocksAt(int tx, int ty, int lift) =>
        Solid && DimZ > 0 && Occupies(tx, ty) && lift >= Tz && lift < Tz + DimZ;

    /// <summary>
    /// Approximate Exult paint order: south-east and higher lift draw later.
    /// </summary>
    public int RenderOrder => (Tx + Ty) * 64 + Tz * 4 + (IsActor ? 1 : 0);

    public bool GetFlag(int flag)
    {
        if (flag is >= 0 and < 32)
        {
            return (Flags & (1u << flag)) != 0;
        }

        if (flag is >= 32 and < 64)
        {
            return (Flags2 & (1u << (flag - 32))) != 0;
        }

        return false;
    }

    public void SetFlag(int flag)
    {
        if (flag is >= 0 and < 32)
        {
            Flags |= 1u << flag;
        }
        else if (flag is >= 32 and < 64)
        {
            Flags2 |= 1u << (flag - 32);
        }
    }

    public void ClearFlag(int flag)
    {
        if (flag is >= 0 and < 32)
        {
            Flags &= ~(1u << flag);
        }
        else if (flag is >= 32 and < 64)
        {
            Flags2 &= ~(1u << (flag - 32));
        }
    }

    public void CollectContents(List<U7Object> dest, bool recursive = true)
    {
        foreach (var child in Contents)
        {
            dest.Add(child);
            if (recursive)
            {
                child.CollectContents(dest, true);
            }
        }
    }

    public int GetProp(int index) =>
        (uint)index < (uint)Props.Length ? Props[index] : 0;

    public void SetProp(int index, int value)
    {
        if ((uint)index < (uint)Props.Length)
        {
            Props[index] = value;
        }
    }

    public void Bark(string text, ulong ms = 1800)
    {
        BarkText = text;
        BarkUntilMsec = Godot.Time.GetTicksMsec() + ms;
    }

    public int GetLevel()
    {
        var n = (uint)Math.Max(0, GetProp(U7.Actors.ActorProp.Exp) / 50);
        var log = 0;
        for (n >>= 1; n != 0; n >>= 1)
        {
            log++;
        }

        return 1 + log;
    }

    /// <summary>Exult <c>Actor::set_usecode</c>: a function the actor runs instead of its own (the arrest's guards), or -1.</summary>
    public int AssignedUsecode = -1;

    public int GetUsecode()
    {
        if (IsEgg && EggType == U7.World.EggType.Usecode)
        {
            return EggData2;
        }

        if (AssignedUsecode >= 0)
        {
            return AssignedUsecode;
        }

        return NpcNum is >= 0 and < 256 ? 0x400 + NpcNum : -1;
    }
}
