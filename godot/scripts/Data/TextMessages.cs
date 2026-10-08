using System.IO;
using System.Text;
using U7.Core;

namespace U7.Data;

/// <summary>
/// Exult <c>get_text_msg</c>: the game's own messages, TEXT.FLX entries from
/// 0x400 on (item names come before them), followed by Exult's own from 0x100
/// (data/exultmsg.txt). Numbers are Exult's (items.h).
/// </summary>
public static class TextMessages
{
    /// <summary>A guard pacing into someone ("Step aside!").</summary>
    public const int FirstMoveAside = 0x00;
    public const int LastMoveAside = 0x02;
    /// <summary>A preacher at the podium ("Strive for unity!"), and to one of the flock ("Art thou with us, brother?").</summary>
    public const int FirstPreach = 0x03;
    public const int LastPreach = 0x07;
    public const int FirstPreach2 = 0x08;
    public const int LastPreach2 = 0x0b;
    /// <summary>The flock answering, or the preacher praying.</summary>
    public const int FirstAmen = 0x0c;
    public const int LastAmen = 0x0f;
    /// <summary>A thief's small talk ("Nice weather today.").</summary>
    public const int FirstThief = 0x10;
    public const int LastThief = 0x13;
    /// <summary>An NPC coming to talk ("I would have words with thee.").</summary>
    public const int FirstTalk = 0x14;
    public const int LastTalk = 0x16;
    public const int FirstArrest = 0x17;
    public const int LastArrest = 0x1a;

    // Thefts and calls for the guards.
    public const int FirstNeedHelp = 0x30;
    public const int LastNeedHelp = 0x33;
    public const int FirstCallGuardsTheft = 0x3c;
    public const int LastCallGuardsTheft = 0x3e;
    public const int FirstCallPolice = 0x69;
    public const int LastCallPolice = 0x6d;
    public const int FirstCallGuards = 0x6c;
    public const int LastCallGuards = 0x6d;
    public const int FirstTheft = 0x6e;
    public const int LastTheft = 0x70;
    public const int FirstInvisTheft = 0x85;
    public const int LastInvisTheft = 0x87;
    public const int HeardSomething = 0x95;
    /// <summary>A farmer cutting crops ("These crops are tough!"), and looking for some.</summary>
    public const int FirstFarmer = 0x3f;
    public const int LastFarmer = 0x41;
    public const int FirstFarmer2 = 0x60;
    public const int LastFarmer2 = 0x62;
    /// <summary>A miner at the rock ("Still no gold!"), and striking it rich ("Eureka!").</summary>
    public const int FirstMiner = 0x42;
    public const int LastMiner = 0x44;
    public const int FirstMinerGold = 0x45;
    public const int LastMinerGold = 0x47;
    /// <summary>Street maintenance: lamps lit and put out, a candle replaced, shutters closed and opened.</summary>
    public const int FirstLampOn = 0x63;
    public const int LastLampOn = 0x66;
    public const int LampOff = 0x67;
    public const int NewCandle = 0x68;
    public const int FirstCloseShutters = 0x71;
    public const int LastCloseShutters = 0x73;
    public const int FirstOpenShutters = 0x74;
    public const int LastOpenShutters = 0x76;
    /// <summary>Combat: coming to help a protected party member ("On my way!").</summary>
    public const int FirstWillHelp = 0x34;
    public const int LastWillHelp = 0x36;
    /// <summary>Combat: running away ("Aiiieeee!"), or now and then something longer.</summary>
    public const int FleeScreaming = 0x37;
    public const int FirstFlee = 0x48;
    public const int LastFlee = 0x52;
    /// <summary>Combat: charging in ("To Battle!") and taunting ("Take this!").</summary>
    public const int FirstToBattle = 0x39;
    public const int LastToBattle = 0x3b;
    public const int FirstTaunt = 0x53;
    public const int LastTaunt = 0x59;
    /// <summary>A waiter taking an order ("What wilt thou have?").</summary>
    public const int FirstWaiterAsk = 0x1b;
    public const int LastWaiterAsk = 0x1f;
    /// <summary>Asking for food at the table ("Waiter!").</summary>
    public const int FirstMoreFood = 0x20;
    public const int LastMoreFood = 0x24;
    /// <summary>Eating ("Mmmmm...").</summary>
    public const int FirstMunch = 0x25;
    public const int LastMunch = 0x28;
    /// <summary>A sleeper woken by the avatar ("Who goes there?").</summary>
    public const int FirstAwakened = 0x95;
    public const int LastAwakened = 0x9a;
    /// <summary>Exult's: someone moved the chair an NPC was going to sit on.</summary>
    public const int FirstChairThief = 0x100;
    public const int LastChairThief = 0x104;
    /// <summary>Exult's: a waiter's chat and serving lines.</summary>
    public const int FirstWaiterBanter = 0x105;
    public const int LastWaiterBanter = 0x107;
    public const int FirstWaiterServe = 0x108;
    public const int LastWaiterServe = 0x109;

