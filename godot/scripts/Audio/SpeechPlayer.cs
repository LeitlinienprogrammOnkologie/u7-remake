using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using U7.Core;
using U7.Data;

namespace U7.Audio;

/// <summary>
/// Exult's speech (<c>Audio::start_speech</c>, <c>playSpeechfile</c>,
/// <c>copy_and_play_speech</c>): one VOC at a time, from U7SPEECH.SPC by
/// number, a whole file (the intro's INTROSND.DAT), or a buffer (the
/// endgame's). Starting one stops the last (Exult keeps one speech id).
/// </summary>
public sealed partial class SpeechPlayer : Node
{
    readonly AudioStreamPlayer _player = new() { Name = "Voice" };
    FlexFile? _speech;
    List<FlexFile>? _patches;

    /// <summary>Exult's <c>speech_enabled</c> (its default: on); the console's games have none, as they have no music.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Exult's <c>speech_with_subs</c>: the words show while they are spoken
    /// (the user's pick, 2026-10-10; Exult's default is off).
    /// </summary>
    public bool WithSubtitles { get; set; } = true;

    /// <summary>Exult <c>is_speech_playing</c>.</summary>
    public bool IsPlaying => _player.Playing;

    public override void _Ready() => AddChild(_player);

    /// <summary>
    /// Exult <c>start_speech(num)</c>: U7SPEECH.SPC's entry, or the patch's
    /// (Exult's <c>PATCH_U7SPEECH</c> through <c>U7multiobject</c>: a mod's
    /// <c>u7speech.spc</c> wins where it has the entry); false if none plays.
    /// </summary>
    public bool PlaySpeech(int num)
    {
        if (!Enabled || num < 0)
        {
            return false;
        }

        _patches ??= U7Paths.PatchLayers("u7speech.spc").Reverse().Select(p => new FlexFile(p)).ToList();
        foreach (var patch in _patches)
        {
            if (num < patch.Count && patch.Get(num) is var voc && voc.Length > 0)
            {
                return Play(voc);
            }
        }

        var path = Path.Combine(U7Paths.StaticDir, "U7SPEECH.SPC");
        if (!File.Exists(path))
        {
            return false;
        }

        _speech ??= new FlexFile(path);
        return num < _speech.Count && Play(_speech.Get(num));
    }

    /// <summary>Exult <c>playSpeechfile</c>: a file that is one VOC.</summary>
    public bool PlayFile(string path) => Enabled && File.Exists(path) && Play(File.ReadAllBytes(path));

    /// <summary>Exult <c>copy_and_play_speech</c>.</summary>
    public bool Play(ReadOnlySpan<byte> voc)
    {
        Stop();
        if (!Enabled || VocFile.Decode(voc) is not { } stream)
        {
            return false;
        }

        _player.Stream = stream;
        _player.Play();
        return true;
    }

    /// <summary>Exult <c>stop_speech</c>.</summary>
    public void Stop()
    {
        if (_player.Playing)
        {
            _player.Stop();
        }
    }
}
