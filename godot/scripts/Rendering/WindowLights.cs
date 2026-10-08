using Godot;
using U7.Core;
using U7.Data;

namespace U7.Rendering;

/// <summary>
/// Light from rooms a painted roof hides, through their windows (cosmetic,
/// the user's wish; Exult has no pools and shows nothing through glass). A
/// window whose room is roofed from view gathers what lights the room: light
/// sources, carried lights, magic objects, blasts, magic missiles and
/// vortices under the same roof, within <see cref="ReachTiles"/>, with no
/// wall between them (walkers don't count). Its panes glow in that light and
/// a fan of it falls on the ground outside; the world shader draws both and
/// keeps them off roofs. Hidden effects show through their room's panes
/// (<see cref="PanesFor"/>).
/// </summary>
public sealed class WindowLights
{
    /// <summary>The world shader's kind for a window facing south; east, north and west follow.</summary>
    public const int KindWindow = 2;
    public const int South = 0;
    public const int East = 1;
    public const int North = 2;
    public const int West = 3;
    /// <summary>How far a room's lights reach its windows, in tiles.</summary>
    const float ReachTiles = 9f;
    /// <summary>The most windows a frame tracks (a pane's window is a byte).</summary>
    const int MaxWindows = 255;

    /// <summary>
    /// How window light looks: <see cref="Gain"/> how strongly a room's light
    /// shows at its windows, <see cref="Spill"/> how far the fan reaches
    /// (tiles), <see cref="Spread"/> how much wider it gets per pixel out and
    /// <see cref="Light"/> its brightness.
    /// </summary>
    public readonly record struct WindowLook(float Gain, float Spill, float Spread, float Light);

    /// <summary>
    /// The user's picks (2026-10-08) from shots at night: medium strength (of
    /// subtle, medium, strong) and a medium fan (of 3 tiles narrow, 4, and 6
    /// wide); panes keep the glass's colours, lit by the room (not
    /// candlelight); north and west fans reach past the roof that hides
    /// their start (not the true projection, which left only a faint end).
    /// </summary>
    public static WindowLook Look { get; } = new(2.5f, 4f, 0.6f, 0.9f);

    /// <summary>Glass: windows, stained glass and glass walls, whose see-through pixels are panes.</summary>
    public static bool IsGlass(int shape) =>
        shape is 438 or 732 or 508 or 512 or 373 or 832 or 833 or 841 or 996 or 1005 or 1007 or 1019;

    /// <summary>Glass, and the open shutters (an opening without glass).</summary>
    public static bool IsWindow(int shape) => IsGlass(shape) || shape is 322 or 372;

    sealed class Window
    {
        public required U7Object Obj;
        /// <summary>The way it faces, out of its room: <see cref="South"/> ... <see cref="West"/>.</summary>
        public int Dir;
        /// <summary>The roof over its room (<see cref="GameMap.CoverAbove"/>).</summary>
        public int Cover;
        public bool Glass;
        public bool AlongX;
        public int Length;
        public byte Slot;
        /// <summary>The middle of its outer face, in world pixels.</summary>
        public Vector2 Pane;
        /// <summary>How far down (in world pixels along x and y) the ground outside is from <see cref="Pane"/>.</summary>
        public float Drop;
        /// <summary>How much of a north or west fan the roof hides on screen, in tiles.</summary>
        public float BehindRoof;
        public float Sum;
        public Vector3 Colour;
    }

    readonly record struct Hidden(int Tx, int Ty, int Cover, float Intensity, Vector3 Colour);

    readonly GameMap _map;
    readonly List<Window> _windows = new();
    readonly List<Window> _spare = new();
    readonly Dictionary<U7Object, byte> _slots = new();
    readonly List<Hidden> _hidden = new();
    int _skip = U7Constants.NoRoof;

    public WindowLights(GameMap map) => _map = map;

    /// <summary>Windows tracked this frame, and how many of them shine.</summary>
    public int Count => _windows.Count;
    public int Lit { get; private set; }

    /// <summary>A new frame: the roofs at and above <paramref name="skip"/> are not painted.</summary>
    public void Begin(int skip)
    {
        _skip = skip;
        _spare.AddRange(_windows);
        _windows.Clear();
        _slots.Clear();
        _hidden.Clear();
        Lit = 0;
    }

