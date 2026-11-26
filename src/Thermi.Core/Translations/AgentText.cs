namespace Thermi.Core.Translations;

/// <summary>The Settings page's AI agents section: how to add thermi to an agent, the switch, and what agents changed last.</summary>
[Translated]
public enum AgentText
{
    Section,

    /// <summary>Under the switch: the one line on what agents do through thermi.</summary>
    WhatTheyDo,

    LetThemChange,

    /// <summary>The row of Claude Code's command.</summary>
    ClaudeCode,

    /// <summary>The row of the JSON for clients set up by a file.</summary>
    ByFile,

    /// <summary>Under it: which clients those are, and where the JSON goes.</summary>
    ByFileHow,

    Copy,

    Copied,

    /// <summary>A copy button's name, with its row's name.</summary>
    CopyOf,

    LastChanges,

    OpenLog,
}
