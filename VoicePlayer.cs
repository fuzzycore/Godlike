using NAudio.Wave;
using System;
using System.Collections.Generic;

namespace Godlike;

/// <summary>
/// Plays short audio clips (.wav / .mp3) through NAudio for the announcer.
///
/// Two playback modes:
///  - <see cref="Play"/>      primary callouts (kills). Interrupts whatever is playing AND drops
///    anything queued, so the newest kill callout always wins and lines never overlap.
///  - <see cref="Enqueue"/>   secondary callouts (Battle High rank-ups). Plays only once the current
///    clip finishes instead of cutting it off, so a kill callout that triggered the rank-up is heard
///    in full first. Plays immediately only when nothing is already playing.
/// </summary>
internal sealed class VoicePlayer : IDisposable
{
    private readonly object gate = new();

    // The clip currently playing (null when idle). When it stops naturally we advance the queue.
    private (WaveOutEvent Output, AudioFileReader Reader)? current;

    // Clips we've asked to Stop() (interrupted); kept only so PlaybackStopped can dispose them.
    private readonly List<(WaveOutEvent Output, AudioFileReader Reader)> stopping = new();

    // Deferred clips waiting for the current one to finish (Battle High lines behind a kill callout).
    private readonly Queue<(string Path, float Volume)> queue = new();

    private bool disposed;

    /// <summary>Primary callout: interrupt anything playing, drop anything queued, and play now.</summary>
    public void Play(string path, float volume)
    {
        if (disposed)
            return;

        lock (gate)
        {
            queue.Clear();
            StopCurrent();
            StartNow(path, volume);
        }
    }

    /// <summary>Secondary callout: play after the current clip finishes; play now only when idle.</summary>
    public void Enqueue(string path, float volume)
    {
        if (disposed)
            return;

        lock (gate)
        {
            if (current == null)
                StartNow(path, volume);
            else
                queue.Enqueue((path, volume));
        }
    }

    // Caller must hold gate. Starts a clip as the new current; on failure, falls through to the queue.
    private void StartNow(string path, float volume)
    {
        try
        {
            var reader = new AudioFileReader(path) { Volume = Math.Clamp(volume, 0f, 1f) };
            var output = new WaveOutEvent();
            output.Init(reader);
            output.PlaybackStopped += OnPlaybackStopped;
            current = (output, reader);
            output.Play();
        }
        catch
        {
            // Bad/unsupported file or no audio device - go idle and try whatever is queued next.
            current = null;
            PlayNext();
        }
    }

    // Caller must hold gate. Moves the current clip aside and asks it to stop; its PlaybackStopped
    // fires later and disposes it. Leaves current == null.
    private void StopCurrent()
    {
        if (current is { } c)
        {
            stopping.Add(c);
            current = null;
            try { c.Output.Stop(); } catch { /* ignore */ }
        }
    }

    // Caller must hold gate.
    private void PlayNext()
    {
        if (disposed || queue.Count == 0)
            return;

        var (path, volume) = queue.Dequeue();
        StartNow(path, volume);
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (sender is not WaveOutEvent output)
            return;

        lock (gate)
        {
            if (disposed)
                return;

            if (current?.Output == output)
            {
                // The active clip finished on its own - dispose it and advance the queue.
                Dispose(current.Value);
                current = null;
                PlayNext();
            }
            else
            {
                // An interrupted clip we Stop()'d earlier - just dispose it.
                var idx = stopping.FindIndex(x => x.Output == output);
                if (idx >= 0)
                {
                    Dispose(stopping[idx]);
                    stopping.RemoveAt(idx);
                }
            }
        }
    }

    private static void Dispose((WaveOutEvent Output, AudioFileReader Reader) clip)
    {
        try { clip.Output.Dispose(); } catch { /* ignore */ }
        try { clip.Reader.Dispose(); } catch { /* ignore */ }
    }

    public void Dispose()
    {
        List<(WaveOutEvent Output, AudioFileReader Reader)> snapshot;
        lock (gate)
        {
            disposed = true;
            queue.Clear();
            snapshot = new List<(WaveOutEvent, AudioFileReader)>(stopping);
            if (current is { } c)
                snapshot.Add(c);
            current = null;
            stopping.Clear();
        }

        foreach (var clip in snapshot)
            Dispose(clip);
    }
}
