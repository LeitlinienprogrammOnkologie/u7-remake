using System.Text;
using Godot;
using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Loads Black Gate NPCs from INITGAME.DAT flex entry 0 (<c>npc.dat</c>).
/// Byte layout is Exult <c>Actor::read</c> with <c>fix_first</c> (new game).
/// </summary>
public static class NpcDat
{
    /// <summary>Count of fixed NPCs from the npc.dat header (needed when writing).</summary>
    public static int NumFixed { get; private set; }

    public static List<U7Object?> Load(GameMap map, U7Object avatar)
    {
        byte[] data;
        bool fixFirst;
        if (U7Paths.SavedGameFile("NPC.DAT") is { } saved)
        {
            // A saved game (Exult layout): raw npc.dat.
            data = File.ReadAllBytes(saved);
            fixFirst = false;
            GD.Print($"npc.dat from {saved}");
        }
        else
        {
            var path = Path.Combine(U7Paths.StaticDir, "INITGAME.DAT");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("INITGAME.DAT not found.", path);
            }

            var flex = new FlexFile(path);
            var entry = flex.Get(0);
            if (entry.Length <= 13)
            {
                throw new InvalidDataException("INITGAME.DAT entry 0 is empty.");
            }

            var name = Encoding.ASCII.GetString(entry[..13]).TrimEnd('\0', '.');
            if (!name.StartsWith("npc.dat", StringComparison.OrdinalIgnoreCase))
            {
                GD.Print($"INITGAME entry 0 name is '{name}', expected npc.dat");
            }

            data = entry[13..].ToArray();
            fixFirst = true;
        }

        var r = new Cursor(data);
        var num1 = r.U2();
        var num2 = r.U2();
        NumFixed = num1;
        var count = num1 + num2;
        var npcs = new List<U7Object?>(count);
        var spawned = 0;
        for (var i = 0; i < count; i++)
        {
            var dest = i == 0 ? avatar : new U7Object();
            ReadActor(r, dest, i, map, fixFirst);
            dest.NpcNum = i;
            dest.IsActor = true;
            dest.Kind = ObjectKind.Actor;
            dest.Solid = false;
            ApplyShape(map.Catalog, dest);
            if (i == 0)
            {
                npcs.Add(avatar);
                map.MoveObject(avatar, dest.Tx, dest.Ty, dest.Tz);
                continue;
            }

            npcs.Add(dest);
            if (dest.IsDead)
            {
                dest.Removed = true; // not on the map; its corpse is an IREG object
                continue;
            }

            if (dest.Unused)
            {
                continue;
            }

            map.AddObject(dest);
            spawned++;
        }

        GD.Print($"npc.dat: {count} records ({num1} type1 + {num2}), spawned {spawned}, leftover {data.Length - r.I}");
        foreach (var id in new[] { 0, 1, 2, 11, 13, 14 })
        {
            if ((uint)id >= (uint)npcs.Count || npcs[id] is not { } n)
            {
                continue;
            }

            GD.Print(
                $"NPC {id} '{n.NpcName}' shape {n.Shape} at {n.Tx},{n.Ty},{n.Tz} " +
                $"sched {n.ScheduleType} unused={n.Unused} items={n.Contents.Count}");
        }

        var weapons = WeaponTable.Load();
        var armor = ArmorTable.Load();
        foreach (var n in npcs)
        {
            if (n is not { Unused: false })
            {
                continue;
            }

            Equipment.ReadyBestWeapon(n, map.Catalog, weapons, armor);
        }

        var avW = Equipment.GetReadied(avatar, ReadySpot.Lhand)
                  ?? Equipment.GetReadied(avatar, ReadySpot.Rhand);
        GD.Print(
            $"avatar ready {(avW is null ? "none" : $"shape {avW.Shape} slot {avW.ReadySlot}")} " +
            $"armor {Equipment.WornArmor(avatar, armor, out _)}");

