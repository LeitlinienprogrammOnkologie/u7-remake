namespace U7.World;

/// <summary>Exult <c>Egg_object::Egg_types</c>.</summary>
public static class EggType
{
    public const int Monster = 1;
    public const int Jukebox = 2;
    public const int SoundSfx = 3;
    public const int Voice = 4;
    public const int Usecode = 5;
    public const int Missile = 6;
    public const int Teleport = 7;
    public const int Weather = 8;
    public const int Path = 9;
    public const int Button = 10;
    public const int Intermap = 11;

    public static readonly string[] Names =
    [
        "?", "monster", "jukebox", "soundsfx", "voice", "usecode", "missile",
        "teleport", "weather", "path", "button", "intermap"
    ];

    public static string Name(int type) =>
        (uint)type < (uint)Names.Length ? Names[type] : $"type_{type}";
}

/// <summary>Exult <c>Egg_object::Egg_criteria</c>.</summary>
public static class EggCriteria
{
    public const int CachedIn = 0;
    public const int PartyNear = 1;
    public const int AvatarNear = 2;
    public const int AvatarFar = 3;
    public const int AvatarFootpad = 4;
    public const int PartyFootpad = 5;
    public const int SomethingOn = 6;
    public const int External = 7;
}

/// <summary>Exult <c>Egg_object::Egg_flag_shifts</c>.</summary>
public static class EggFlag
{
    public const int Nocturnal = 1 << 0;
    public const int Once = 1 << 1;
    public const int Hatched = 1 << 2;
    public const int AutoReset = 1 << 3;
}