    /// <summary>A party member, when the avatar would nap in someone's bed (Exult <c>first_bed_occupied</c>).</summary>
    public const int FirstBedOccupied = 0x10a;
    public const int LastBedOccupied = 0x10c;

    /// <summary>Exult's own messages (data/exultmsg.txt, 0x500 on), as far as they are used here.</summary>
    static readonly Dictionary<int, string> ExultMessages = new()
    {
        [0x100] = "Put that chair back!",
        [0x101] = "Thief!!",
        [0x102] = "Thou scoundrel!!",
        [0x103] = "Not funny!",
        [0x104] = "Who moved my chair??",
        [0x105] = "You look like you're doing fine.",
        [0x106] = "Everything okay?",
        [0x107] = "Ready for dessert?",
        [0x108] = "Enjoy!",
        [0x109] = "Specialty of the house!",
        [0x10a] = "Avatar!  Please restrain thyself!",
        [0x10b] = "Hast thou noticed that this bed is occupied?",
        [0x10c] = "The resident of this bed may not be desirouth of company at the moment.",
        // The endgame (Exult end_game, show_congratulations).
        [0x113] = "THE END OF ULTIMA VII",
        [0x114] = "THE END OF BRITANNIA AS YOU KNOW IT...",
        [0x115] = "No. You cannot do that! You must not!",
        [0x116] = "Damn you Avatar!  Damn you!",
        [0x117] = "The Black Gate is destroyed.",
        [0x118] = "The Guardian has been stopped.",
        [0x119] = "Avatar! You think you have won?",
        [0x11A] = "Think again! You are unable to",
        [0x11B] = "leave Britannia, whereas I am free",
        [0x11C] = "to enter other worlds!",
        [0x11D] = "Perhaps your puny Earth shall be",
        [0x11E] = "my NEXT target!",
        [0x11F] = "",
        [0x120] = "",
        [0x121] = "In the months following the climactic",
        [0x122] = "battle at The Black Gate, Britannia",
        [0x123] = "is set upon the long road to recovery",
        [0x124] = "from its various plights.",
        [0x125] = "",
        [0x126] = "Upon your return to Britain,",
        [0x127] = "Lord British decreed that",
        [0x128] = "The Fellowship be outlawed",
        [0x129] = "and all of the branches were",
        [0x12A] = "soon destroyed.",
        [0x12B] = "",
        [0x12C] = "The frustration you feel at having been",
        [0x12D] = "stranded in Britannia is somewhat",
        [0x12E] = "alleviated by the satisfaction that you",
        [0x12F] = "solved the gruesome murders committed",
        [0x130] = "by The Fellowship and even avenged the",
        [0x131] = "death of Spark's father.",
        [0x132] = "",
        [0x133] = "",
        [0x134] = "",
        [0x135] = "And although you are, at the moment,",
        [0x136] = "helpless to do anything about",
        [0x137] = "The Guardian's final threat,",
        [0x138] = "another thought nags at you...",
        [0x139] = "what became of Batlin, the fiend",
        [0x13A] = "who got away?",
        [0x13B] = "",
        [0x13C] = "",
        [0x13D] = "That is another story...",
        [0x13E] = "one that will take you",
        [0x13F] = "to a place called",
        [0x140] = "The Serpent Isle...",
        [0x141] = "",
        [0x142] = "Congratulations!",
        [0x143] = "You have completed Ultima VII in",
        [0x144] = " & |only |exactly | year| years| month| months| day| days| hour| hours|negative time!",
        [0x145] = "Please write Lord British,",
        [0x146] = "@RichardGarriott on Twitter,",
        [0x147] = "telling him of your",
        [0x148] = "accomplishment!",
        [0x149] = "",
        [0x14A] = ""
    };

    // The endgame's messages (Exult shapes/items.h).
    public const int EndOfUltima7 = 0x113, EndOfBritannia = 0x114, YouCannotDoThat = 0x115, DamnAvatar = 0x116,
        BlackgateDestroyed = 0x117, GuardianHasStopped = 0x118, TextScreen0 = 0x119, TextScreen1 = 0x121,
        TextScreen2 = 0x12C, TextScreen3 = 0x135, TextScreen4 = 0x13D, Congrats = 0x142;

    static FlexFile? _flex;

    /// <summary>A random message from <paramref name="first"/> to <paramref name="last"/> (Exult <c>Actor::say(from, to)</c>).</summary>
    public static string Random(int first, int last) => Get(first + System.Random.Shared.Next(last - first + 1));

    /// <summary>Exult <c>get_misc_name</c>: TEXT.FLX entries from 0x500 on (reagents, "Circle", ...).</summary>
    public static string MiscName(int num) => Entry(0x500 + num);

    public static string Get(int msg)
    {
        if (msg >= 0x100)
        {
            return ExultMessages.GetValueOrDefault(msg, "");
        }

        return Entry(0x400 + msg);
    }

    static string Entry(int index)
    {
        _flex ??= new FlexFile(Path.Combine(U7Paths.StaticDir, "TEXT.FLX"));
        if (index >= _flex.Count)
        {
            return "";
        }

        var data = _flex.Get(index);
        var end = data.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end >= 0 ? data[..end] : data);
    }
}
