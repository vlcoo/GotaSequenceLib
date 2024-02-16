using System.Collections.Generic;

namespace GotaSequenceLib;

/// <summary>
///     UInt24 parameter.
/// </summary>
public class UInt24Parameter
{
    /// <summary>
    ///     Label.
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
    ///     Command index used when reading and writing.
    /// </summary>
    /// <param name="commands">The commands.</param>
    public int Index(List<SequenceCommand> commands)
    {
        return ReferenceCommand == null ? m_Index : ReferenceCommand.Index(commands);
    }
}