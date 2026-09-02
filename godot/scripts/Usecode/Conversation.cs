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
    public string NpcText = "";
    public int FaceCount { get; private set; }

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

    public int LocateAnswer(string str) =>
        Answers.FindIndex(a => a.Equals(str, StringComparison.OrdinalIgnoreCase));

    public void ShowFace(int shape, int frame, int slot = -1)
    {
        if (slot < 0)
        {
            for (var i = 0; i < Faces.Length; i++)
            {
                if (Faces[i] is { } f && f.Shape == shape)
                {
                    Faces[i] = (shape, frame);
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
    }

    public void RemoveFace(int shape)
    {
        for (var i = 0; i < Faces.Length; i++)
        {
            if (Faces[i] is { } f && f.Shape == shape)
            {
                Faces[i] = null;
                FaceCount = Math.Max(0, FaceCount - 1);
            }
        }
    }

    public void InitFaces()
    {
        Array.Clear(Faces);
        FaceCount = 0;
        NpcText = "";
    }
}
