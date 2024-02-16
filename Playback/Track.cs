using System;
using System.Collections.Generic;

namespace GotaSequenceLib.Playback;

/// <summary>
///     A track.
/// </summary>
public class Track
{
    /// <summary>
    ///     Player.
    /// </summary>
    private readonly Player _player;

    /// <summary>
    ///     Channel listing.
    /// </summary>
    public readonly List<Channel> Channels = new(0x10);

    /// <summary>
    ///     Track index.
    /// </summary>
    public readonly byte Index;

    //Public parameters.
    public bool Allocated;
    public byte Attack;
    public int BankNum;
    public int[] CallStack = new int[3];
    public byte CallStackDepth;
    public byte[] CallStackLoops = new byte[3];
    public int CurEvent;
    public byte Decay;
    public bool Enabled;
    public byte Expression;
    public byte Hold;
    public ushort LFODelay;
    public ushort LFODelayCount;
    public byte LFODepth;
    public ushort LFOPhase;
    public byte LFORange;
    public byte LFOSpeed;
    public LFOType LFOType;
    public bool Mono;
    public bool NoteDown;
    public sbyte Panpot;
    public sbyte PitchBend;
    public byte PitchBendRange;
    public bool Portamento;
    public byte PortamentoKey;
    public byte PortamentoTime;
    public byte Priority;
    public byte Release;
    public int Rest;
    public bool Stopped;
    public byte Sustain;
    public short SweepPitch;
    public bool Tie;
    public sbyte Transpose;
    public bool VariableFlag;
    public short[] Vars = new short[0x10];
    public int Voice;
    public byte Volume;
    public bool WaitingForNoteToFinishBeforeContinuingXD; // Is this necessary?

    /// <summary>
    ///     Create a new track.
    /// </summary>
    /// <param name="i">The track index.</param>
    /// <param name="player">The player.</param>
    public Track(byte i, Player player)
    {
        Index = i;
        _player = player;
    }

    /// <summary>
    ///     Get the true pitch.
    /// </summary>
    /// <returns>The true pitch.</returns>
    public int GetPitch()
    {
        var lfo = LFOType == LFOType.Pitch ? LFORange * Utils.Sin(LFOPhase >> 8) * LFODepth : 0;
        lfo = (int)(((long)lfo * 60) >> 14);
        return PitchBend * PitchBendRange / 2 + lfo;
    }

    /// <summary>
    ///     Get the true volume.
    /// </summary>
    /// <returns>The true volume.</returns>
    public int GetVolume()
    {
        var lfo = LFOType == LFOType.Volume ? LFORange * Utils.Sin(LFOPhase >> 8) * LFODepth : 0;
        lfo = (int)(((lfo & ~0xFC000000) >> 8) | ((lfo < 0 ? -1 : 0) << 6) | (((uint)lfo >> 26) << 18));
        return Utils.SustainTable[Math.Min((byte)127, _player.Volume)] +
               Utils.SustainTable[Math.Min((byte)127, Volume)] + Utils.SustainTable[Expression] + lfo;
    }

    /// <summary>
    ///     Get the true pan.
    /// </summary>
    /// <returns>The true pan.</returns>
    public sbyte GetPan()
    {
        var lfo = LFOType == LFOType.Panpot ? LFORange * Utils.Sin(LFOPhase >> 8) * LFODepth : 0;
        lfo = (int)(((lfo & ~0xFC000000) >> 8) | ((lfo < 0 ? -1 : 0) << 6) | (((uint)lfo >> 26) << 18));
        var p = Panpot + lfo;
        if (p < -0x40)
            p = -0x40;
        else if (p > 0x3F) p = 0x3F;
        return (sbyte)p;
    }

    /// <summary>
    ///     Set default values.
    /// </summary>
    public void Init()
    {
        Stopped = Tie = WaitingForNoteToFinishBeforeContinuingXD = Portamento = false;
        Allocated = Enabled = Index == 0;
        CurEvent = 0;
        Mono = VariableFlag = true;
        CallStackDepth = 0;
        Voice = LFODepth = 0;
        PitchBend = Panpot = Transpose = 0;
        LFOPhase = LFODelay = LFODelayCount = 0;
        LFORange = 1;
        LFOSpeed = 0x10;
        Priority = 0x40;
        Volume = Expression = 0x7F;
        Attack = Decay = Sustain = Release = 0xFF;
        PitchBendRange = 2;
        PortamentoKey = 60;
        PortamentoTime = 0;
        SweepPitch = 0;
        LFOType = LFOType.Pitch;
        Rest = 0;
        Vars = new short[] { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };
        StopAllChannels();
    }

    /// <summary>
    ///     Tick track.
    /// </summary>
    public void Tick()
    {
        if (Rest > 0) Rest--;
        if (Channels.Count != 0)
        {
            // TickNotes:
            for (var i = 0; i < Channels.Count; i++)
            {
                var c = Channels[i];
                if (c.NoteDuration > 0) c.NoteDuration--;
                if (!c.AutoSweep && c.SweepCounter < c.SweepLength) c.SweepCounter++;
            }

            // LFO:
            if (LFODelayCount > LFODelay)
            {
                var speed = LFOSpeed << 6; // "<< 6" is "* 0x40"
                var counter = (LFOPhase + speed) >> 8; // ">> 8" is "/ 0x100"
                while (counter >= 0x80) counter -= 0x80;
                LFOPhase += (ushort)speed;
                LFOPhase &= 0xFF;
                LFOPhase |= (ushort)(counter << 8); // "<< 8" is "* 0x100"
            }
            else
            {
                LFODelayCount++;
            }
        }
        else
        {
            WaitingForNoteToFinishBeforeContinuingXD = false;
            LFOPhase = 0;
            LFODelayCount = LFODelay;
        }
    }

    /// <summary>
    ///     Stop all channels.
    /// </summary>
    public void StopAllChannels()
    {
        var chans = Channels.ToArray();
        for (var i = 0; i < chans.Length; i++) chans[i].Stop();
    }
}

/// <summary>
///     LFO type.
/// </summary>
public enum LFOType
{
    Pitch,
    Volume,
    Panpot
}