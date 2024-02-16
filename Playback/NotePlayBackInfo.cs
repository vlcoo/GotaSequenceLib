namespace GotaSequenceLib.Playback;

/// <summary>
///     Note playback info.
/// </summary>
public class NotePlayBackInfo
{
    /// <summary>
    ///     Attack.
    /// </summary>
    public byte Attack = 127;

    /// <summary>
    ///     Base key.
    /// </summary>
    public byte BaseKey = 60;

    /// <summary>
    ///     Decay.
    /// </summary>
    public byte Decay = 127;

    /// <summary>
    ///     Hold. TODO!!!
    /// </summary>
    public byte Hold = 0;

    /// <summary>
    ///     Instrument type.
    /// </summary>
    public InstrumentType InstrumentType;

    /// <summary>
    ///     Linear interpolation. TODO!!!
    /// </summary>
    public bool IsLinearInterpolation = false;

    /// <summary>
    ///     Key group. TODO!!!
    /// </summary>
    public byte KeyGroup = 0;

    /// <summary>
    ///     Pan.
    /// </summary>
    public byte Pan = 64;

    /// <summary>
    ///     Percussion mode. TODO!!!
    /// </summary>
    public bool PercussionMode = false;

    /// <summary>
    ///     Release.
    /// </summary>
    public byte Release = 127;

    /// <summary>
    ///     Surround pan. TODO!!!
    /// </summary>
    public sbyte SurroundPan;

    /// <summary>
    ///     Sustain.
    /// </summary>
    public byte Sustain = 127;

    /// <summary>
    ///     Tune. TODO!!!
    /// </summary>
    public float Tune = 1f;

    /// <summary>
    ///     Volume. TODO!!!
    /// </summary>
    public byte Volume = 127;

    /// <summary>
    ///     Wave archive Id.
    /// </summary>
    public int WarId;

    /// <summary>
    ///     Wave Id. Duty cycle if PSG.
    /// </summary>
    public int WaveId;
}