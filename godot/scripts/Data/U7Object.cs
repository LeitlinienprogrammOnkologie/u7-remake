namespace U7.Data;

public enum ObjectKind
{
    TerrainRle,
    Ifix,
    Ireg,
    Actor
}

/// <summary>Exult <c>Actor::Casting_mode</c>.</summary>
public enum CastingMode
{
    NotCasting,
    /// <summary>The actor's next usecode script shows the casting frames.</summary>
    Init,
    /// <summary>Shown in the hand until the script ends.</summary>
    Show
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
    public ObjectSet? Dependencies;
    public ObjectSet? Dependors;
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
    /// <summary>
    /// 1×1 hatchable (shapes 200/275), or a path marker (607, which Exult's
    /// <c>create_ireg_object</c> makes an <c>Egglike_game_object</c>). Hidden
    /// unless painting eggs.
    /// </summary>
    public bool InvisibleEgg => (IsEgg && DimX <= 1 && DimY <= 1) || Shape == U7.Actors.PatrolSchedule.PathShape;
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
    /// <summary>Exult <c>Actor::casting_mode</c>: casting frames are coming with the actor's next script, or shown in its hand.</summary>
    public CastingMode Casting;
    /// <summary>Exult <c>Actor::casting_shape</c>: the shape shown in the hand while casting (859).</summary>
    public int CastingShape = -1;
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

    /// <summary>
    /// Everything inside, depth first, each thing before its own contents
    /// (Exult <c>Container_game_object</c>'s searches); removed things and what
    /// they hold are skipped. One iterator and stack, not one per nesting level.
    /// </summary>
    public IEnumerable<U7Object> AllInside()
    {
        var stack = new Stack<(U7Object Container, int Next)>();
        var container = this;
        var next = 0;
        while (true)
        {
            if (next < container.Contents.Count)
            {
                var obj = container.Contents[next++];
                if (obj.Removed)
                {
                    continue;
                }

                yield return obj;
                if (obj.Contents.Count > 0)
                {
                    stack.Push((container, next));
                    (container, next) = (obj, 0);
                }
            }
            else if (stack.Count > 0)
            {
                (container, next) = stack.Pop();
            }
            else
            {
                yield break;
            }
        }
    }

    /// <summary>Sets the flag on this and on everything inside it.</summary>
    public void SetFlagWithContents(int flag)
    {
        SetFlag(flag);
        foreach (var item in Contents)
        {
            item.SetFlagWithContents(flag);
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

    /// <summary>
    /// Exult <c>Actor::get_effective_prop</c>: dexterity and combat are 0 and
    /// 1 while paralysed, asleep or knocked out; those and intelligence the
    /// same when dead, else one less charmed or asleep; those and strength
    /// gain up to 15 with might and lose 3 cursed. Other properties as they are.
    /// </summary>
    public int EffectiveProp(int prop)
    {
        var val = GetProp(prop);
        var dexterity = prop == U7.Actors.ActorProp.Dexterity;
        if (dexterity || prop == U7.Actors.ActorProp.Combat)
        {
            if (GetFlag(U7.Actors.ObjFlag.Paralyzed) || GetFlag(U7.Actors.ObjFlag.Asleep) || GetProp(U7.Actors.ActorProp.Health) <= 0)
            {
                return dexterity ? 0 : 1;
            }
        }

        if (dexterity || prop is U7.Actors.ActorProp.Combat or U7.Actors.ActorProp.Intelligence)
        {
            if (IsDead)
            {
                return dexterity ? 0 : 1;
            }

            if (GetFlag(U7.Actors.ObjFlag.Charmed) || GetFlag(U7.Actors.ObjFlag.Asleep))
            {
                val--;
            }
        }

        if (dexterity || prop is U7.Actors.ActorProp.Combat or U7.Actors.ActorProp.Intelligence or U7.Actors.ActorProp.Strength)
        {
            if (GetFlag(U7.Actors.ObjFlag.Might))
            {
                val += Math.Min(val, 15);
            }

            if (GetFlag(U7.Actors.ObjFlag.Cursed))
            {
                val -= 3;
            }
        }

        return val;
    }

    /// <summary>Exult <c>Actor::charmalign</c>: the side a charmed actor takes (the attacker's that charmed it).</summary>
    public int CharmAlignment;

    /// <summary>
    /// Exult <c>Actor::get_effective_alignment</c>: the charm's side while
    /// charmed, except for the avatar (Exult's charmed_more_difficult is off);
    /// the side combat, guards and usecode go by. (SI's goblin village hack
    /// doesn't apply.)
    /// </summary>
    public int EffectiveAlignment => GetFlag(U7.Actors.ObjFlag.Charmed) && NpcNum != 0 ? CharmAlignment : Alignment;

    /// <summary>Exult <c>Actor::set_effective_alignment</c>: the charm's side, and the actor's own unless charmed.</summary>
    public void SetEffectiveAlignment(int align)
    {
        CharmAlignment = align;
        if (!GetFlag(U7.Actors.ObjFlag.Charmed))
        {
            Alignment = align;
        }
    }

    public void Bark(string text, ulong ms = U7.Core.U7Constants.TextEffectMs)
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
    /// <summary>Exult <c>usecode_name</c>: the assigned function's name in UCC's table, which a save keeps instead of its number.</summary>
    public string? UsecodeName;
    /// <summary>Exult <c>Actor::ident</c> (0-31): usecode's <c>get_npc_id</c> / <c>set_npc_id</c>.</summary>
    public int Ident;
    /// <summary>Exult <c>Actor::temperature</c> (0-63, Serpent Isle's cold), kept through saves.</summary>
    public int Temperature;
    /// <summary>Exult <c>Actor::shape_save</c>: a polymorphed actor's own shape, -1 when it has its own.</summary>
    public int ShapeSave = -1;
    /// <summary>Exult <c>Actor::skin_color</c>: -1 for the shape's own; a save writes it as a byte, so it comes back 255.</summary>
    public int SkinColor = -1;

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
