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
        [0x109] = "Specialty of the house!"
    };

    static FlexFile? _flex;

    /// <summary>A random message from <paramref name="first"/> to <paramref name="last"/> (Exult <c>Actor::say(from, to)</c>).</summary>
    public static string Random(int first, int last) => Get(first + System.Random.Shared.Next(last - first + 1));

    public static string Get(int msg)
    {
        if (msg >= 0x100)
        {
            return ExultMessages.GetValueOrDefault(msg, "");
        }

        _flex ??= new FlexFile(Path.Combine(U7Paths.StaticDir, "TEXT.FLX"));
        var index = 0x400 + msg;
        if (index >= _flex.Count)
        {
            return "";
        }

        var data = _flex.Get(index);
        var end = data.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end >= 0 ? data[..end] : data);
    }
}
