using NAudio.Wave;
using System;
using System.Collections.Generic;

namespace Godlike;

/// <summary>
/// Plays short audio clips (.wav / .mp3) through NAudio for the kill-streak voice announcer.
/// A new callout interrupts the previous one so voice lines never overlap.
/// </summary>
internal sealed class VoicePlayer : IDisposable
{
    private readonly object gate = new();
    private readonly List<(WaveOutEvent Output, AudioFileReader Reader)> active = new();
    private bool disposed;

    public void Play(string path, float volume)
    {
        if (disposed)
            return;

        // Interrupt anything currently playing so callouts don't stack.
        StopAll();

        try
        {
            var reader = new AudioFileReader(path) { Volume = Math.Clamp(volume, 0f, 1f) };
            var output = new WaveOutEvent();
            output.Init(reader);
            output.PlaybackStopped += (_, _) => Cleanup(output, reader);

            lock (gate)
                active.Add((output, reader));

            output.Play();
        }
        catch
        {
            // Bad/unsupported file or no audio device - just stay silent.
        }
    }

    private void StopAll()
    {
        List<(WaveOutEvent Output, AudioFileReader Reader)> snapshot;
        lock (gate)
            snapshot = new List<(WaveOutEvent, AudioFileReader)>(active);

        foreach (var (output, _) in snapshot)
        {
            try { output.Stop(); } catch { /* ignore */ }
        }
    }

    private void Cleanup(WaveOutEvent output, AudioFileReader reader)
    {
        lock (gate)
            active.RemoveAll(x => x.Output == output);

        try { output.Dispose(); } catch { /* ignore */ }
        try { reader.Dispose(); } catch { /* ignore */ }
    }

    public void Dispose()
    {
        disposed = true;
        List<(WaveOutEvent Output, AudioFileReader Reader)> snapshot;
        lock (gate)
        {
            snapshot = new List<(WaveOutEvent, AudioFileReader)>(active);
            active.Clear();
        }

        foreach (var (output, reader) in snapshot)
        {
            try { output.Dispose(); } catch { /* ignore */ }
            try { reader.Dispose(); } catch { /* ignore */ }
        }
    }
}
