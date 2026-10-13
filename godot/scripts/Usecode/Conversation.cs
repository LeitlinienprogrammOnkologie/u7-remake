namespace U7.Usecode;

/// <summary>
/// Conversation answers and face slots. HUD renders these; the VM only stores state.
/// Matches the data side of Exult <c>Conversation</c>.
/// </summary>
public sealed class Conversation
{
    public List<string> Answers { get; } = new();
    readonly Stack<List<string>> _answerStack = new();
    public readonly (int Shape, int Frame)?[] Faces = new (int, int)?[2];
    /// <summary>Speaker name per face slot, empty when the player has not met them yet.</summary>
    public readonly string[] FaceNames = ["", ""];
    public string NpcText = "";
    public int FaceCount { get; private set; }
    /// <summary>Exult <c>last_face_shown</c>: slot of the face shown most recently, or -1.</summary>
    public int LastFace { get; private set; } = -1;
    /// <summary>Slot of the face that said <see cref="NpcText"/>, or -1 (signs, no face).</summary>
    public int TextFace = -1;

    public void ClearAnswers() => Answers.Clear();

    public void AddAnswer(UsecodeValue val)
    {
        if (val.IsArray)
        {
            for (var i = 0; i < val.ArraySize; i++)
            {
                AddAnswer(val.GetElem(i));
            }

            return;
        }

        var str = val.StrValue;
        if (str is not null)
        {
            AddAnswer(str);
        }
    }

    public void AddAnswer(string str)
    {
        RemoveAnswer(str);
        Answers.Add(str);
    }

    public void RemoveAnswer(UsecodeValue val)
    {
        if (val.IsArray)
        {
            for (var i = 0; i < val.ArraySize; i++)
            {
                RemoveAnswer(val.GetElem(i));
            }

            return;
        }

        var str = val.StrValue;
        if (str is not null)
        {
            RemoveAnswer(str);
        }
    }

    public void RemoveAnswer(string str) => Answers.Remove(str);

    public void PushAnswers()
    {
        _answerStack.Push(new List<string>(Answers));
        Answers.Clear();
    }

    public void PopAnswers()
    {
        if (_answerStack.Count == 0)
        {
            return;
        }

        Answers.Clear();
        Answers.AddRange(_answerStack.Pop());
    }

    /// <summary>Exult <c>Conversation::locate_answer</c>: the exact answer's index, or -1.</summary>
    public int LocateAnswer(string str) => Answers.IndexOf(str);

    public void ShowFace(int shape, int frame, string name = "", int slot = -1)
    {
        if (slot < 0)
        {
            for (var i = 0; i < Faces.Length; i++)
            {
                if (Faces[i] is { } f && f.Shape == shape)
                {
                    Faces[i] = (shape, frame);
                    LastFace = i;
                    if (name.Length > 0)
                    {
                        FaceNames[i] = name;
                    }

                    return;
                }
            }

            slot = Faces[0] is null ? 0 : 1;
        }

        slot = Math.Clamp(slot, 0, Faces.Length - 1);
        if (Faces[slot] is null)
        {
            FaceCount++;
        }

        Faces[slot] = (shape, frame);
        FaceNames[slot] = name;
        LastFace = slot;
    }

    /// <summary>Exult <c>Conversation::change_face_frame(frame, slot)</c>: the slot's face in another frame, and it speaks next.</summary>
    public void ChangeFaceFrame(int frame, int slot)
    {
        if ((uint)slot < (uint)Faces.Length && Faces[slot] is { } f)
        {
            LastFace = slot;
            Faces[slot] = (f.Shape, frame);
        }
    }

    /// <summary>Exult <c>Conversation::set_slot</c> (SI's <c>set_conversation_slot</c>): which face speaks next.</summary>
    public void SetSlot(int slot) => LastFace = slot;

    /// <summary>Exult <c>Conversation::remove_slot_face</c>: that slot's face goes; the last face left speaks next.</summary>
    public void RemoveSlotFace(int slot)
    {
        if ((uint)slot >= (uint)Faces.Length || Faces[slot] is null)
        {
            return;
        }

        Faces[slot] = null;
        FaceNames[slot] = "";
        FaceCount = Math.Max(0, FaceCount - 1);
        if (TextFace == slot)
        {
            TextFace = -1;
        }

        if (LastFace == slot)
        {
            LastFace = Array.FindLastIndex(Faces, f => f is not null);
        }
    }

    public void RemoveFace(int shape)
    {
        for (var i = 0; i < Faces.Length; i++)
        {
            if (Faces[i] is { } f && f.Shape == shape)
            {
                Faces[i] = null;
                FaceNames[i] = "";
                FaceCount = Math.Max(0, FaceCount - 1);
                if (TextFace == i)
                {
                    TextFace = -1;
                }
            }
        }

        if (LastFace >= 0 && Faces[LastFace] is null)
        {
            LastFace = Array.FindIndex(Faces, f => f is not null);
        }
    }

    public void InitFaces()
    {
        Array.Clear(Faces);
        FaceNames[0] = FaceNames[1] = "";
        FaceCount = 0;
        LastFace = -1;
        TextFace = -1;
        NpcText = "";
    }
}