        return npcs;
    }

    static void ReadActor(Cursor r, U7Object npc, int num, GameMap map, bool fixFirst)
    {
        var locx = r.U1();
        var locy = r.U1();
        var shnum = r.U2();
        npc.Shape = shnum & 0x3ff;
        npc.Frame = shnum >> 10;
        var iflag1 = r.U2();
        var schunk = r.U1();
        r.U1(); // map_num, discarded for new game
        var usefun = r.U2();
        npc.Tz = usefun >> 12;
        npc.SetProp(ActorProp.Health, r.S1());
        r.Skip(3);
        var iflag2 = r.U2();
        npc.Unused = iflag2 == 0 && num > 0;
        // Exult Actor::read: the original flags any nonzero word; Exult's writer uses bit 0.
        var hasContents = fixFirst ? iflag1 != 0 && !npc.Unused : (iflag1 & 1) != 0;
        var rflags = r.U2();
        npc.Alignment = (rflags >> 3) & 3;
        var strengthVal = r.U1();
        npc.SetProp(ActorProp.Strength, strengthVal & 0x3F);
        npc.SetProp(ActorProp.Dexterity, r.U1());
        var intelVal = r.U1();
        npc.SetProp(ActorProp.Intelligence, intelVal & 0x1F);
        var combatVal = r.U1();
        npc.SetProp(ActorProp.Combat, combatVal & 0x7F);
        npc.ScheduleType = r.U1();
        var amode = r.U1();
        npc.AttackMode = amode & 0xf;
        npc.CombatProtected = (amode & (1 << 4)) != 0;
        r.Skip(1); // charmalign / effective alignment
        // Exult Actor::read: the original stores magic/mana two bytes later;
        // Exult's own saves put magic (high bit set) and mana here.
        var unk0 = r.U1();
        var unk1 = r.U1();
        int magic;
        int mana;
        var flags3 = 0;
        if (fixFirst || unk0 == 0)
        {
            var magicVal = r.U1();
            var manaVal = r.U1();
            if (num == 0)
            {
                magic = magicVal & 0x1f;
                mana = manaVal & 0x1f;
                flags3 = 1;
            }
            else
            {
                magic = 0;
                mana = 0;
                flags3 = manaVal;
            }
        }
        else
        {
            magic = unk0 & 0x7f;
            mana = unk1;
            r.U1(); // temperature
            flags3 = r.U1() & 7;
        }

        npc.SetProp(ActorProp.Magic, magic);
        npc.SetProp(ActorProp.Mana, mana < magic ? mana : magic);
        if ((flags3 & 1) != 0)
        {
            npc.SetFlag(ObjFlag.Met);
        }

        npc.FaceNum = r.U2();
        if (fixFirst || (npc.FaceNum == 0 && num > 0))
        {
            npc.FaceNum = num;
        }
        r.Skip(1);
        npc.SetProp(ActorProp.Exp, (int)r.U4());
        npc.SetProp(ActorProp.Training, r.U1());
        r.Skip(2); // primary attacker
        r.Skip(2); // secondary attacker
        r.U2(); // oppressor
        r.Skip(4);
        var schedTx = r.U2();
        var schedTy = r.U2();
        npc.TypeFlags = r.U2();
        r.Skip(5);
        r.U1(); // next_schedule
        r.Skip(1);
        r.Skip(2);
        r.Skip(2);
        r.U2(); // 16-bit shape
        r.Skip(2); // polymorph
        npc.Flags = (uint)(r.U2() | (r.U2() << 16)); // Obj_flags (in_party, dead, ...)
        var schedTz = r.U1();
        r.Skip(1);
        if (!fixFirst)
        {
            npc.ScheduleDestTx = schedTx;
            npc.ScheduleDestTy = schedTy;
            npc.ScheduleDestTz = schedTz;
        }
        r.Skip(4); // flags2
        r.Skip(1); // extended skin
        r.Skip(14);
        var food = r.U1();
        npc.SetProp(ActorProp.FoodLevel, fixFirst ? 18 : food);
        r.Skip(7);
        npc.NpcName = r.CString(16);

        var scy = 16 * (schunk / 12);
        var scx = 16 * (schunk % 12);
        if (hasContents)
        {
            map.ReadIregObjects(r.D, ref r.I, scx, scy, npc);
        }

        if (!fixFirst && (iflag1 & 2) != 0)
        {
            // Exult Game_map::read_special_ireg: scheduled usecode scripts up to the end mark.
            while (r.I < r.D.Length && r.D[r.I] == 255)
            {
                r.I++;
                var kind = r.I < r.D.Length ? r.D[r.I++] : 2;
                if (kind == 2)
                {
                    break; // IREG_ENDMARK
                }

                if (kind == 1 && r.I + 2 <= r.D.Length)
                {
                    var len = r.D[r.I] | (r.D[r.I + 1] << 8);
                    r.I += 2;
                    if (r.I + len <= r.D.Length)
                    {
                        map.PendingScripts.Add((npc, r.D.AsSpan(r.I, len).ToArray())); // IREG_UCSCRIPT
                    }

                    r.I += len;
                }
            }
        }

        var cx = locx >> 4;
        var cy = locy >> 4;
        var tilex = locx & 0xf;
        var tiley = locy & 0xf;
        npc.Tx = (scx + cx) * 16 + tilex;
        npc.Ty = (scy + cy) * 16 + tiley;
    }

    /// <summary>Exult <c>Game_window::write_npcs</c>: NPC.DAT with the fixed/extra counts.</summary>
    public static void Save(string dir, List<U7Object?> npcs, GameMap map)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var count = npcs.Count;
        var num1 = Math.Min(NumFixed, count);
        w.Write((ushort)num1);
        w.Write((ushort)(count - num1));
        for (var i = 0; i < count; i++)
        {
            var npc = npcs[i];
            if (npc is null)
            {
                WriteUnused(w, i);
                continue;
            }

            WriteActor(w, npc, i, map);
        }

        w.Flush();
        File.WriteAllBytes(Path.Combine(dir, "NPC.DAT"), ms.ToArray());
    }

    /// <summary>Exult MONSNPCS.DAT: count, then <c>Actor::write</c> records of living spawned monsters.</summary>
    public static void SaveMonsters(string dir, IEnumerable<U7Object> monsters, GameMap map)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var live = monsters.Where(m => !m.IsDead && !m.Removed).ToList();
        w.Write((ushort)live.Count);
        foreach (var m in live)
        {
            WriteActor(w, m, 1000, map);
        }

        w.Flush();
        File.WriteAllBytes(Path.Combine(dir, "MONSNPCS.DAT"), ms.ToArray());
    }

    /// <summary>Exult <c>Game_window::read_npcs</c> monster part: recreate spawned monsters from a save.</summary>
    public static List<U7Object> LoadMonsters(GameMap map)
    {
        var result = new List<U7Object>();
        var data = U7Paths.ReadGameDat("MONSNPCS.DAT");
        if (data is null)
        {
            return result;
        }

        var r = new Cursor(data);
        var count = r.U2();
        for (var i = 0; i < count && r.I < r.D.Length; i++)
        {
            var m = new U7Object { NpcNum = -1, IsMonster = true };
            ReadActor(r, m, 1000, map, fixFirst: false);
            m.NpcNum = -1;
            m.IsActor = true;
            m.Kind = ObjectKind.Actor;
            m.Solid = false;
            m.SetFlag(ObjFlag.Temporary);
            ApplyShape(map.Catalog, m);
            if (string.IsNullOrEmpty(m.NpcName))
            {
                var rec = map.Catalog[m.Shape];
                m.NpcName = string.IsNullOrEmpty(rec.Name) ? $"shape {m.Shape}" : rec.Name;
            }

            if (!m.IsDead)
            {
                map.AddObject(m);
                result.Add(m);
            }
        }

        GD.Print($"monsters restored: {result.Count}");
        return result;
    }

    static void WriteUnused(BinaryWriter w, int num)
    {
        var dummy = new U7Object { NpcNum = num, Unused = true, Shape = 721, IsActor = true };
        WriteActor(w, dummy, num, null);
    }

    /// <summary>Exult <c>Actor::write</c> (actorio.cc), Exult's extended layout.</summary>
    static void WriteActor(BinaryWriter w, U7Object npc, int num, GameMap? map)
    {
        var cx = npc.Tx / 16;
        var cy = npc.Ty / 16;
        w.Write((byte)(((cx % 16) << 4) | (npc.Tx % 16)));
        w.Write((byte)(((cy % 16) << 4) | (npc.Ty % 16)));
        w.Write((byte)(npc.Shape & 0xff));
        w.Write((byte)(((npc.Shape >> 8) & 3) | ((npc.Frame & 63) << 2)));
        var hasContents = npc.Contents.Count > 0 && !npc.Unused;
        w.Write((ushort)((hasContents ? 1 : 0) | 2 | 16));
        w.Write((byte)((cy / 16) * 12 + cx / 16));
        w.Write((byte)0); // map
        w.Write((ushort)((npc.Tz & 15) << 12)); // usecode fun 0 = default
        w.Write((sbyte)Math.Clamp(npc.GetProp(ActorProp.Health), -128, 127));
        w.Write((byte)0);
        w.Write((ushort)0);
        var unused = npc.Unused; // dead NPCs keep their record (dead flag) like Exult
        w.Write((ushort)(unused ? 0 : 1));
        var iout = 0;
        if (npc.GetFlag(ObjFlag.Asleep)) iout |= 1 << 7;
        if (npc.GetFlag(2)) iout |= 1 << 8; // charmed
        if (npc.GetFlag(3)) iout |= 1 << 9; // cursed
        if (npc.GetFlag(ObjFlag.InParty)) iout |= 1 << 0xB;
        if (npc.GetFlag(ObjFlag.Paralyzed)) iout |= 1 << 0xC;
        if (npc.GetFlag(8)) iout |= 1 << 0xD; // poisoned
        if (npc.GetFlag(9)) iout |= 1 << 0xE; // protection
        if (npc.GetFlag(10)) iout |= 1 << 0xA; // on moving barge
        if (npc.GetFlag(ObjFlag.Temporary)) iout |= 1 << 6;
        iout |= (npc.Alignment & 3) << 3;
        w.Write((ushort)iout);
        w.Write((byte)(npc.GetProp(ActorProp.Strength) & 0x3f));
        w.Write((byte)npc.GetProp(ActorProp.Dexterity));
        w.Write((byte)(npc.GetProp(ActorProp.Intelligence) & 0x1f));
        w.Write((byte)(npc.GetProp(ActorProp.Combat) & 0x7f));
        w.Write((byte)npc.ScheduleType);
        w.Write((byte)((npc.AttackMode & 0xf) | (npc.CombatProtected ? 1 << 4 : 0)));
        w.Write((byte)npc.Alignment); // effective alignment
        w.Write((byte)(npc.GetProp(ActorProp.Magic) | 0x80));
        w.Write((byte)npc.GetProp(ActorProp.Mana));
        w.Write((byte)0); // temperature
        w.Write((byte)(npc.GetFlag(ObjFlag.Met) ? 1 : 0)); // flags3
        w.Write((ushort)npc.FaceNum);
        w.Write((byte)0);
        w.Write((uint)npc.GetProp(ActorProp.Exp));
        w.Write((byte)npc.GetProp(ActorProp.Training));
        w.Write((ushort)0);
        w.Write((ushort)0);
        w.Write((ushort)0xffff); // oppressor
        w.Write((uint)0);
        w.Write((ushort)npc.ScheduleDestTx);
        w.Write((ushort)npc.ScheduleDestTy);
        w.Write((ushort)npc.TypeFlags);
        w.Write((uint)0);
        w.Write((byte)0);
        w.Write((byte)255); // next_schedule
        w.Write((byte)0);
        w.Write((ushort)0);
        w.Write((ushort)0);
        w.Write((ushort)npc.Shape);
        w.Write((ushort)0); // polymorph
        w.Write(npc.Flags);
        w.Write((byte)npc.ScheduleDestTz);
        w.Write((byte)0);
        w.Write(npc.Flags2);
        w.Write((byte)0); // skin
        for (var i = 0; i < 14; i++) w.Write((byte)0);
        w.Write((byte)npc.GetProp(ActorProp.FoodLevel));
        for (var i = 0; i < 7; i++) w.Write((byte)0);
        var name = new byte[16];
        var bytes = Encoding.ASCII.GetBytes(npc.NpcName ?? "");
        Array.Copy(bytes, name, Math.Min(16, bytes.Length));
        w.Write(name);
        if (hasContents && map is not null)
        {
            map.WriteActorContents(w, npc);
        }

        // Exult Game_map::write_scheduled(..., write_mark = true): scripts, then the end mark.
        map?.WriteScheduled(w, npc);
        w.Write((byte)255); // IREG_SPECIAL
        w.Write((byte)2);   // IREG_ENDMARK
    }

    static void ApplyShape(ShapeCatalog catalog, U7Object npc)
    {
        var info = catalog[npc.Shape];
        var reflected = (npc.Frame & 32) != 0;
        npc.DimX = reflected ? info.DimY : info.DimX;
        npc.DimY = reflected ? info.DimX : info.DimY;
        npc.DimZ = info.DimZ;
        npc.Solid = false;
        npc.IsActor = true;
        npc.Kind = ObjectKind.Actor;
    }

    sealed class Cursor
    {
        public readonly byte[] D;
        public int I;

        public Cursor(byte[] data) => D = data;

        public byte U1() => D[I++];

        public sbyte S1() => unchecked((sbyte)D[I++]);

        public int U2()
        {
            var v = BitConverter.ToUInt16(D, I);
            I += 2;
            return v;
        }

        public uint U4()
        {
            var v = BitConverter.ToUInt32(D, I);
            I += 4;
            return v;
        }

        public void Skip(int n) => I += n;

        public string CString(int len)
        {
            var end = I;
            var stop = Math.Min(I + len, D.Length);
            while (end < stop && D[end] != 0)
            {
                end++;
            }

            var s = Encoding.ASCII.GetString(D, I, end - I);
            I += len;
            return s;
        }
    }
}
