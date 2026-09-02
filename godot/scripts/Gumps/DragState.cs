using U7.Data;

namespace U7.Gumps;

/// <summary>
/// One in-flight drag: an item, a gump window, or a pushed button. Exult <c>Dragging_info</c>.
/// </summary>
public sealed class DragState
{
    public U7Object? Object;
    public Gump? SourceGump;
    public GumpButton? Button;
    public int MouseX;
    public int MouseY;
    public int PaintX;
    public int PaintY;
    public int OldTx;
    public int OldTy;
    public int OldTz;
    public U7Object? OldContainer;
    public int OldReadySlot = -1;
    public bool Moved;
    public bool FromWorld;

    public bool IsItem => Object is not null;
    public bool IsWindow => Object is null && Button is null && SourceGump is not null;
}
