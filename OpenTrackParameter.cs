using System.Collections.Generic;

namespace GotaSequenceLib;

/// <summary>
///     Open track parameter.
/// </summary>
public class OpenTrackParameter
{
    /// <summary>
    ///     Label text.
    /// </summary>
    public string Label;

    public int m_Index;

    /// <summary>
    ///     Offset.
    /// </summary>
    public UInt24 Offset = 0;

    /// <summary>
    ///     Reference command.
    /// </summary>
    public SequenceCommand ReferenceCommand;

    /// <summary>
    ///     Track number.
    /// </summary>
    public byte TrackNumber;

    /// <summary>
    ///     Command index used when reading and writing.
    /// </summary>
    /// <param name="commands">The commands.</param>
    public int Index(List<SequenceCommand> commands)
    {
        var ind = m_Index;
        if (ReferenceCommand != null)
            if (ReferenceCommand.Index(commands) != -1)
                ind = ReferenceCommand.Index(commands);
        return ind;
    }
}