    /// <summary>
    /// A painted window: kept if one side of it is under a roof and the other
    /// isn't, and that roof is painted (else the room's lights show as they are).
    /// </summary>
    public void AddWindow(U7Object w)
    {
        if (_windows.Count >= MaxWindows)
        {
            return;
        }

        var alongX = w.DimX > w.DimY;
        var len = alongX ? w.DimX : w.DimY;
        var mx = alongX ? w.Tx - len / 2 : w.Tx;
        var my = alongX ? w.Ty : w.Ty - len / 2;
        var southEast = alongX ? _map.CoverAbove(mx, w.Ty + 1, w.Tz) : _map.CoverAbove(w.Tx + 1, my, w.Tz);
        var northWest = alongX ? _map.CoverAbove(mx, w.Ty - 1, w.Tz) : _map.CoverAbove(w.Tx - 1, my, w.Tz);
        int dir;
        int cover;
        if (northWest < U7Constants.NoRoof && southEast == U7Constants.NoRoof)
        {
            dir = alongX ? South : East;
            cover = northWest;
        }
        else if (southEast < U7Constants.NoRoof && northWest == U7Constants.NoRoof)
        {
            dir = alongX ? North : West;
            cover = southEast;
        }
        else
        {
            return;
        }

        if (cover >= _skip)
        {
            return;
        }

        // The middle of the outer face, at lift 0 and then at the window's middle height.
        const int tile = U7Constants.TileSize;
        float fx;
        float fy;
        if (alongX)
        {
            fx = (w.Tx - len / 2f + 1) * tile;
            fy = dir == South ? (w.Ty + 1) * tile : w.Ty * tile;
        }
        else
        {
            fx = dir == East ? (w.Tx + 1) * tile : w.Tx * tile;
            fy = (w.Ty - len / 2f + 1) * tile;
        }

        var (ox, oy) = Outward(dir);
        var ground = Ground(mx + ox, my + oy, w.Tz);
        var lift = w.Tz + w.DimZ / 2f;
        var window = _spare.Count > 0 ? _spare[^1] : new Window { Obj = w };
        if (_spare.Count > 0)
        {
            _spare.RemoveAt(_spare.Count - 1);
        }

        window.Obj = w;
        window.Dir = dir;
        window.Cover = cover;
        window.Glass = IsGlass(w.Shape);
        window.AlongX = alongX;
        window.Length = len;
        window.Slot = (byte)(_windows.Count + 1);
        window.Pane = new Vector2(fx - 4 * lift, fy - 4 * lift);
        window.Drop = 4 * (lift - ground);
        // On screen the roof covers the ground beyond a north or west wall, half a
        // tile a lift; their fans reach that much farther, so as much of them shows.
        window.BehindRoof = dir is North or West ? (cover - ground) / 2f : 0f;
        window.Sum = 0;
        window.Colour = Vector3.Zero;
        _windows.Add(window);
        _slots[w] = window.Slot;
    }

    /// <summary>A light the roof hides, at its tile, as bright as it shows now.</summary>
    public void AddHidden(int tx, int ty, int tz, float intensity, Vector3 colour)
    {
        if (intensity > 0)
        {
            _hidden.Add(new Hidden(tx, ty, _map.CoverAbove(tx, ty, tz), intensity, colour));
        }
    }

