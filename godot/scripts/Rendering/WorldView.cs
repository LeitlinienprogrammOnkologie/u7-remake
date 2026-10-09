using System.Collections.Generic;
using Godot;
using U7.Core;
using U7.Data;

namespace U7.Rendering;

/// <summary>
/// Paints the visible map as Exult does, into an 8-bit buffer of palette
/// indices (<see cref="IndexBuffer8"/>, Exult's <c>Image_buffer8</c>), shown
/// as one texture through a palette shader that also does the lighting
/// (<see cref="SceneLighting"/>: each pixel between its ambient and its lit
/// colour by the light falling on it). Screen mapping is Exult's:
/// <c>x = (tx+1)*8 - 1 - 4*tz</c> is a frame's hotspot.
/// </summary>
public partial class WorldView : Node2D
{
    const string ShaderCode = """
        shader_type canvas_item;
        render_mode unshaded;

        const int MAX_LIGHTS = 64;
        const int KIND_SPELL = 1;
        // A window facing south; east, north and west follow (WindowLights).
        const int KIND_WINDOW = 2;

        // Row 0 the ambient palette, row 1 the lit one (SceneLighting).
        uniform sampler2D palette_tex : filter_nearest;
        // Per light: xy centre in world pixels, z radius, w kind.
        uniform vec4 light_a[MAX_LIGHTS];
        // Per light: rgb colour times intensity, a phase.
        uniform vec4 light_b[MAX_LIGHTS];
        uniform int light_count = 0;
        // How much brighter than its colour a glowing pixel is (GlowTable.Magic.Day).
        uniform float glow_boost = 0.0;
        // Window light (WindowLights.Look): how much wider its fan gets per pixel out.
        uniform float window_spread = 0.6;
        // Cloud shadows (SceneLighting): drift in world pixels, how much ground
        // they cover, how dark, their colour as a multiplier, size, edge softness.
        uniform vec2 cloud_offset = vec2(0.0);
        uniform float cloud_cover = 0.0;
        uniform float cloud_shadow = 0.0;
        uniform vec3 cloud_tint = vec3(0.68, 0.64, 0.6);
        uniform float cloud_scale = 240.0;
        uniform float cloud_soft = 0.12;
        // The fog's mist: thickness, drift in world pixels, colour, size.
        uniform float mist = 0.0;
        uniform vec2 mist_offset = vec2(0.0);
        uniform vec3 mist_colour = vec3(0.75);
        uniform float mist_scale = 220.0;

        varying vec4 tint;
        varying vec2 world_pos;

        void vertex() {
            // The draw's modulate (WorldView.Modulate). In fragment() COLOR is
            // already multiplied by the texture, which would mangle the indices.
            tint = COLOR;
            world_pos = VERTEX;
        }

        // The light spell as LightSpellOverlay drew it: a breathing circle with
        // ripples running round the rim, a faint blue band at the rim and a slow
        // blue pulse inside. Returns its weight and the colour it multiplies by.
        float spell(vec2 d, float radius, float t, out vec3 colour) {
            float ang = atan(d.y, d.x);
            float r = radius * (1.0 + 0.05 * sin(t * 1.9)
                + 0.025 * sin(ang * 5.0 + t * 1.3)
                + 0.015 * sin(ang * 11.0 - t * 2.7));
            float dist = length(d);
            float pulse = 0.5 + 0.5 * sin(t * 1.1);
            vec3 inner = mix(vec3(1.0), vec3(0.88, 0.93, 1.0), 0.35 * pulse);
            vec3 rim = vec3(0.62, 0.74, 1.0);
            float edge = 1.0 - smoothstep(r * 0.78, r, dist);
            float core = 1.0 - smoothstep(r * 0.4, r * 0.8, dist);
            // The overlay's mix(mix(outside, rim, edge), inner, core) as one weight and colour.
            float w = 1.0 - (1.0 - edge) * (1.0 - core);
            colour = w > 0.0 ? (rim * edge * (1.0 - core) + inner * core) / w : vec3(1.0);
            return w;
        }

        // A roofed room's light through a window: d from the middle of its outer
        // face, dir the way it faces, reach how far the fan goes; packed holds
        // the drop to the ground outside (x 256) and half the window's length.
        // A pane of it glows; elsewhere a fan widens out from the wall's foot.
        float window_light(vec2 d, int dir, float reach, float packed, bool pane) {
            float drop = floor(packed / 256.0);
            float half_len = packed - drop * 256.0;
            bool along_x = dir == 0 || dir == 2;
            if (pane) {
                // On a south or north face a lift moves x and y alike, so x - y runs along it.
                float along = along_x ? d.x - d.y : d.y - d.x;
                float up = along_x ? d.y : d.x;
                return abs(up) > 14.0 ? 0.0 : 1.0 - smoothstep(half_len - 1.0, half_len + 2.0, abs(along));
            }

            vec2 n = dir == 0 ? vec2(0.0, 1.0) : dir == 1 ? vec2(1.0, 0.0) : dir == 2 ? vec2(0.0, -1.0) : vec2(-1.0, 0.0);
            vec2 f = d - vec2(drop);
            float u = dot(f, n);
            float v = along_x ? f.x : f.y;
            float width = half_len + max(u, 0.0) * window_spread;
            return smoothstep(-2.0, 4.0, u) * (1.0 - smoothstep(0.0, reach, u))
                * (1.0 - smoothstep(width * 0.7, width * 1.15, abs(v)));
        }

        float hash(vec2 p) {
            vec3 p3 = fract(vec3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return fract((p3.x + p3.y) * p3.z);
        }

        float value_noise(vec2 p) {
            vec2 i = floor(p);
            vec2 f = fract(p);
            vec2 u = f * f * (3.0 - 2.0 * f);
            return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x),
                mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
        }

        // Five octaves, about 0 to 1 round 0.5.
        float fbm(vec2 p) {
            float v = 0.0;
            float a = 0.5;
            for (int i = 0; i < 5; i++) {
                v += a * value_noise(p);
                p = p * 2.03 + vec2(17.1, 9.2);
                a *= 0.5;
            }
            return v / 0.97;
        }

        void fragment() {
            ivec2 size = textureSize(TEXTURE, 0);
            ivec2 at = min(ivec2(UV * vec2(size)), size - 1);
            vec2 texel = texelFetch(TEXTURE, at, 0).rg;
            int index = int(texel.r * 255.0 + 0.5);
            // The glow plane: six bits of glow, a roof mark and a pane mark (IndexBuffer8).
            int g = int(texel.g * 255.0 + 0.5);
            float glow = float(g >> 2) / 63.0;
            bool roof = (g & 1) != 0;
            bool pane = (g & 2) != 0;
            vec3 ambient = texelFetch(palette_tex, ivec2(index, 0), 0).rgb;
            vec3 lit = texelFetch(palette_tex, ivec2(index, 1), 0).rgb;

            // L: how much light falls here; tint_sum: its colours, weighted.
            float L = 0.0;
            vec3 tint_sum = vec3(0.0);
            for (int i = 0; i < light_count; i++) {
                vec4 a = light_a[i];
                vec4 b = light_b[i];
                float intensity = max(b.r, max(b.g, b.b));
                if (intensity <= 0.0) {
                    continue;
                }

                vec3 colour = b.rgb / intensity;
                float w;
                int kind = int(a.w + 0.5);
                if (kind == KIND_SPELL) {
                    w = spell(world_pos - a.xy, a.z, TIME + b.a, colour);
                } else if (kind >= KIND_WINDOW) {
                    if (roof) {
                        continue;
                    }

                    w = window_light(world_pos - a.xy, kind - KIND_WINDOW, a.z, b.a, pane);
                    if (w <= 0.0) {
                        continue;
                    }
                } else {
                    float x = length(world_pos - a.xy) / a.z;
                    if (x >= 1.0) {
                        continue;
                    }

                    w = 1.0 - smoothstep(0.0, 1.0, x);
                }

                w *= intensity;
                L += w;
                tint_sum += w * colour;
            }

            // Clouds shade the daylight, not the lights.
            if (cloud_shadow > 0.0) {
                // Exult's cloud shapes are about twice as wide as high.
                vec2 p = (world_pos - cloud_offset) / cloud_scale * vec2(0.5, 1.0);
                float threshold = mix(0.72, 0.28, cloud_cover);
                float shade = smoothstep(threshold - cloud_soft, threshold + cloud_soft, fbm(p));
                ambient *= mix(vec3(1.0), cloud_tint, shade * cloud_shadow);
            }

            float total = L + glow;
            vec3 light_tint = total > 0.0 ? (tint_sum + vec3(glow)) / total : vec3(1.0);
            vec3 rgb = mix(ambient, lit * light_tint, clamp(total, 0.0, 1.0));
            rgb *= 1.0 + glow * glow_boost;
            // The fog's mist veils everything: two layers drifting apart.
            if (mist > 0.0) {
                vec2 p = (world_pos - mist_offset) / mist_scale;
                float m = 0.6 * fbm(p * vec2(0.6, 1.0)) + 0.4 * fbm(p * 1.7 + mist_offset / mist_scale * 0.6);
                rgb = mix(rgb, mist_colour, mist * smoothstep(0.3, 0.75, m));
            }
            COLOR = vec4(rgb, 1.0) * tint;
        }
        """;

