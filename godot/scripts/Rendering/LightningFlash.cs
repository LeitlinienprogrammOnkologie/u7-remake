using Godot;

namespace U7.Rendering;

/// <summary>
/// Exult <c>Lightning_effect</c>'s PALETTE_LIGHTNING: while a flash shows the
/// world is drawn at full light and this adds a blue-white wash over it
/// (PALETTES.FLX entry 10 is about the day palette plus this much).
/// </summary>
public partial class LightningFlash : Node2D
{
    static readonly Color Wash = new(0.14f, 0.09f, 0.31f);

    public override void _Ready()
    {
        Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        ZIndex = 2;
        Visible = false;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var cam = GetViewport().GetCamera2D();
        var view = GetViewport().GetVisibleRect().Size;
        var zoom = cam?.Zoom ?? Vector2.One;
        var half = view / zoom / 2f + new Vector2(16, 16);
        var mid = cam?.GlobalPosition ?? Vector2.Zero;
        DrawRect(new Rect2(mid - half, half * 2), Wash);
    }
}
