using System.Runtime.InteropServices;
using Godot;

internal sealed class AndroidRacePacing
{
    private const long FrameInterval = 1_000_000_000L / 30;
    private const int MonotonicClock = 1;
    private const int EglDraw = 0x3059;
    private const int EglExtensions = 0x3055;
    private PresentationTime? presentFrame;
    private nint previousSurface;
    private long nextPresentation;
    private bool unsupported;
    private bool reported;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Timespec
    {
        public readonly nint Seconds, Nanoseconds;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint PresentationTime(nint display, nint surface, long nanoseconds);

    [DllImport("libEGL.so")]
    private static extern nint eglGetCurrentDisplay();
    [DllImport("libEGL.so")]
    private static extern nint eglGetCurrentSurface(int buffer);
    [DllImport("libEGL.so")]
    private static extern nint eglQueryString(nint display, int name);
    [DllImport("libEGL.so")]
    private static extern nint eglGetProcAddress(string name);
    [DllImport("libc.so")]
    private static extern int clock_gettime(int clock, out Timespec time);

    public void Reset()
    {
        previousSurface = 0;
        nextPresentation = 0;
    }

    public void ScheduleFrame()
    {
        if (unsupported) return;
        try
        {
            nint display = eglGetCurrentDisplay();
            nint surface = eglGetCurrentSurface(EglDraw);
            if (display == 0 || surface == 0 || clock_gettime(MonotonicClock, out var clock) != 0) return;
            if (presentFrame is null)
            {
                string? extensions = Marshal.PtrToStringAnsi(eglQueryString(display, EglExtensions));
                nint function = extensions?.Split(' ').Contains("EGL_ANDROID_presentation_time") == true
                    ? eglGetProcAddress("eglPresentationTimeANDROID") : 0;
                if (function == 0) { unsupported = true; return; }
                presentFrame = Marshal.GetDelegateForFunctionPointer<PresentationTime>(function);
            }

            long now = (long)clock.Seconds * 1_000_000_000 + (long)clock.Nanoseconds;
            long earliest = now + FrameInterval / 2;
            if (surface != previousSurface || nextPresentation < now - FrameInterval * 3)
                nextPresentation = now + FrameInterval;
            // Keep the cadence after a late frame, dropping its elapsed presentation slots.
            while (nextPresentation < earliest) nextPresentation += FrameInterval;
            previousSurface = surface;
            if (presentFrame(display, surface, nextPresentation) != 0 && !reported)
            {
                reported = true;
                GD.Print("Yanshi XL Android presentation timestamps active.");
            }
            nextPresentation += FrameInterval;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            unsupported = true;
        }
    }
}
