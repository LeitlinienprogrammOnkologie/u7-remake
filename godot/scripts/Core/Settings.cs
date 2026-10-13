using System.IO;
using Godot;

namespace U7.Core;

/// <summary>
/// The player's choices kept between runs, as Exult keeps its in
/// <c>exult.cfg</c>: <c>saves/settings.cfg</c> (a Godot config file, local
/// like the saves).
/// </summary>
public static class Settings
{
    static ConfigFile? _file;

    static string FilePath => Path.Combine(U7Paths.SavesRoot, "settings.cfg");

    public static Variant Get(string section, string key, Variant fallback)
    {
        Load();
        return _file!.GetValue(section, key, fallback);
    }

    public static void Set(string section, string key, Variant value)
    {
        Load();
        _file!.SetValue(section, key, value);
        Directory.CreateDirectory(U7Paths.SavesRoot);
        _file.Save(FilePath);
    }

    static void Load()
    {
        if (_file is not null)
        {
            return;
        }

        _file = new ConfigFile();
        if (File.Exists(FilePath))
        {
            _file.Load(FilePath);
        }
    }
}