    /// <summary>About as many terrains' flats as Exult keeps rendered.</summary>
    const int MaxCachedTerrains = 256;

    public GameMap Map = null!;
    public ShapeCatalog Catalog = null!;
    public ShapeCache Shapes = null!;
    public U7Object Avatar = null!;
    public U7.World.EffectsManager? Effects;
    /// <summary>Missiles in flight (Exult's projectile effects), painted after the map.</summary>
    public IReadOnlyList<U7.Actors.Missile>? Missiles;
    /// <summary>Death vortices and energy mists (Exult's homing projectiles), painted after the map.</summary>
    public IReadOnlyList<U7.Actors.HomingMissile>? HomingMissiles;
    /// <summary>The palettes and lights to show the frame with.</summary>
    public SceneLighting? Lighting;
    public int SkipAboveLift = U7Constants.NoRoof;
    /// <summary>The view shows the world elsewhere (Exult <c>display_area</c>, <c>paint_map_at_tile</c>).</summary>
    public bool RemoteView;
    public const int RemoteViewSkipLift = 4;
    public int InDungeonLift;
    /// <summary>
    /// Whether the palette's colours cycle. Exult rotates them in its main
    /// loop, in modal gumps and while conversations and books wait for a
    /// click, but not in its other click waits (<c>Get_click</c> without
    /// <c>rotate_colors</c>), such as picking a target.
    /// </summary>
    public bool RotateColors = true;
    /// <summary>
    /// Keep showing the last frame. While Exult fades out, its game stands
    /// still, and once the screen is black what it paints is black; so a
    /// teleport after a fade-out is not seen before the fade-in.
    /// </summary>
    public bool Frozen;

