namespace Thermi.Core.Translations;

/// <summary>The parts of the names thermi gives what it makes: an overlay, from where it sits, and an item, from its sensor.</summary>
[Translated]
public enum NameText
{
    /// <summary>The name of an overlay made on the taskbar.</summary>
    Taskbar,

    /// <summary>The name of an overlay made at a free position.</summary>
    Floating,

    /// <summary>A name taken already, with the first free number after it.</summary>
    Numbered,

    /// <summary>An item's name taken by another sensor's item, with its own sensor's name after it.</summary>
    AfterSensor,

    /// <summary>A new item's name with no metric's to take: its device's kind and its type.</summary>
    OfSensor,

    /// <summary>The name of an overlay made at the top left corner, short as "Taskbar" is.</summary>
    TopLeft,

    /// <summary>The name of an overlay made at the top right corner.</summary>
    TopRight,

    /// <summary>The name of an overlay made at the bottom left corner.</summary>
    BottomLeft,

    /// <summary>The name of an overlay made at the bottom right corner.</summary>
    BottomRight,

    /// <summary>The label of the pill of a second GPU, as the graphics chip in a CPU beside a graphics card.</summary>
    OtherGpu,
}

/// <summary>
/// A new item's name with no metric's to take, from its device's kind: what it reads, {0}, of that device, which a language
/// may say with an article, the same its metrics' names say it with.
/// </summary>
[Translated]
public enum OfDevice
{
    Cpu,
    Gpu,
    Ram,
    Board,
    Ssd,
    Network,
    Battery,
    System,
}
