namespace Thermi.Core.Frames;

/// <summary>A window an app shows on screen: the monitor it is on, and whether it fills it.</summary>
public readonly record struct AppWindow(nint Monitor, bool Fills);
