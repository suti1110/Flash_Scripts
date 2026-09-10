using System;
using System.Threading.Tasks;
using UnityEngine;

public static class UnityRealtimeDelay
{
    public static Task WaitAsync(int milliseconds)
    {
        return WaitAsync(TimeSpan.FromMilliseconds(Math.Max(0, milliseconds)));
    }

    public static async Task WaitAsync(TimeSpan duration)
    {
        double seconds = Math.Max(0d, duration.TotalSeconds);
        if (seconds <= 0d)
            return;

        double deadline = Time.realtimeSinceStartupAsDouble + seconds;
        while (Application.isPlaying && Time.realtimeSinceStartupAsDouble < deadline)
            await Awaitable.NextFrameAsync();
    }
}
