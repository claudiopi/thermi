namespace Thermi.Core.Frames;

/// <summary>
/// A window an app shows on screen. <c>Own</c> is true when the process that presents shows it, as a game does, not its
/// parent, as with a browser or an Electron app, whose frames come from a child.
/// </summary>
public readonly record struct AppWindow(nint Monitor, bool Fills, bool Own);
