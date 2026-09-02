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
    public static List<U7Object?> Load(GameMap map, U7Object avatar)
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

        var data = entry[13..].ToArray();
        var r = new Cursor(data);
        var num1 = r.U2();
        var num2 = r.U2();
        var count = num1 + num2;
        var npcs = new List<U7Object?>(count);
        var spawned = 0;
        for (var i = 0; i < count; i++)
        {
            var dest = i == 0 ? avatar : new U7Object();
            ReadActor(r, dest, i, map);
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

    static void ReadActor(Cursor r, U7Object npc, int num, GameMap map)
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
        var hasContents = iflag1 != 0 && !npc.Unused;
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
        r.U1(); // attack mode
        r.Skip(1); // charmalign (fix_first)
        r.Skip(2); // unk0, unk1
        var magicVal = r.U1();
        var manaVal = r.U1();
        int magic;
        int mana;
        if (num == 0)
        {
            magic = magicVal & 0x1f;
            mana = manaVal & 0x1f;
        }
        else
        {
            magic = 0;
            mana = 0;
        }

        npc.SetProp(ActorProp.Magic, magic);
        npc.SetProp(ActorProp.Mana, mana < magic ? mana : magic);
        npc.FaceNum = r.U2();
        npc.FaceNum = num; // fix_first
        r.Skip(1);
        npc.SetProp(ActorProp.Exp, (int)r.U4());
        npc.SetProp(ActorProp.Training, r.U1());
        r.Skip(2); // primary attacker
        r.Skip(2); // secondary attacker
        r.U2(); // oppressor
        r.Skip(4);
        r.U2(); // schedule_loc.tx
        r.U2(); // schedule_loc.ty
        r.U2(); // type flags
        r.Skip(5);
        r.U1(); // next_schedule
        r.Skip(1);
        r.Skip(2);
        r.Skip(2);
        r.U2(); // 16-bit shape
        r.Skip(2); // polymorph
        r.Skip(4); // flags
        r.Skip(2); // siflags / schedule z
        r.Skip(4); // flags2
        r.Skip(1); // extended skin
        r.Skip(14);
        r.S1(); // food
        npc.SetProp(ActorProp.FoodLevel, 18);
        r.Skip(7);
        npc.NpcName = r.CString(16);

        var scy = 16 * (schunk / 12);
        var scx = 16 * (schunk % 12);
        if (hasContents)
        {
            map.ReadIregObjects(r.D, ref r.I, scx, scy, npc);
        }

        var cx = locx >> 4;
        var cy = locy >> 4;
        var tilex = locx & 0xf;
        var tiley = locy & 0xf;
        npc.Tx = (scx + cx) * 16 + tilex;
        npc.Ty = (scy + cy) * 16 + tiley;
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
