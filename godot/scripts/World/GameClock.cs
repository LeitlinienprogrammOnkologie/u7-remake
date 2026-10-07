using Godot;
using U7.Core;

namespace U7.World;

/// <summary>Exult <c>Game_clock</c> without hunger / dungeon lights.</summary>
public sealed class GameClock
{
    public const int PaletteDay = 0;
    public const int PaletteDusk = 1;
    public const int PaletteNight = 2;

    public int Hour { get; private set; } = 6;
    public int Minute { get; private set; }
    public int Ticks { get; private set; }
    public int Day { get; private set; }
    public int TimeRate { get; set; } = 1;
    public int Slot => Hour / 3;
    /// <summary>Exult <c>Game_clock::get_total_hours</c>.</summary>
    public int TotalHours => Day * 24 + Hour;

    public event Action<int>? HourChanged;
    public event Action<int>? SlotChanged;

    double _accum;

    public int TimePalette
    {
        get
        {
            var h = Hour + 1;
            if (h < 5 || h > 20)
            {
                return PaletteNight;
            }

            if (h == 5 || h == 20)
            {
                return PaletteDusk;
            }

            return PaletteDay;
        }
    }

    public Color WorldModulate => TimePalette switch
    {
        PaletteNight => new Color(0.28f, 0.34f, 0.58f),
        PaletteDusk => new Color(0.95f, 0.62f, 0.38f),
        _ => Colors.White
    };

    public void Update(double delta)
    {
        _accum += delta;
        var step = U7Constants.StandardDelayMs / 1000.0;
        while (_accum >= step)
        {
            _accum -= step;
            AdvanceTick();
        }
    }

    public void AdvanceTick()
    {
        var oldHour = Hour;
        var oldSlot = Slot;
        Ticks += TimeRate;
        Minute += Ticks / U7Constants.TicksPerMinute;
        Ticks %= U7Constants.TicksPerMinute;
        while (Minute >= 60)
        {
            Minute -= 60;
            Hour++;
            if (Hour >= 24)
            {
                Hour -= 24;
                Day++;
            }
        }

        if (Hour != oldHour)
        {
            HourChanged?.Invoke(Hour);
            if (Slot != oldSlot)
            {
                SlotChanged?.Invoke(Slot);
            }
        }
    }

    /// <summary>Restore the time of day from a saved game.</summary>
    public void Set(int day, int hour, int minute)
    {
        Day = day;
        Hour = Math.Clamp(hour, 0, 23);
        Minute = Math.Clamp(minute, 0, 59);
        Ticks = 0;
    }

    public void SkipHours(int delta)
    {
        var oldSlot = Slot;
        var hours = Hour + delta;
        while (hours < 0)
        {
            hours += 24;
            Day = Math.Max(0, Day - 1);
        }

        Day += hours / 24;
        Hour = hours % 24;
        Minute = 0;
        Ticks = 0;
        HourChanged?.Invoke(Hour);
        if (Slot != oldSlot)
        {
            SlotChanged?.Invoke(Slot);
        }
    }

    public string HudText() => $"Day {Day}  {Hour:00}:{Minute:00}";
}
