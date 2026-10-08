using U7.Core;
using U7.Rendering;

namespace U7.World;

/// <summary>Exult <c>Game_clock</c> without hunger or light sources.</summary>
public sealed class GameClock
{
    public int Hour { get; private set; } = 6;
    public int Minute { get; private set; }
    public int Ticks { get; private set; }
    public int Day { get; private set; }
    public int TimeRate { get; set; } = 1;
    public int Slot => Hour / 3;
    /// <summary>Exult <c>Game_clock::get_total_hours</c>.</summary>
    public int TotalHours => Day * 24 + Hour;
    /// <summary>Exult <c>Game_clock::get_total_minutes</c>.</summary>
    public int TotalMinutes => TotalHours * 60 + Minute;
    /// <summary>Exult <c>Game_window::special_light</c>: the game minute a light spell ends, 0 for none.</summary>
    public int SpecialLight { get; set; }
    /// <summary>Exult <c>Game_clock::overcast</c>: the clouds that make the day overcast; overcast while above 0.</summary>
    public int Overcast { get; private set; }
    /// <summary>Exult <c>Game_clock::fog</c>: the fogs going on.</summary>
    public int Fog { get; private set; }
    public bool Cloudy => Overcast > 0;
    /// <summary>A fog is going on, and it is between 6:00 and 20:59 (Exult's "Disable fog at night???").</summary>
    public bool Foggy => Fog > 0 && Hour is >= 6 and <= 20;

    public event Action<int>? HourChanged;
    public event Action<int>? SlotChanged;

    double _accum;

    /// <summary>
    /// Exult <c>get_time_palette</c>: the palette for an hour (24 is the
    /// next day's 0), night all day in a dungeon.
    /// </summary>
    public static int PaletteForHour(int hour, bool dungeon)
    {
        if (dungeon || hour < 5)
        {
            return PaletteSet.Night;
        }

        if (hour == 5)
        {
            return PaletteSet.Dawn;
        }

        if (hour < 20)
        {
            return PaletteSet.Day;
        }

        return hour == 20 ? PaletteSet.Dusk : PaletteSet.Night;
    }

    /// <summary>Exult <c>is_dark_palette</c>: dusk (which is also dawn) and night.</summary>
    public static bool IsDarkPalette(int pal) => pal is PaletteSet.Dusk or PaletteSet.Night;

    /// <summary>
    /// Exult <c>Game_clock::set_time_palette</c>'s <c>Palette_transition</c>:
    /// through each hour the palette goes from that hour's to the next
    /// hour's. Exult steps once a game minute; <c>T</c> runs smoothly with
    /// the clock's ticks.
    /// </summary>
    public (int From, int To, float T) PaletteBlend(bool dungeon) =>
        (PaletteForHour(Hour, dungeon), PaletteForHour(Hour + 1, dungeon),
            (Minute + Ticks / (float)U7Constants.TicksPerMinute) / 60f);

    /// <summary>
    /// Exult <c>get_final_palette</c>: a light spell shows while either end
    /// of the hour's blend is dark (Exult blends to or from PALETTE_SPELL
    /// then; here the spell is a light round the avatar). A dungeon is dark.
    /// </summary>
    public bool LightSpellShows(bool dungeon)
    {
        if (SpecialLight == 0)
        {
            return false;
        }

        var (from, to, _) = PaletteBlend(dungeon);
        return IsDarkPalette(from) || IsDarkPalette(to);
    }

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

        // Exult Game_render::paint_map: the light spell ends.
        if (SpecialLight != 0 && TotalMinutes > SpecialLight)
        {
            SpecialLight = 0;
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

    /// <summary>Exult <c>Game_window::add_special_light</c>: Light is 500 units, Great Light 5000, a minute per 20.</summary>
    public void AddSpecialLight(int units)
    {
        if (SpecialLight == 0)
        {
            SpecialLight = TotalMinutes;
        }

        SpecialLight += units / 20;
    }

    /// <summary>Exult <c>Game_clock::set_overcast</c>: cloud cover starts or ends.</summary>
    public void SetOvercast(bool on) => Overcast += on ? 1 : -1;

    /// <summary>
    /// Exult <c>Game_clock::set_fog</c>: a fog starts or ends. Exult sets the
    /// count to 0 when this happens before 6:00 or after 20:59, so a fog
    /// started at night and ended by day leaves -1 and cancels the next one;
    /// here the count stays true and <see cref="Foggy"/> applies the hours
    /// (the user's pick). Exult shows fog only on the dawn and day palettes
    /// anyway; the one change is that a night fog still going at 6:00 shows.
    /// </summary>
    public void SetFog(bool on) => Fog += on ? 1 : -1;

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
