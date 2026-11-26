using Thermi.Core.Editing;
using Thermi.Core.Settings;

namespace Thermi.Core.Agents;

/// <summary>
/// What the person lets AI agents do (<see cref="ThermiConfig.Agents"/>): while they only read, every change an agent asks
/// for is refused, the settings', a saved look's file, the start at sign-in's and the fan finder's alike.
/// </summary>
public static class AgentRules
{
    /// <summary>Why an agent may change nothing on settings that read as <paramref name="config"/>, or null when it may.</summary>
    public static Refusal? Refused(ThermiConfig config) => config.Agents == AgentAccess.Read ? Refusal.AgentsReadOnly : null;

    /// <summary>
    /// <paramref name="edit"/>, refused on settings whose agents only read: each write finds the file as it is then, so a
    /// person who stops them between an agent's read and its write stops that write too.
    /// </summary>
    public static Func<ThermiConfig, EditResult> Guarded(Func<ThermiConfig, EditResult> edit) =>
        config => Refused(config) is { } refused ? ConfigEdits.Refuse(config, refused) : edit(config);
}