    readonly IndexBuffer8 _buffer = new();
    /// <summary>Scratch for <see cref="PaintThroughWindows"/>: the windows (by number) an effect shows through.</summary>
    readonly bool[] _paneWindows = new bool[256];
    readonly Dictionary<int, LinkedListNode<(int Terrain, byte[] Pixels)>> _flats = new();
    readonly LinkedList<(int Terrain, byte[] Pixels)> _flatOrder = new();
    WorldPalette _palette = null!;
    ShaderMaterial _material = null!;
    Image? _image;
    ImageTexture? _texture;
    byte[] _upload = [];
    /// <summary>The world pixel at the buffer's top left.</summary>
    int _originX;
    int _originY;
    int _paletteVersion = -1;
    uint _renderSeq;
    uint _frameNo;
    int _paintCounter;

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        ZIndex = 0;
        _palette = new WorldPalette(Shapes.DayPalette);
        _material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        _material.SetShaderParameter("palette_tex", _palette.Texture);
        Material = _material;
    }

    public override void _Process(double delta)
    {
        if (!Frozen)
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (Map is null || Avatar is null)
        {
            return;
        }

        if (Frozen && _texture is not null)
        {
            DrawTexture(_texture, new Vector2(_originX, _originY));
            return;
        }

        // Exult display_area paints the view elsewhere up to lift 4, dungeons not dark.
        SkipAboveLift = RemoteView ? RemoteViewSkipLift : Map.RoofHeight(Avatar.Tx, Avatar.Ty, Avatar.Tz);
        InDungeonLift = RemoteView ? 0 : Map.DungeonHeight(Avatar.Tx, Avatar.Ty);
        _frameNo++;
        _paintCounter = 0;

        var cam = GetViewport().GetCamera2D();
        var view = GetViewport().GetVisibleRect().Size;
        var zoom = cam is not null ? cam.Zoom : Vector2.One;
        var center = cam is not null ? cam.GlobalPosition : Vector2.Zero;
        var half = view / zoom / 2f;
        _originX = Mathf.FloorToInt(center.X - half.X);
        _originY = Mathf.FloorToInt(center.Y - half.Y);
        _buffer.Resize(Mathf.CeilToInt(view.X / zoom.X) + 2, Mathf.CeilToInt(view.Y / zoom.Y) + 2);
        _buffer.Fill8(0);
        _buffer.ClearGlow();

        // Objects from chunks around the view can reach into it.
        var margin = 160f;
        var left = center.X - half.X - margin;
        var top = center.Y - half.Y - margin;
        var right = center.X + half.X + margin;
        var bottom = center.Y + half.Y + margin;

        var c0x = Mathf.FloorToInt(left / U7Constants.ChunkSizePixels) - 1;
        var c0y = Mathf.FloorToInt(top / U7Constants.ChunkSizePixels) - 1;
        var c1x = Mathf.CeilToInt(right / U7Constants.ChunkSizePixels) + 1;
        var c1y = Mathf.CeilToInt(bottom / U7Constants.ChunkSizePixels) + 1;

        const int chunkPx = U7Constants.ChunkSizePixels;
        for (var cy = c0y; cy <= c1y; cy++)
        {
            for (var cx = c0x; cx <= c1x; cx++)
            {
                var flats = GetFlats(Map.TerrainMap[U7Constants.WrapChunk(cx), U7Constants.WrapChunk(cy)]);
                _buffer.Copy8(flats, chunkPx, chunkPx, cx * chunkPx - _originX, cy * chunkPx - _originY);
            }
        }

        var ticks = Time.GetTicksMsec();

        // Exult Game_render::paint_map: flat RLE objects for every chunk first ...
        for (var cy = c0y; cy <= c1y; cy++)
        {
            for (var cx = c0x; cx <= c1x; cx++)
            {
                Map.EnsureChunkOrdered(cx, cy);
                foreach (var obj in Map.ObjectsInChunk(cx, cy))
                {
                    if (obj.IsFlat && !obj.Removed && !obj.InvisibleEgg && obj.Container is null)
                    {
                        DrawObject(obj, ticks);
                    }
                }
            }
        }

        // ... then non-flat objects chunk by chunk, each after its dependencies.
        _renderSeq++;
        for (var cy = c0y; cy <= c1y; cy++)
        {
            for (var cx = c0x; cx <= c1x; cx++)
            {
                var list = Map.ObjectsInChunk(cx, cy);
                for (var i = 0; i < list.Count; i++)
                {
                    var obj = list[i];
                    if (!obj.IsFlat && obj.RenderSeq != _renderSeq)
                    {
                        PaintObject(obj, ticks);
                    }
                }
            }
        }

        if (InDungeonLift != 0 && InDungeonLift >= SkipAboveLift)
        {
            PaintDungeonBlackness(c0x, c0y, c1x, c1y);
        }

        PaintMissiles();
        PaintHoming();
        PaintSprites();

        // Exult paints text effects after the map; the bark overlay draws these on screen.
        _barkBack.Clear();
        foreach (var obj in _barks)
        {
            _barkBack.Add(MakeBark(obj));
        }

        (_barkInfo, _barkBack) = (_barkBack, _barkInfo);
        _barks.Clear();

        if (Lighting is not null)
        {
            if (Lighting.Version != _paletteVersion)
            {
                _paletteVersion = Lighting.Version;
                _palette.SetRows(Lighting.Ambient, Lighting.Lit);
            }

            _material.SetShaderParameter("light_a", Lighting.LightA);
            _material.SetShaderParameter("light_b", Lighting.LightB);
            _material.SetShaderParameter("light_count", Lighting.LightCount);
            _material.SetShaderParameter("glow_boost", GlowTable.Magic.Day);
            _material.SetShaderParameter("window_spread", WindowLights.Look.Spread);
            var clouds = WeatherLook.Clouds;
            _material.SetShaderParameter("cloud_offset", Lighting.CloudOffset);
            _material.SetShaderParameter("cloud_cover", Lighting.CloudCover);
            _material.SetShaderParameter("cloud_shadow", Lighting.CloudShadow);
            _material.SetShaderParameter("cloud_tint", WeatherLook.CloudTint);
            _material.SetShaderParameter("cloud_scale", clouds.Scale);
            _material.SetShaderParameter("cloud_soft", clouds.Softness);
            var fog = WeatherLook.Fog;
            _material.SetShaderParameter("mist", Lighting.Mist);
            _material.SetShaderParameter("mist_offset", Lighting.MistOffset);
            // A little lighter than what the fog has made of the day.
            _material.SetShaderParameter("mist_colour", (Lighting.AmbientMean * 1.2f).Clamp(Vector3.Zero, Vector3.One));
            _material.SetShaderParameter("mist_scale", fog.Scale);
        }

        if (RotateColors)
        {
            _palette.Advance(Time.GetTicksMsec());
        }

        Upload();
        DrawTexture(_texture, new Vector2(_originX, _originY));
    }

    /// <summary>The index and glow planes as one RG8 texture: red the index, green the glow byte with its marks.</summary>
    void Upload()
    {
        var w = _buffer.Width;
        var h = _buffer.Height;
        var n = w * h;
        if (_upload.Length != n * 2)
        {
            _upload = new byte[n * 2];
        }

        var pixels = _buffer.Pixels;
        var glow = _buffer.Glow;
        for (var i = 0; i < n; i++)
        {
            _upload[2 * i] = pixels[i];
            _upload[2 * i + 1] = glow[i];
        }

        if (_image is null || _texture is null || _image.GetWidth() != w || _image.GetHeight() != h)
        {
            _image = Image.CreateFromData(w, h, false, Image.Format.Rg8, _upload);
            _texture = ImageTexture.CreateFromImage(_image);
            return;
        }

        _image.SetData(w, h, false, Image.Format.Rg8, _upload);
        _texture.Update(_image);
    }

    /// <summary>
    /// Top-most object whose frame covers <paramref name="world"/> (Exult <c>has_point</c>).
    /// </summary>
    public U7Object? PickObject(Vector2 world)
    {
        if (Map is null)
        {
            return null;
        }

        // Pick what is visibly on top: the object painted last in the most recent frame.
        var tile = WorldToTile(world);
        var cx = tile.Tx / U7Constants.TilesPerChunk;
        var cy = tile.Ty / U7Constants.TilesPerChunk;
        U7Object? best = null;
        var bestStamp = long.MinValue;
        for (var dy = -2; dy <= 2; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                foreach (var obj in Map.ObjectsInChunk(cx + dx, cy + dy))
                {
                    if (!PaintedRecently(obj) || !SpriteContains(obj, world))
                    {
                        continue;
                    }

                    if (obj.PaintStamp >= bestStamp)
                    {
                        bestStamp = obj.PaintStamp;
                        best = obj;
                    }
                }
            }
        }

        return best ?? PickByTile(tile);
    }

    /// <summary>Drawn in this or the previous frame (PickObject may run before this frame's _Draw).</summary>
    bool PaintedRecently(U7Object obj) =>
        !obj.Removed && obj.Container is null && (obj.PaintStamp >> 32) + 1 >= _frameNo;

    U7Object? PickByTile(TileCoord tile)
    {
        U7Object? best = null;
        var bestStamp = long.MinValue;
        foreach (var obj in Map.ObjectsInChunk(tile.ChunkX, tile.ChunkY))
        {
            if (!PaintedRecently(obj) || !obj.Occupies(tile.Tx, tile.Ty))
            {
                continue;
            }

            if (obj.PaintStamp >= bestStamp)
            {
                bestStamp = obj.PaintStamp;
                best = obj;
            }
        }

        return best;
    }

    bool SpriteContains(U7Object obj, Vector2 world)
    {
        if (Shapes.GetFrame8(obj.Shape, obj.Frame) is not { } frame)
        {
            var t = WorldToTile(world, obj.Tz);
            return obj.Occupies(t.Tx, t.Ty);
        }

        ShapeLocation(obj.Tx, obj.Ty, obj.Tz, out var hx, out var hy);
        return frame.Covers(Mathf.FloorToInt(world.X) - hx, Mathf.FloorToInt(world.Y) - hy);
    }

    void DrawBark(U7Object obj, ulong ticks)
    {
        if (obj.BarkUntilMsec == 0 || ticks > obj.BarkUntilMsec)
        {
            if (obj.BarkUntilMsec != 0)
            {
                obj.BarkText = "";
                obj.BarkUntilMsec = 0;
            }

            return;
        }

        if (string.IsNullOrEmpty(obj.BarkText))
        {
            return;
        }

        _barks.Add(obj);
    }

    readonly List<U7Object> _barks = new();
    List<BarkInfo> _barkInfo = new();
    List<BarkInfo> _barkBack = new();

    /// <summary>A bark to draw: its text and the top centre of the speaker's sprite in world pixels.</summary>
    public readonly record struct BarkInfo(string Text, Vector2 WorldTop);

    /// <summary>Barks painted in the last frame (drawn on screen by the bark overlay).</summary>
    public IReadOnlyList<BarkInfo> Barks => _barkInfo;

    /// <summary>
    /// Exult <c>Text_effect</c>: one line per speaker, the '@' quote marks shown
    /// as '"'. Anchored at the top centre of the shape rectangle.
    /// </summary>
    BarkInfo MakeBark(U7Object obj)
    {
        var fi = Catalog[obj.Shape].GetFrame(obj.Frame);
        ShapeLocation(obj.Tx, obj.Ty, obj.Tz, out var hx, out var hy);
        return new BarkInfo(ShownText(obj.BarkText), SpriteTop(fi, hx, hy));
    }

    /// <summary>Exult <c>Text_effect::init</c>: '@' quote marks at either end are shown as '"'.</summary>
    public static string ShownText(string text)
    {
        if (text.StartsWith('@'))
        {
            text = '"' + text[1..];
        }

        if (text.EndsWith('@'))
        {
            text = text[..^1] + '"';
        }

        return text;
    }

    /// <summary>The top centre of a frame painted with its hot spot at (<paramref name="hx"/>, <paramref name="hy"/>).</summary>
    public static Vector2 SpriteTop(FrameInfo fi, int hx, int hy) =>
        new(hx - fi.XLeft + (fi.XLeft + fi.XRight + 1) / 2f, hy - fi.YAbove);

    /// <summary>Exult <c>Game_render::paint_object</c>: dependencies first, then the object.</summary>
    void PaintObject(U7Object obj, ulong ticks)
    {
        if (obj.Tz >= SkipAboveLift)
        {
            return;
        }

        obj.RenderSeq = _renderSeq;
        if (obj.Dependencies is { Count: > 0 } deps)
        {
            foreach (var dep in deps)
            {
                if (dep.RenderSeq != _renderSeq && !dep.Removed)
                {
                    PaintObject(dep, ticks);
                }
            }
        }

        // (Exult paints no barge: its parts are objects of their own.)
        if (obj.Removed || obj.Container is not null || obj.InvisibleEgg || obj.IsBarge ||
            (obj.IsActor && obj.GetFlag(U7.Actors.ObjFlag.DontMove)))
        {
            return;
        }

        DrawObject(obj, ticks);
        DrawBark(obj, ticks);
    }

    /// <summary>The frame an object shows: animated shapes (not actors) cycle through their frames.</summary>
    public static int DisplayFrame(ShapeCatalog catalog, U7Object obj, ulong ticks)
    {
        var info = catalog[obj.Shape];
        return info.Animated && info.FrameCount > 1 && !obj.IsActor
            ? (int)((ticks / 180) % (ulong)info.FrameCount)
            : obj.Frame;
    }

    /// <summary>
    /// Whether the world shows the object, by the paint passes' rules: flats
    /// always, others below the roof (Exult <c>paint_object</c>'s skip lift),
    /// not barges, actors not while <c>dont_move</c>, and invisible ones only
    /// as the avatar or in the party (<c>Actor::paint</c>).
    /// </summary>
    public static bool IsPainted(U7Object obj, int skipAboveLift, U7Object avatar)
    {
        if (obj.Removed || obj.Container is not null || obj.InvisibleEgg)
        {
            return false;
        }

        if (obj.IsFlat)
        {
            return true;
        }

        if (obj.Tz >= skipAboveLift || obj.IsBarge)
        {
            return false;
        }

        if (!obj.IsActor)
        {
            return true;
        }

        return !obj.GetFlag(U7.Actors.ObjFlag.DontMove) &&
               (!obj.GetFlag(U7.Actors.ObjFlag.Invisible) || obj == avatar || obj.GetFlag(U7.Actors.ObjFlag.InParty));
    }

    void DrawObject(U7Object obj, ulong ticks)
    {
        var info = Catalog[obj.Shape];
        // The light pools' discs: their light is drawn by the world shader.
        if (GlowTable.IsPool(obj.Shape))
        {
            return;
        }

        var frame = DisplayFrame(Catalog, obj, ticks);
        if (Shapes.GetFrame8(obj.Shape, frame) is not { } shape)
        {
            return;
        }

        ShapeLocation(obj.Tx, obj.Ty, obj.Tz, out var hx, out var hy);
        hx -= _originX;
        hy -= _originY;
        if (obj.IsActor)
        {
            if (!PaintActor(obj, shape, hx, hy, ticks))
            {
                return;
            }
        }
        else
        {
            // Roofs, upper floors and what stands on them (Exult's building class from lift 3) take no window light.
            var marks = info.IsBuilding && obj.Tz >= 3 && !WindowLights.IsWindow(obj.Shape) ? IndexBuffer8.RoofMark : (byte)0;
            var glow = (byte)((GlowTable.IsEmitter(obj.Shape) ? GlowTable.GlowByte : 0) | marks);
            if (info.Translucent)
            {
                // Exult Shape_manager::paint_shape: TFA-translucent shapes through the tables.
                var glass = WindowLights.IsGlass(obj.Shape);
                var haze = (byte)(Haze(glow) | marks | (glass ? IndexBuffer8.PaneMark : 0));
                var window = glass ? Lighting?.Windows.SlotOf(obj) ?? 0 : (byte)0;
                _buffer.PaintRleTranslucent(shape, hx, hy, Shapes.Xforms, glow, haze, window);
            }
            else
            {
                _buffer.PaintRle(shape, hx, hy, glow);
            }
        }

        obj.PaintStamp = ((long)_frameNo << 32) | (uint)(++_paintCounter);
    }

    /// <summary>
    /// Exult <c>Actor::paint</c>: always translucent; an invisible actor
    /// only if it is the avatar or in the party, through the invisible table
    /// (<c>paint_invisible</c>); then one outline for its state. False when
    /// nothing was painted.
    /// </summary>
    bool PaintActor(U7Object actor, ShapeFrame shape, int x, int y, ulong ticks)
    {
        if (actor.GetFlag(U7.Actors.ObjFlag.Invisible))
        {
            if (actor != Avatar && !actor.GetFlag(U7.Actors.ObjFlag.InParty))
            {
                return false;
            }

            _buffer.PaintRleTransformed(shape, x, y, Shapes.Xforms.Invisible);
        }
        else
        {
            var glow = GlowTable.IsEmitter(actor.Shape) ? GlowTable.GlowByte : (byte)0;
            _buffer.PaintRleTranslucent(shape, x, y, Shapes.Xforms, glow, Haze(glow));
        }

        if (StatusOutline(actor, ticks) is { } color)
        {
            _buffer.PaintRleOutline(shape, x, y, color);
        }

        return true;
    }

    /// <summary>Exult <c>Actor::paint</c>'s outline: a momentary red one for a hit, else the first of charmed, paralysed, protected, cursed and poisoned.</summary>
    byte? StatusOutline(U7Object actor, ulong ticks)
    {
        if (ticks < actor.HitUntilMsec)
        {
            return _palette.Hit;
        }

        if (actor.GetFlag(U7.Actors.ObjFlag.Charmed))
        {
            return _palette.Charmed;
        }

        if (actor.GetFlag(U7.Actors.ObjFlag.Paralyzed))
        {
            return _palette.Paralyze;
        }

        if (actor.GetFlag(U7.Actors.ObjFlag.Protection))
        {
            return _palette.Protect;
        }

        if (actor.GetFlag(U7.Actors.ObjFlag.Cursed))
        {
            return _palette.Cursed;
        }

        return actor.GetFlag(U7.Actors.ObjFlag.Poisoned) ? _palette.Poison : null;
    }

    /// <summary>
    /// Exult <c>Projectile_effect::paint</c>, after the map, at
    /// <see cref="MissileHotspot"/>, translucent if its shape is; magic
    /// missiles glow.
    /// </summary>
    void PaintMissiles()
    {
        if (Missiles is null)
        {
            return;
        }

        foreach (var m in Missiles)
        {
            if (m.Frame < 0 || Shapes.GetFrame8(m.SpriteShape, m.Frame) is not { } frame)
            {
                continue;
            }

            var (x, y) = MissileHotspot(m);
            var glow = GlowTable.IsGlowingMissile(m.SpriteShape) ? GlowTable.GlowByte : (byte)0;
            if (Roofed(m.Pos))
            {
                PaintThroughWindows(frame, m.Pos, x, y, glow, Catalog[m.SpriteShape].Translucent);
            }
            else if (Catalog[m.SpriteShape].Translucent)
            {
                _buffer.PaintRleTranslucent(frame, x - _originX, y - _originY, Shapes.Xforms, glow, Haze(glow));
            }
            else
            {
                _buffer.PaintRle(frame, x - _originX, y - _originY, glow);
            }
        }
    }

    /// <summary>Exult <c>Homing_projectile::paint</c> at <see cref="HomingHotspot"/>, translucent like every sprite; it glows.</summary>
    void PaintHoming()
    {
        if (HomingMissiles is null)
        {
            return;
        }

        foreach (var h in HomingMissiles)
        {
            if (Shapes.GetSprite8(h.Sprite, h.Frame) is not { } frame)
            {
                continue;
            }

            var (x, y) = HomingHotspot(h);
            var glow = GlowTable.IsGlowingSprite(h.Sprite) ? GlowTable.GlowByte : (byte)0;
            if (Roofed(h.Pos))
            {
                PaintThroughWindows(frame, h.Pos, x, y, glow, true);
                continue;
            }

            _buffer.PaintRleTranslucent(frame, x - _originX, y - _originY, Shapes.Xforms, glow, Haze(glow));
        }
    }

    /// <summary>
    /// Exult paints its effects after the map, over everything; here a roof
    /// (or upper floor) painted over an effect's tile hides it, as it hides
    /// the effect's light (the user's rule, <see cref="GameMap.CoverAbove"/>).
    /// </summary>
    bool Roofed(TileCoord at) => Map.CoverAbove(at.Tx, at.Ty, at.Tz) < SkipAboveLift;

    /// <summary>A hidden effect at its hotspot (world pixels), seen only through the panes of its room's windows.</summary>
    void PaintThroughWindows(ShapeFrame frame, TileCoord at, int x, int y, byte glow, bool translucent)
    {
        if (Lighting?.Windows.PanesFor(at, _paneWindows) == true)
        {
            _buffer.PaintRleThroughPanes(frame, x - _originX, y - _originY, Shapes.Xforms, _paneWindows, glow, translucent);
        }
    }

    /// <summary>Exult <c>Sprites_effect::paint</c>, after the map, at <see cref="SpriteHotspot"/>; magic and blasts glow.</summary>
    void PaintSprites()
    {
        if (Effects is null)
        {
            return;
        }

        foreach (var e in Effects.Sprites)
        {
            if (!e.Visible || Shapes.GetSprite8(e.Sprite, e.Frame) is not { } frame)
            {
                continue;
            }

            var (x, y) = SpriteHotspot(e);
            var glow = GlowTable.IsGlowingSprite(e.Sprite) ? GlowTable.GlowByte : (byte)0;
            if (Roofed(e.Pos))
            {
                PaintThroughWindows(frame, e.Pos, x, y, glow, true);
                continue;
            }

            // Exult paints every SPRITES.VGA shape translucent (Shape_manager::paint_shape).
            _buffer.PaintRleTranslucent(frame, x - _originX, y - _originY, Shapes.Xforms, glow, Haze(glow));
        }
    }

    /// <summary>
    /// Exult <c>Projectile_effect::paint</c>'s place for the frame's hotspot,
    /// in world pixels: the tile's corner, raised by half the lift in pixels
    /// (<c>tx*8 - 4tz</c>). Where Exult's missile jumps to its next tile every
    /// 100 ms, this one glides there in between (only the picture: the
    /// flight's rules see whole tiles).
    /// </summary>
    public static (int X, int Y) MissileHotspot(U7.Actors.Missile m) => Glide(m.Pos, m.Next, m.Fraction);

    /// <summary>
    /// Exult <c>Homing_projectile::paint</c>'s place, as a missile's; where
    /// Exult's moves a tile every 100 ms, this one glides from its last tile
    /// to the new one in between.
    /// </summary>
    public static (int X, int Y) HomingHotspot(U7.Actors.HomingMissile h) => Glide(h.PrevPos, h.Pos, h.Fraction);

    /// <summary>Exult <c>Sprites_effect::paint</c>'s place: the tile's corner, raised by half the lift in whole tiles, plus the effect's offset.</summary>
    public static (int X, int Y) SpriteHotspot(U7.World.SpriteEffect e)
    {
        var lp = e.Pos.Tz / 2;
        return (e.XOff + (e.Pos.Tx - lp) * U7Constants.TileSize, e.YOff + (e.Pos.Ty - lp) * U7Constants.TileSize);
    }

    /// <summary>A glowing shape's see-through pixels glow less (<see cref="GlowTable.MagicLook.Haze"/>).</summary>
    static byte Haze(byte glow) => (glow & IndexBuffer8.GlowBits) == 0 ? (byte)0 : GlowTable.HazeByte;

    static (int X, int Y) Glide(TileCoord from, TileCoord to, double f)
    {
        const int tile = U7Constants.TileSize;
        var lift = 4 * (to.Tz - from.Tz);
        return (from.Tx * tile - 4 * from.Tz + (int)Math.Round((U7Constants.TileDelta(from.Tx, to.Tx) * tile - lift) * f),
            from.Ty * tile - 4 * from.Tz + (int)Math.Round((U7Constants.TileDelta(from.Ty, to.Ty) * tile - lift) * f));
    }

    /// <summary>
    /// Exult <c>Game_render::paint_blackness</c>: in a dungeon, index 0 over
    /// the chunks without dungeon and the tiles under no dungeon roof,
    /// shifted up by the dungeon's lift.
    /// </summary>
    void PaintDungeonBlackness(int c0x, int c0y, int c1x, int c1y)
    {
        var off = 4 * InDungeonLift;
        const int chunkPx = U7Constants.ChunkSizePixels;
        const int tilePx = U7Constants.TileSize;
        for (var cy = c0y; cy <= c1y; cy++)
        {
            for (var cx = c0x; cx <= c1x; cx++)
            {
                var x0 = cx * chunkPx - off - _originX;
                var y0 = cy * chunkPx - off - _originY;
                if (!Map.ChunkHasDungeon(cx, cy))
                {
                    _buffer.Fill8(0, chunkPx, chunkPx, x0, y0);
                    continue;
                }

                for (var ly = 0; ly < U7Constants.TilesPerChunk; ly++)
                {
                    var run = 0;
                    var runX = 0;
                    for (var lx = 0; lx < U7Constants.TilesPerChunk; lx++)
                    {
                        if (Map.DungeonHeightLocal(cx, cy, lx, ly) == 0)
                        {
                            if (run == 0)
                            {
                                runX = lx;
                            }

                            run++;
                        }
                        else if (run != 0)
                        {
                            _buffer.Fill8(0, run * tilePx, tilePx, x0 + runX * tilePx, y0 + ly * tilePx);
                            run = 0;
                        }
                    }

                    if (run != 0)
                    {
                        _buffer.Fill8(0, run * tilePx, tilePx, x0 + runX * tilePx, y0 + ly * tilePx);
                    }
                }
            }
        }
    }

    public static void ShapeLocation(int tx, int ty, int tz, out int x, out int y)
    {
        // Exult Game_window::Get_shape_location with scroll = 0.
        var lift = 4 * tz;
        x = (tx + 1) * U7Constants.TileSize - 1 - lift;
        y = (ty + 1) * U7Constants.TileSize - 1 - lift;
    }

    /// <summary>
    /// The tile under a world pixel at a lift, the inverse of
    /// <see cref="ShapeLocation"/> (whose point is the tile's last pixel):
    /// Exult <c>start_actor_along_path</c>'s <c>(winx + liftpixels) / c_tilesize</c>.
    /// </summary>
    public static TileCoord WorldToTile(Vector2 world, int lift = 0)
    {
        var liftPx = 4 * lift;
        var tx = Mathf.FloorToInt((world.X + liftPx) / U7Constants.TileSize);
        var ty = Mathf.FloorToInt((world.Y + liftPx) / U7Constants.TileSize);
        return new TileCoord(U7Constants.WrapTile(tx), U7Constants.WrapTile(ty), lift);
    }

    /// <summary>
    /// Exult <c>Chunk_terrain::render_flats</c>: a terrain's tiles whose frame
    /// is a raw 8×8 terrain frame, copied into a 128×128 picture of indices
    /// (<c>paint_tile</c>; RLE flats are objects of their own). Kept for the
    /// most recently used terrains, as Exult's render queue does.
    /// </summary>
    byte[] GetFlats(int terrain)
    {
        if (_flats.TryGetValue(terrain, out var node))
        {
            _flatOrder.Remove(node);
            _flatOrder.AddFirst(node);
            return node.Value.Pixels;
        }

        const int size = U7Constants.ChunkSizePixels;
        const int tile = ShapeFrame.TileSize;
        byte[] pixels;
        if (_flats.Count >= MaxCachedTerrains && _flatOrder.Last is { } last)
        {
            _flatOrder.RemoveLast();
            _flats.Remove(last.Value.Terrain);
            pixels = last.Value.Pixels;
            Array.Clear(pixels);
        }
        else
        {
            pixels = new byte[size * size];
        }

        var cells = (uint)terrain < (uint)Map.Terrains.Length ? Map.Terrains[terrain] : null;
        for (var i = 0; cells is not null && i < cells.Length; i++)
        {
            var cell = cells[i];
            if (cell.IsRle || Shapes.GetFrame8(cell.Shape, cell.Frame) is not { IsRle: false } flat)
            {
                continue;
            }

            var at = i / U7Constants.TilesPerChunk * tile * size + i % U7Constants.TilesPerChunk * tile;
            for (var row = 0; row < tile; row++)
            {
                flat.Pixels.AsSpan(row * tile, tile).CopyTo(pixels.AsSpan(at + row * size, tile));
            }
        }

        _flats[terrain] = _flatOrder.AddFirst((terrain, pixels));
        return pixels;
    }
}
