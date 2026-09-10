namespace U7.Actors;

/// <summary>Exult <c>Actor::Item_properties</c>.</summary>
public static class ActorProp
{
    public const int Strength = 0;
    public const int Dexterity = 1;
    public const int Intelligence = 2;
    public const int Health = 3;
    public const int Combat = 4;
    public const int Mana = 5;
    public const int Magic = 6;
    public const int Training = 7;
    public const int Exp = 8;
    public const int FoodLevel = 9;
    public const int SexFlag = 10;
    public const int MissileWeapon = 11;
    public const int Count = 12;
}

/// <summary>Exult <c>Actor::Alignment</c>.</summary>
public static class Alignment
{
    public const int Neutral = 0;
    public const int Good = 1;
    public const int Evil = 2;
    public const int Chaotic = 3;
}

/// <summary>Exult <c>Actor_gump</c> ready spots.</summary>
public static class ReadySpot
{
    public const int Head = 0;
    public const int Back = 1;
    public const int Belt = 2;
    public const int Lhand = 3;
    public const int Lfinger = 4;
    public const int Legs = 5;
    public const int Feet = 6;
    public const int Rfinger = 7;
    public const int Rhand = 8;
    public const int Torso = 9;
    public const int Neck = 10;
    public const int Ammo = 11;
    public const int Back2h = 0x0c;
    public const int BackShield = 0x0d;
    public const int Earrings = 0x0e;
    public const int Cloak = 0x0f;
    public const int Gloves = 0x10;
    public const int Ucont = 0x11;
    public const int BothHands = 0x12;
    public const int GlovesPair = 0x13;
    public const int NeckFill = 0x14;
    public const int Scabbard = 0x15;
    public const int TripleBolts = 0x16;
    public const int Invalid = 0xff;

    /// <summary>Exult <c>Ready_spot_from_BG</c>.</summary>
    public static int FromBg(int spot) =>
        spot switch
        {
            0 => Back,
            1 => Lhand,
            2 => Rhand,
            3 => Belt,
            4 => NeckFill,
            5 => Torso,
            6 => Lfinger,
            7 => Rfinger,
            8 => Ammo,
            9 => Head,
            10 => Legs,
            11 => Feet,
            12 => Ucont,
            13 => Cloak,
            14 => Gloves,
            15 => TripleBolts,
            16 => Earrings,
            17 => BackShield,
            18 => Lhand,
            19 => Back2h,
            20 => BothHands,
            21 => GlovesPair,
            22 => Neck,
            23 => Scabbard,
            _ => Invalid
        };
}

/// <summary>Exult <c>Schedule::Schedule_types</c>.</summary>
public static class ScheduleType
{
    public const int Combat = 0;
    public const int HorizPace = 1;
    public const int VertPace = 2;
    public const int Talk = 3;
    public const int Dance = 4;
    public const int Eat = 5;
    public const int Farm = 6;
    public const int TendShop = 7;
    public const int Miner = 8;
    public const int Hound = 9;
    public const int Stand = 10;
    public const int Loiter = 11;
    public const int Wander = 12;
    public const int Blacksmith = 13;
    public const int Sleep = 14;
    public const int Wait = 15;
    public const int Sit = 16;
    public const int Graze = 17;
    public const int Bake = 18;
    public const int Sew = 19;
    public const int Shy = 20;
    public const int Lab = 21;
    public const int Thief = 22;
    public const int Waiter = 23;
    public const int Special = 24;
    public const int KidGames = 25;
    public const int EatAtInn = 26;
    public const int Duel = 27;
    public const int Preach = 28;
    public const int Patrol = 29;
    public const int DeskWork = 30;
    public const int FollowAvatar = 31;
    public const int WalkToSchedule = 32;

    public static readonly string[] Names =
    [
        "combat", "horiz_pace", "vert_pace", "talk", "dance", "eat", "farm",
        "tend_shop", "miner", "hound", "stand", "loiter", "wander", "blacksmith",
        "sleep", "wait", "sit", "graze", "bake", "sew", "shy", "lab", "thief",
        "waiter", "special", "kid_games", "eat_at_inn", "duel", "preach",
        "patrol", "desk_work", "follow_avatar", "walk_to_schedule"
    ];

    public static string Name(int type) =>
        (uint)type < (uint)Names.Length ? Names[type] : $"type_{type}";
}

/// <summary>Exult <c>Actor::Attack_mode</c> (combat-mode gump frames).</summary>
public static class AttackMode
{
    public const int Nearest = 0;
    public const int Weakest = 1;
    public const int Strongest = 2;
    public const int Berserk = 3;
    public const int Protect = 4;
    public const int Defend = 5;
    public const int Flank = 6;
    public const int Flee = 7;
    public const int Random = 8;
    public const int Manual = 9;
    public const int AvatarFrames = 10;
    public const int NpcFrames = 9;
}

/// <summary>Exult <c>Obj_flags</c> (subset used by NPCs).</summary>
public static class ObjFlag
{
    public const int Met = 28;
    public const int Dead = 4;
    public const int Temporary = 18;
    public const int OkayToTake = 11;
    public const int InParty = 6;
    public const int Asleep = 1;
    public const int Paralyzed = 7;
    public const int DontMove = 16;
}
