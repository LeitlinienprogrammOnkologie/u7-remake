using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Exult <c>Patrol_schedule</c>: walking from path marker to path marker
/// (shape 607, frame = the marker's number, out to the shape's last frame
/// and back again) within 25 tiles. A marker's quality says what to do there
/// (low 5 bits) and whether to fight any foe in sight (32) or keep using it
/// (64): wrap to the first, pause, sit or read, kneel, loiter, turn about,
/// pace, reverse or skip at random, hammer, tend lamps and shutters, run the
/// NPC's usecode, bow, ready or put away the weapon, swing it. With no
/// marker nearby the NPC loiters and fights any foe it sees.
/// </summary>
public sealed class PatrolSchedule(NpcBrain brain) : Schedule(brain)
{
    const int PathShape = 607;
    const int BookShape = 642;
    const int HammerShape = 623;

    /// <summary>Markers found so far, by number.</summary>
    readonly List<U7Object?> _paths = new();
    int _pathnum = -1;
    int _dir = 1;
    int _state = -1;
    TileCoord _center;
    bool _paceHoriz;
    int _phase = 1;
    int _paceCount;
    bool _seekCombat;
    bool _forever;
    U7Object? _book;
    U7Object? _hammer;

    /// <summary>Exult <c>num_path_eggs</c>: the highest marker number.</summary>
    int NumPathEggs => Map.Catalog[PathShape].FrameCount - 1;

    List<U7Object> Markers(int frame = -1) =>
        Map.FindNearby(Here(Npc), PathShape, 25, 16).Where(m => frame < 0 || m.Frame == frame).ToList();

    public override void NowWhat()
    {
        if (_seekCombat && SeekFoes())
        {
            return; // Fighting now.
        }

        var gotpath = true;
        switch (_state)
        {
            case -1: // Initialization.
                gotpath = Markers().Count > 0;
                goto case 0;
            case 0:
                FindNextPath(gotpath);
                break;
            case 1: // Walking to the marker.
                if (_pathnum >= 0 && _pathnum < _paths.Count && _paths[_pathnum] is { Removed: false } path &&
                    (_forever || ObjectGeometry.Distance(Npc, path) < 2))
                {
                    AtMarker(path);
                }
                else
                {
                    _state = 0;
                    Start(Std, Std);
                }

                break;
            case 2: // Sitting or reading: stay 5-15 s.
                if ((Npc.Frame & 0xf) == ActorWalker.SitFrame)
                {
                    if (_book is { Removed: false } book && ObjectGeometry.Distance(Npc, book) < 4 && book.Frame % 3 != 0)
                    {
                        book.Frame -= book.Frame % 3; // Open the book.
                    }

                    Start(250, 5000 + Rng.Next(10000));
                }
                else
                {
                    Start(250, Rng.Next(1000));
                }

                _state = 3;
                break;
            case 3: // Stand up.
                if ((Npc.Frame & 0xf) == ActorWalker.SitFrame)
                {
                    if (_book is { Removed: false } book && ObjectGeometry.Distance(Npc, book) < 4)
                    {
                        book.Frame = book.Frame - book.Frame % 3 + 1; // Close it.
                        _book = null;
                    }

                    RunScript(ScriptDelayTicks, 2, ScriptBowFrame, ScriptDelayTicks, 2, ScriptStandFrame);
                    Start(Std, Std * 7);
                }

                _state = 0;
                break;
            case 4: // Loiter.
                if (Rng.Next(5) == 0)
                {
                    _state = 0;
                    Start(Std, Std);
                }
                else
                {
                    const int dist = 12;
                    var dest = new TileCoord(_center.Tx - dist + Rng.Next(2 * dist),
                        _center.Ty - dist + Rng.Next(2 * dist), _center.Tz).Wrapped();
                    StartAction(PathWalk.Line(Map, Npc, dest), Std, Rng.Next(2000));
                }

                break;
            case 5: // Pacing.
                if (Here(Npc).Distance(_center) < 1)
                {
                    _paceCount++;
                    if (_paceCount == 6)
                    {
                        Npc.Frame = ActorWalker.DirFrame(ActorWalker.FacingOfFrame(Npc.Frame), 0);
                        Start(3 * Std, 3 * Std);
                        _state = 0;
                        break;
                    }

                    if (_phase is >= 2 and <= 4)
                    {
                        _paceCount--;
                    }
                }

                Pace(_paceHoriz, ref _phase, Std);
                break;
            case 6: // Ready the hammer and swing it.
                SwingHammer();
                break;
        }
    }

