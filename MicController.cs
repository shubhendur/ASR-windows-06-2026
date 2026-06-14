// ═══════════════════════════════════════════════════════════════════
//  MicController.cs — Microphone Sensitivity/Volume Control
//  Uses Windows Core Audio API (via NAudio) to set mic volume.
// ═══════════════════════════════════════════════════════════════════

using NAudio.CoreAudioApi;

namespace AsrService;

/// <summary>
/// Controls the default microphone's sensitivity (volume level)
/// using the Windows Core Audio API via NAudio.
/// </summary>
public static class MicController
{
    /// <summary>
    /// Sets the default microphone volume to the specified level.
    /// Also unmutes the mic if it is currently muted.
    /// </summary>
    /// <param name="volumePercent">Volume level from 0 to 100.</param>
    public static void SetMicVolume(int volumePercent)
    {
        float scalar = Math.Clamp(volumePercent / 100f, 0f, 1f);

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var mic = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);

            string deviceName = mic.FriendlyName;
            float oldVolume = mic.AudioEndpointVolume.MasterVolumeLevelScalar;
            bool wasMuted = mic.AudioEndpointVolume.Mute;

            // Set volume
            mic.AudioEndpointVolume.MasterVolumeLevelScalar = scalar;

            // Unmute if muted
            if (wasMuted)
            {
                mic.AudioEndpointVolume.Mute = false;
            }

            Console.WriteLine($"[Mic] Device: {deviceName}");
            Console.WriteLine($"[Mic] Volume: {oldVolume * 100:F0}% → {volumePercent}%{(wasMuted ? " (was muted, now unmuted)" : "")}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Mic] Failed to set microphone volume: {ex.Message}");
            Console.Error.WriteLine("[Mic] Continuing without mic sensitivity adjustment.");
        }
    }
}