    /// <summary>
    /// Each window's light from its room, as world-shader lights in
    /// <paramref name="found"/>: <paramref name="darkness"/> (0 by day, 1 at
    /// night) scales them, as windows only show against the dark.
    /// </summary>
    public void Finish(float darkness, Vector2 mid, List<SceneLighting.SceneLight> found)
    {
        if (darkness <= 0)
        {
            return;
        }

        var look = Look;
        foreach (var w in _windows)
        {
            foreach (var h in _hidden)
            {
                var d = InRoom(w, h.Tx, h.Ty, h.Cover);
                if (d < 0)
                {
                    continue;
                }

                var weight = h.Intensity * (1f - Mathf.SmoothStep(0f, 1f, d / ReachTiles));
                w.Sum += weight;
                w.Colour += weight * h.Colour;
            }

            var strength = Mathf.Min(1f, w.Sum * look.Gain) * darkness;
            if (strength < 0.02f)
            {
                continue;
            }

            Lit++;
            var colour = w.Colour / w.Sum;
            var radius = (look.Spill + w.BehindRoof) * U7Constants.TileSize;
            var glow = new Glow(GlowKind.Window, look.Spill, strength * look.Light, colour);
            var score = glow.Intensity * radius / (1f + w.Pane.DistanceTo(mid) / 200f);
            // The shader's spare value: the drop to the ground (x 256) and half the window's length.
            var packed = Mathf.Round(w.Drop) * 256 + w.Length * U7Constants.TileSize / 2;
            found.Add(new SceneLighting.SceneLight(w.Obj, w.Obj.Shape, w.Obj.Frame, 0, glow, w.Pane, radius, score, false,
                KindWindow + w.Dir, packed));
        }
    }

    /// <summary>The window's number in the pane plane (<see cref="IndexBuffer8.Panes"/>), 0 if it isn't tracked.</summary>
    public byte SlotOf(U7Object window) => _slots.TryGetValue(window, out var slot) ? slot : (byte)0;

    /// <summary>
    /// The panes something hidden at <paramref name="at"/> shows through:
    /// marks in <paramref name="windows"/> (indexed by slot) the glass of its
    /// room facing south or east, whose panes are seen. False if none.
    /// </summary>
    public bool PanesFor(TileCoord at, Span<bool> windows)
    {
        windows.Clear();
        var cover = _map.CoverAbove(at.Tx, at.Ty, at.Tz);
        var any = false;
        foreach (var w in _windows)
        {
            if (w.Glass && w.Dir is South or East && InRoom(w, at.Tx, at.Ty, cover) >= 0)
            {
                windows[w.Slot] = true;
                any = true;
            }
        }

        return any;
    }

    /// <summary>
    /// How far (in tiles) a tile in the room is from the window, or -1 if it
    /// isn't in the room: under the same roof, inside the wall, within
    /// <see cref="ReachTiles"/> of the room's tile in front of the window,
    /// with nothing solid but walkers on the way there at the window's height.
    /// </summary>
    float InRoom(Window w, int tx, int ty, int cover)
    {
        var o = w.Obj;
        if (cover != w.Cover)
        {
            return -1;
        }

        var outside = w.Dir switch
        {
            South => ty > o.Ty,
            North => ty < o.Ty,
            East => tx > o.Tx,
            _ => tx < o.Tx,
        };
        if (outside)
        {
            return -1;
        }

        var nx = w.AlongX ? Math.Clamp(tx, o.Tx - w.Length + 1, o.Tx) : o.Tx;
        var ny = w.AlongX ? o.Ty : Math.Clamp(ty, o.Ty - w.Length + 1, o.Ty);
        var (ox, oy) = Outward(w.Dir);
        nx -= ox;
        ny -= oy;
        var d = new Vector2(nx - tx, ny - ty).Length();
        return d <= ReachTiles && ClearLine(tx, ty, nx, ny, o.Tz + Math.Max(1, o.DimZ / 2)) ? d : -1;
    }

    /// <summary>Nothing solid but walkers on the tiles from a light (left out) to a tile (included), at the lift.</summary>
    bool ClearLine(int x0, int y0, int x1, int y1, int lift)
    {
        var steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        for (var i = 1; i <= steps; i++)
        {
            var x = x0 + (int)MathF.Round((x1 - x0) * i / (float)steps);
            var y = y0 + (int)MathF.Round((y1 - y0) * i / (float)steps);
            if (_map.StaticBlocked(x, y, lift))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Where the ground is outside a window, below its lift: on the highest solid thing there, else at lift 0.</summary>
    int Ground(int tx, int ty, int below)
    {
        for (var lift = below - 1; lift >= 0; lift--)
        {
            if (_map.StaticBlocked(tx, ty, lift))
            {
                return lift + 1;
            }
        }

        return 0;
    }

    static (int X, int Y) Outward(int dir) => dir switch
    {
        South => (0, 1),
        North => (0, -1),
        East => (1, 0),
        _ => (-1, 0),
    };
}