    /// <summary>Exult's state 0: on to the next marker, or turn back at the end.</summary>
    void FindNextPath(bool gotpath)
    {
        if (!gotpath)
        {
            // (Serpent Isle switches schedules first.)
            _seekCombat = true;
            _state = 4;
            Start(2 * Std + Rng.Next(500), 0);
            return;
        }

        if (_forever)
        {
            // Quality flag 64, "repeat forever": the last marker again and again.
            _state = 1;
            Start(Std, Std);
            return;
        }

        _pathnum += _dir;
        if (_pathnum == 0 && _dir == -1)
        {
            _dir = 1; // Start over from zero.
        }
        else if (_pathnum == NumPathEggs && _dir == 1)
        {
            _dir = -1; // Start backwards from the last.
        }

        while (_paths.Count <= _pathnum)
        {
            _paths.Add(null);
        }

        var path = _pathnum >= 0 && _paths[_pathnum] is { Removed: false } known ? known : null;
        if (path is null)
        {
            path = FindNearest(Markers(_pathnum));
            if (path is null)
            {
                // Turn back if at the end.
                _dir = -_dir;
                _pathnum += _dir;
                Start(Std, Std);
                return;
            }

            _paths[_pathnum] = path;
        }

        var d = Here(path);
        if (!WalkPathTo(d, Std, 2 * Std + Rng.Next(500)) &&
            (Map.FindSpot(d, 1, Npc.Shape, Npc.Frame, 1) is not { } spot || !WalkPathTo(spot, Std, Rng.Next(1000))))
        {
            Start(Std, 2000); // Failed. Later.
            return;
        }

        _state = 1;
    }

    /// <summary>Exult's state 1 on arrival: what the marker's quality says.</summary>
    void AtMarker(U7Object path)
    {
        var delay = 2;
        // At worst, the standing frame once the marker is reached.
        var script = new List<object> { ScriptStandFrame };
        var qual = path.Quality;
        _seekCombat = (qual & 32) != 0;
        _forever = (qual & 64) != 0;
        // Exult's fix for the pirate fort at the Isle of the Avatar, which
        // works "incorrectly" in the original.
        if (_forever && (qual & 31) == 0)
        {
            _forever = false;
        }

        switch (qual & 31)
        {
            case 0: // None.
                break;
            case 25: // 50% wrap to 0.
                if (Rng.Next(2) != 0)
                {
                    break;
                }

                goto case 1;
            case 1: // Wrap to 0.
                _pathnum = -1;
                _dir = 1;
                break;
            case 2: // Pause; Exult guesses 3 ticks.
                script.AddRange([ScriptDelayTicks, 3]);
                delay = 5;
                break;
            case 24: // Read: the book within 4 tiles.
                _book = FindClosest([BookShape], 4).FirstOrDefault();
                goto case 3;
            case 3: // Sit.
                if (SitDown(null, 0) is not null)
                {
                    RunScript([.. script]);
                    _state = 2;
                    return;
                }

                break;
            case 4: // Kneel at a tombstone (Exult: no flowers).
            case 5: // Kneel.
                script.AddRange([
                    ScriptDelayTicks, 2, ScriptBowFrame, ScriptDelayTicks, 4, ScriptKneelFrame, ScriptDelayTicks, 20,
                    ScriptBowFrame, ScriptDelayTicks, 4, ScriptStandFrame
                ]);
                delay = 36;
                break;
            case 6: // Loiter.
                RunScript([.. script]);
                _center = Here(path);
                _state = 4;
                Start(Std, Std * delay);
                return;
            case 7: // Left about-face (Exult's label; it turns by way of the right).
            case 8: // Right about-face.
            {
                int[] faceDirs = (qual & 31) == 7 ? [2, 4] : [6, 4];
                var facing = ActorWalker.FacingOfFrame(Npc.Frame);
                foreach (var turn in faceDirs)
                {
                    script.AddRange([ScriptDelayTicks, 2, ScriptFaceDir, (facing + turn) % 8]);
                }

                delay = 8;
                break;
            }
            case 9: // Horizontal pace.
            case 10: // Vertical pace (both pace vertically in the originals; Exult goes by the names).
                _paceHoriz = (qual & 1) != 0;
                _paceCount = -1;
                _phase = 1;
                RunScript([.. script]);
                _center = Here(path);
                _state = 5;
                Start(Std, Std * delay);
                return;
            case 11: // 50% reverse.
                if (Rng.Next(2) != 0)
                {
                    _dir = -_dir;
                }

                break;
            case 12: // 50% skip the next.
                if (Rng.Next(2) != 0)
                {
                    _pathnum += _dir;
                    if (_pathnum == 0 && _dir == -1)
                    {
                        _dir = 1;
                    }
                    else if (_pathnum == NumPathEggs && _dir == 1)
                    {
                        _dir = -1;
                    }
                }

                break;
            case 13: // Hammer: use the one it has, else fetch or make one.
                _hammer = SetProcureItemAction(_hammer, 15, HammerShape, 0);
                RunScript([.. script]);
                _state = 6;
                Start(Std, Std * delay);
                return;
            case 14: // Check the area for lamps and shutters, now.
                ForceStreetMaintenance();
                if (TryStreetMaintenance())
                {
                    return;
                }

                delay += 2;
                script.AddRange([ScriptDelayTicks, 2]);
                break;
            case 15: // Usecode (Exult queues it so that it halts no other script).
                Runner.ProximityUsecode?.Invoke(Npc);
                delay = 3;
                break;
            case 16: // Bow to the ground.
                script.AddRange([ScriptDelayTicks, 2, ScriptBowFrame, ScriptDelayTicks, 2]);
                delay = 8;
                break;
            case 17: // Bow from the ground.
                script.AddRange([ScriptBowFrame, ScriptDelayTicks, 2, ScriptStandFrame]);
                delay = 8;
                break;
            case 20: // Ready the weapon.
                Runner.ReadyBestWeapon?.Invoke(Npc);
                break;
            case 21: // Put it away.
                Equipment.EmptyHands(Npc, Map.Catalog, Map);
                break;
            case 22: // One-handed swing.
            case 23: // Two-handed swing.
            {
                var facing = ActorWalker.FacingOfFrame(Npc.Frame);
                var weapon = Equipment.GetReadied(Npc, ReadySpot.Rhand)?.Shape ?? 0;
                int[] frames = [
                    .. AttackFrames(weapon, facing), ActorWalker.DirFrame(facing, 3), ActorWalker.DirFrame(facing, 0)
                ];
                SetAction(new FramesAction(frames, Std));
                delay = frames.Length;
                break;
            }
            // 18 and 19 (wait for / release a semaphore?) seem unused; Exult ignores them.
        }

        RunScript([.. script]);
        _state = 0; // Then on to the next marker.
        Start(Std, Std * delay);
    }

    /// <summary>Exult's state 6: the hammer in hand, swing it 1-3 times with a clank.</summary>
    void SwingHammer()
    {
        if (_hammer is not { Removed: false } hammer)
        {
            _state = 1; // Should be impossible; look again.
            Start(Std, Std);
            return;
        }

        Map.TakeFromWorld(hammer);
        Equipment.EmptyHands(Npc, Map.Catalog, Map); // For safety.
        Equipment.AddReadied(Npc, hammer, ReadySpot.Lhand, Map.Catalog, Map, forcePos: true);
        var repcnt = 1 + Rng.Next(3);
        RunScript(ScriptDelayTicks, 2, ScriptReadyFrame, ScriptDelayTicks, 2, ScriptRaise1Frame, ScriptDelayTicks, 2,
            ScriptSfx, 45, ScriptOutFrame, ScriptDelayTicks, 2, ScriptRepeat, -13, repcnt, ScriptDelayTicks, 2,
            ScriptReadyFrame, ScriptDelayTicks, 2, ScriptStandFrame);
        _state = 0;
        Start(Std, Std * (11 * (repcnt + 1) + 6));
    }

    /// <summary>Exult: the hammer it fetched goes when the patrol does.</summary>
    public override void Ending(int newType)
    {
        if (_hammer is { Removed: false } hammer)
        {
            Map.RemoveObject(hammer);
        }
    }
}
