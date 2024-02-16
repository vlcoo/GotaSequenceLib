﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GotaSequenceLib.Playback;
using Sanford.Multimedia.Midi;
using Track = Sanford.Multimedia.Midi.Track;

namespace GotaSequenceLib;

/// <summary>
///     MIDI file.
/// </summary>
public static class SMF
{
    /// <summary>
    ///     Random.
    /// </summary>
    private static readonly Random _rand = new();

    /// <summary>
    ///     Create an MIDI from sequence commands.
    /// </summary>
    /// <param name="commands">Commands.</param>
    /// <param name="startIndex">The start index.</param>
    /// <param name="trackMask">Allowed tracks.</param>
    /// <returns>The sequence.</returns>
    public static Sequence FromSequenceCommands(List<SequenceCommand> commands, int startIndex,
        ushort trackMask = 0xFFFF)
    {
        //New sequence with default 960 ticks per quarter note.
        var m = new Sequence(960) { Format = 1 };

        //Add and read initial track.
        m.Add(new Track());
        var tickMap = new Dictionary<int, int>();
        short[] vars =
        {
            -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
            -1, -1, -1, -1, -1
        };
        WriteTrack(m, tickMap, commands, 0, m[0], m[0], startIndex, ref vars, 0, trackMask);

        //Get jump commands.
        var labelNum = 0;
        var jumpsAdded = new List<long>();
        var jumpsToAdd = new List<Tuple<int, int>>();

        //We aren't guaranteed to hit every instruction in the file, so we loop through tickMap, which contains the instructions that we did hit.
        foreach (var cmdTicks in
                 tickMap.Where(x => Player.GetTrueCommandType(commands[x.Key]) == SequenceCommands.Jump))
        {
            var ticksJump = cmdTicks.Value;
            var cmd = commands[cmdTicks.Key];
            var arg = Player.GetCommandParameter(cmd, 0, _rand, commands);
            var ticks = tickMap[arg];
            long tickHash = (ticks << 32) | ticksJump;
            if (!jumpsAdded.Contains(tickHash))
            {
                jumpsAdded.Add(tickHash);
                jumpsToAdd.Add(new Tuple<int, int>(ticks, ticksJump));
            }
        }

        //Add jumps.
        foreach (var j in jumpsToAdd)
        {
            m[0].Insert(j.Item1,
                new MetaMessage(MetaType.Marker,
                    Encoding.UTF8.GetBytes(jumpsToAdd.Count == 1 ? "[" : "Label_" + labelNum)));
            m[0].Insert(j.Item2,
                new MetaMessage(MetaType.Marker,
                    Encoding.UTF8.GetBytes(jumpsToAdd.Count == 1 ? "]" : "jump Label_" + labelNum)));
            labelNum++;
        }

        //Return the sequence.
        return m;
    }

    /// <summary>
    ///     Write a track.
    /// </summary>
    /// <param name="m">Sequence.</param>
    /// <param name="commands">Sequence commands.</param>
    /// <param name="tickMap">Tick map.</param>
    /// <param name="trackNum">Track number.</param>
    /// <param name="t">Track to read.</param>
    /// <param name="metaTrack">Meta track.</param>
    /// <param name="startIndex">Start index.</param>
    /// <param name="startTicks">Starting number of ticks.</param>
    /// <param name="vars">Variables.</param>
    /// <param name="trackMask">Allowed tracks.</param>
    /// <returns>The track.</returns>
    public static void WriteTrack(Sequence m, Dictionary<int, int> tickMap, List<SequenceCommand> commands,
        int trackNum, Track t, Track metaTrack, int startIndex, ref short[] vars, int startTicks = 0,
        ushort trackMask = 0xFFFF)
    {
        //Current command.
        var currCommand = startIndex;

        //Ticks.
        var ticks = startTicks;

        //Track pararmeters.
        var noteWait = true;
        var callStack = new int[3];
        var callStackDepth = 0;
        var tie = false;
        var lastNote = -1;
        var varFlag = false;
        short[] trackVars = { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };
        var timeBase = 48;
        var trackIndex = trackNum;

        //Get var.
        var Vars = vars;

        short GetVar(int varNum, int h)
        {
            if (varNum < 0x20)
                return Vars[varNum];
            return trackVars[varNum - 0x20];
        }

        //Set var.
        void SetVar(int varNum, int h, short val)
        {
            if (varNum < 0x20)
                Vars[varNum] = val;
            else
                trackVars[varNum - 0x20] = val;
        }

        //Loop forever.
        while (currCommand < commands.Count)
        {
            //Current command.
            var c = commands[currCommand];

            //Add ticks.
            if (!tickMap.ContainsKey(currCommand)) tickMap.Add(currCommand, ticks);

            //Fetch arguments.
            var numArgs = Player.NumArguments(c);
            var args = new int[numArgs];
            for (var i = 0; i < numArgs; i++) args[i] = Player.GetCommandParameter(c, i, _rand, commands);

            //Get true command type.
            var trueCommandType = Player.GetTrueCommandType(c);

            //No 0 track.
            if ((trackMask & 0b1) == 0)
            {
                if (trueCommandType == SequenceCommands.OpenTrack)
                    if (((0b1 << args[0]) & trackMask) > 0)
                    {
                        while (m.Count - 1 < args[0]) m.Add(new Track());
                        WriteTrack(m, tickMap, commands, args[0], m[args[0]], metaTrack, args[1], ref vars, ticks);
                    }

                currCommand++;
                continue;
            }

            //If command.
            if (c.CommandType == SequenceCommands.If && !varFlag)
            {
                currCommand++;
                continue;
            }

            //Switch type.
            switch (trueCommandType)
            {
                //Allocate trace.
                case SequenceCommands.AllocateTrack:
                    break;

                //Note.
                case SequenceCommands.Note:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.NoteOn, trackNum, args[0], args[1]));
                    if (!tie)
                        t.Insert(ticks + Sequence2MidiTicks(args[2], 960, timeBase),
                            new ChannelMessage(ChannelCommand.NoteOff, trackNum, args[0]));
                    lastNote = args[0];
                    if (noteWait) ticks += Sequence2MidiTicks(args[2], 960, timeBase);
                    break;

                //Wait.
                case SequenceCommands.Wait:
                    ticks += Sequence2MidiTicks(args[0], 960, timeBase);
                    break;

                //Program change.
                case SequenceCommands.ProgramChange:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.ProgramChange, trackNum, args[0]));
                    break;

                //Open track.
                case SequenceCommands.OpenTrack:
                    if (((0b1 << args[0]) & trackMask) > 0)
                    {
                        while (m.Count - 1 < args[0]) m.Add(new Track());
                        WriteTrack(m, tickMap, commands, args[0], m[args[0]], metaTrack, args[1], ref vars, ticks);
                    }

                    break;

                //Jump. Is implemented after.
                case SequenceCommands.Jump:
                    break;

                //Call.
                case SequenceCommands.Call:
                    if (callStackDepth < 3)
                    {
                        callStack[callStackDepth] = currCommand + 1;
                        callStackDepth++;
                        currCommand = args[0];
                        continue;
                    }

                    break;

                //Timebase.
                case SequenceCommands.Timebase:
                    timeBase = args[0];
                    break;

                //Hold.
                case SequenceCommands.EnvHold:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 79, args[0]));
                    break;

                //Monophonic.
                case SequenceCommands.Monophonic:
                    t.Insert(ticks,
                        new ChannelMessage(ChannelCommand.Controller, trackNum, 68, args[0] >= 0 ? 0x7F : 0));
                    break;

                //Biquad type.
                case SequenceCommands.BiquadType:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 30, args[0]));
                    break;

                //Biquad value.
                case SequenceCommands.BiquadValue:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 31, args[0]));
                    break;

                //Bank select.
                case SequenceCommands.BankSelect:
                    t.Insert(ticks,
                        new ChannelMessage(ChannelCommand.Controller, trackNum, (int)ControllerType.BankSelect,
                            args[0]));
                    break;

                //Pan.
                case SequenceCommands.Pan:
                    t.Insert(ticks,
                        new ChannelMessage(ChannelCommand.Controller, trackNum, (int)ControllerType.Pan, args[0]));
                    break;

                //Volume.
                case SequenceCommands.Volume:
                    t.Insert(ticks,
                        new ChannelMessage(ChannelCommand.Controller, trackNum, (int)ControllerType.Volume, args[0]));
                    break;

                //Main volume.
                case SequenceCommands.MainVolume:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 12, args[0]));
                    break;

                //Transpose.
                case SequenceCommands.Transpose:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 13, args[0] + 0x40));
                    break;

                //Pitch bend.
                case SequenceCommands.PitchBend:
                    var pitch = PitchBend2Midi(args[0] / 127d);
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.PitchWheel, trackNum, pitch.Item2, pitch.Item1));
                    break;

                //Pitch bend range. Pitch Range is selected by RPN 0, 0. The MSB is specified by control 6.
                case SequenceCommands.BendRange:
                    t.Insert(ticks,
                        new ChannelMessage(ChannelCommand.Controller, trackNum,
                            (int)ControllerType.RegisteredParameterCoarse, 0));
                    t.Insert(ticks,
                        new ChannelMessage(ChannelCommand.Controller, trackNum,
                            (int)ControllerType.RegisteredParameterFine, 0));
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 6, args[0]));
                    t.Insert(ticks,
                        new ChannelMessage(ChannelCommand.Controller, trackNum,
                            (int)ControllerType.RegisteredParameterCoarse, 127));
                    t.Insert(ticks,
                        new ChannelMessage(ChannelCommand.Controller, trackNum,
                            (int)ControllerType.RegisteredParameterFine, 127));
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 20, args[0]));
                    break;

                //Priority.
                case SequenceCommands.Prio:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 14, args[0]));
                    break;

                //Note wait.
                case SequenceCommands.NoteWait:
                    noteWait = args[0] > 0;
                    break;

                //Tie mode.
                case SequenceCommands.Tie:
                    tie = args[0] > 0;
                    t.Insert(ticks,
                        new ChannelMessage(ChannelCommand.Controller, trackNum, (int)ControllerType.AllNotesOff));
                    break;

                //Porta.
                case SequenceCommands.Porta:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 84, args[0]));
                    break;

                //Mod depth.
                case SequenceCommands.ModDepth:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 1, args[0]));
                    break;

                //Mod speed.
                case SequenceCommands.ModSpeed:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 21, args[0]));
                    break;

                //Mod type.
                case SequenceCommands.ModType:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 22, args[0]));
                    break;

                //Mod range.
                case SequenceCommands.ModRange:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 23, args[0]));
                    break;

                //Porta switch.
                case SequenceCommands.PortaSw:
                    t.Insert(ticks,
                        new ChannelMessage(ChannelCommand.Controller, trackNum, (int)ControllerType.Portamento,
                            args[0] > 0 ? 0x7F : 0));
                    break;

                //Porta time.
                case SequenceCommands.PortaTime:
                    t.Insert(ticks,
                        new ChannelMessage(ChannelCommand.Controller, trackNum, (int)ControllerType.PortamentoTime,
                            args[0]));
                    break;

                //Attack.
                case SequenceCommands.Attack:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 85, args[0]));
                    break;

                //Decay.
                case SequenceCommands.Decay:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 86, args[0]));
                    break;

                //Sustain.
                case SequenceCommands.Sustain:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 87, args[0]));
                    break;

                //Release.
                case SequenceCommands.Release:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 88, args[0]));
                    break;

                //Loop start.
                case SequenceCommands.LoopStart:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 89, args[0]));
                    break;

                //Volume 2.
                case SequenceCommands.Volume2:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 11, args[0]));
                    break;

                //FX send A.
                case SequenceCommands.FxSendA:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 91, args[0]));
                    break;

                //FX send B.
                case SequenceCommands.FxSendB:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 92, args[0]));
                    break;

                //Main send.
                case SequenceCommands.MainSend:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 95, args[0]));
                    break;

                //Surround pan.
                case SequenceCommands.SurroundPan:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 9, args[0]));
                    break;

                //Initial pan.
                case SequenceCommands.InitPan:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 3, args[0]));
                    break;

                //FX send C.
                case SequenceCommands.FxSendC:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 93, args[0]));
                    break;

                //Damper.
                case SequenceCommands.Damper:
                    t.Insert(ticks,
                        new ChannelMessage(ChannelCommand.Controller, trackNum, 64, args[0] >= 0 ? 0x7F : 0));
                    break;

                //Mod delay.
                case SequenceCommands.ModDelay:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 26, args[0]));
                    break;

                //Tempo.
                case SequenceCommands.Tempo:
                    var change = new TempoChangeBuilder { Tempo = 60000000 / args[0] };
                    change.Build();
                    metaTrack.Insert(ticks, change.Result);
                    break;

                //Loop end.
                case SequenceCommands.LoopEnd:
                    t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 90));
                    break;

                //Return.
                case SequenceCommands.Return:
                    if (callStackDepth != 0)
                    {
                        callStackDepth--;
                        currCommand = callStack[callStackDepth];
                        continue;
                    }

                    break;

                //Fin.
                case SequenceCommands.Fin:
                    return;

                //Set var.
                case SequenceCommands.SetVar:
                    switch (args[0])
                    {
                        case 0:
                            t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 16, args[1]));
                            break;
                        case 1:
                            t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 17, args[1]));
                            break;
                        case 2:
                            t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 18, args[1]));
                            break;
                        case 3:
                            t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 19, args[1]));
                            break;
                        case 32:
                            t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 80, args[1]));
                            break;
                        case 33:
                            t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 81, args[1]));
                            break;
                        case 34:
                            t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 82, args[1]));
                            break;
                        case 35:
                            t.Insert(ticks, new ChannelMessage(ChannelCommand.Controller, trackNum, 83, args[1]));
                            break;
                        default:
                            metaTrack.Insert(ticks,
                                new MetaMessage(MetaType.Marker, Encoding.UTF8.GetBytes(trackNum + ": " + c)));
                            break;
                    }

                    break;

                //Add var.
                case SequenceCommands.AddVar:
                    SetVar(args[0], trackIndex, (short)(GetVar(args[0], trackIndex) + args[1]));
                    metaTrack.Insert(ticks,
                        new MetaMessage(MetaType.Marker, Encoding.UTF8.GetBytes(trackNum + ": " + c)));
                    break;

                //Sub var.
                case SequenceCommands.SubVar:
                    SetVar(args[0], trackIndex, (short)(GetVar(args[0], trackIndex) - args[1]));
                    metaTrack.Insert(ticks,
                        new MetaMessage(MetaType.Marker, Encoding.UTF8.GetBytes(trackNum + ": " + c)));
                    break;

                //Mul var.
                case SequenceCommands.MulVar:
                    SetVar(args[0], trackIndex, (short)(GetVar(args[0], trackIndex) * args[1]));
                    metaTrack.Insert(ticks,
                        new MetaMessage(MetaType.Marker, Encoding.UTF8.GetBytes(trackNum + ": " + c)));
                    break;

                //Div var.
                case SequenceCommands.DivVar:
                    SetVar(args[0], trackIndex, (short)(GetVar(args[0], trackIndex) / args[1]));
                    metaTrack.Insert(ticks,
                        new MetaMessage(MetaType.Marker, Encoding.UTF8.GetBytes(trackNum + ": " + c)));
                    break;

                //Shift var.
                case SequenceCommands.ShiftVar:
                    SetVar(args[0], trackIndex,
                        args[1] < 0
                            ? (short)(GetVar(args[0], trackIndex) >> -args[1])
                            : (short)(GetVar(args[0], trackIndex) << args[1]));
                    metaTrack.Insert(ticks,
                        new MetaMessage(MetaType.Marker, Encoding.UTF8.GetBytes(trackNum + ": " + c)));
                    break;

                //Rand var.
                case SequenceCommands.RandVar:
                {
                    var negate = false;
                    if (args[1] < 0)
                    {
                        negate = true;
                        args[1] = (short)-args[1];
                    }

                    var val = (short)_rand.Next(args[1] + 1);
                    if (negate) val = (short)-val;
                    SetVar(args[0], trackIndex, val);
                    metaTrack.Insert(ticks,
                        new MetaMessage(MetaType.Marker, Encoding.UTF8.GetBytes(trackNum + ": " + c)));
                    break;
                }

                //And var.
                case SequenceCommands.AndVar:
                    SetVar(args[0], trackIndex, (short)(GetVar(args[0], trackIndex) & args[1]));
                    metaTrack.Insert(ticks,
                        new MetaMessage(MetaType.Marker, Encoding.UTF8.GetBytes(trackNum + ": " + c)));
                    break;

                //Or var.
                case SequenceCommands.OrVar:
                    SetVar(args[0], trackIndex, (short)(GetVar(args[0], trackIndex) | (short)args[1]));
                    metaTrack.Insert(ticks,
                        new MetaMessage(MetaType.Marker, Encoding.UTF8.GetBytes(trackNum + ": " + c)));
                    break;

                //Xor var.
                case SequenceCommands.XorVar:
                    SetVar(args[0], trackIndex, (short)(GetVar(args[0], trackIndex) ^ args[1]));
                    metaTrack.Insert(ticks,
                        new MetaMessage(MetaType.Marker, Encoding.UTF8.GetBytes(trackNum + ": " + c)));
                    break;

                //Not var.
                case SequenceCommands.NotVar:
                    SetVar(args[0], trackIndex,
                        (short)(~(GetVar(args[0], trackIndex) & args[1]) | (GetVar(args[0], trackIndex) & ~args[0])));
                    metaTrack.Insert(ticks,
                        new MetaMessage(MetaType.Marker, Encoding.UTF8.GetBytes(trackNum + ": " + c)));
                    break;

                //Mod var.
                case SequenceCommands.ModVar:
                    SetVar(args[0], trackIndex, (short)(GetVar(args[0], trackIndex) % args[1]));
                    metaTrack.Insert(ticks,
                        new MetaMessage(MetaType.Marker, Encoding.UTF8.GetBytes(trackNum + ": " + c)));
                    break;

                //Compare equal.
                case SequenceCommands.CmpEq:
                    varFlag = GetVar(args[0], trackIndex) == args[1];
                    metaTrack.Insert(ticks,
                        new MetaMessage(MetaType.Marker, Encoding.UTF8.GetBytes(trackNum + ": " + c)));
                    break;

                //Compare greater than or equal.
                case SequenceCommands.CmpGe:
                    varFlag = GetVar(args[0], trackIndex) >= args[1];
                    metaTrack.Insert(ticks,
                        new MetaMessage(MetaType.Marker, Encoding.UTF8.GetBytes(trackNum + ": " + c)));
                    break;

                //Compare greater than.
                case SequenceCommands.CmpGt:
                    varFlag = GetVar(args[0], trackIndex) > args[1];
                    metaTrack.Insert(ticks,
                        new MetaMessage(MetaType.Marker, Encoding.UTF8.GetBytes(trackNum + ": " + c)));
                    break;

                //Compare less than or equal.
                case SequenceCommands.CmpLe:
                    varFlag = GetVar(args[0], trackIndex) <= args[1];
                    metaTrack.Insert(ticks,
                        new MetaMessage(MetaType.Marker, Encoding.UTF8.GetBytes(trackNum + ": " + c)));
                    break;

                //Compare less than.
                case SequenceCommands.CmpLt:
                    varFlag = GetVar(args[0], trackIndex) < args[1];
                    metaTrack.Insert(ticks,
                        new MetaMessage(MetaType.Marker, Encoding.UTF8.GetBytes(trackNum + ": " + c)));
                    break;

                //Compare not equal.
                case SequenceCommands.CmpNe:
                    varFlag = GetVar(args[0], trackIndex) != args[1];
                    metaTrack.Insert(ticks,
                        new MetaMessage(MetaType.Marker, Encoding.UTF8.GetBytes(trackNum + ": " + c)));
                    break;

                //Not implemented.
                default:
                    metaTrack.Insert(ticks,
                        new MetaMessage(MetaType.Marker, Encoding.UTF8.GetBytes(trackNum + ": " + c)));
                    break;
            }

            //Increment command number.
            currCommand++;
        }
    }

    /// <summary>
    ///     Convert an MIDI to sequence commands.
    /// </summary>
    /// <param name="s">Sequence.</param>
    /// <param name="labels">Labels.</param>
    /// <param name="privateLabels">Private labels.</param>
    /// <param name="sequenceName">Sequence name.</param>
    /// <param name="timeBase">Time base.</param>
    /// <param name="privateLabelsForCalls">If the use private labels for calls.</param>
    /// <returns>The sequence commands.</returns>
    public static List<SequenceCommand> ToSequenceCommands(Sequence s, out Dictionary<string, int> labels,
        out List<int> privateLabels, string sequenceName, int timeBase = 48, bool privateLabelsForCalls = false)
    {
        //Commands.
        var commands = new List<SequenceCommand>();

        //Labels.
        labels = new Dictionary<string, int>();
        privateLabels = new List<int>();
        labels.Add("SMF_" + sequenceName + "_Begin", 0);

        //Allocate tracks.
        var allocs = new List<int>();
        if (s.Count > 1)
        {
            ushort alloc = 0;
            for (var i = 0; i < s.Count; i++)
            {
                //if (s[i].Count > 0x32) {
                alloc |= (ushort)(0b1 << i);
                allocs.Add(i);
                //}
            }

            commands.Add(new SequenceCommand { CommandType = SequenceCommands.AllocateTrack, Parameter = alloc });
        }

        //Add open track parameters.
        var openTrackOff = commands.Count;
        for (var i = 1; i < s.Count; i++)
            commands.Add(new SequenceCommand
            {
                CommandType = SequenceCommands.OpenTrack, Parameter = new OpenTrackParameter { TrackNumber = (byte)i }
            });

        //Other helpers.
        var otherLabelTicks = new Dictionary<string, int>();
        var loopStartTicks = -1;
        var loopEndTicks = -1;

        //Read tracks.
        labels.Add("SMF_" + sequenceName + "_Start", 1);
        for (var i = 0; i < allocs.Count; i++)
        {
            labels.Add("SMF_" + sequenceName + "_Track_" + allocs[i], commands.Count);
            if (i != 0) (commands[1 + i - 1].Parameter as OpenTrackParameter).m_Index = commands.Count;
            ReadTrack(commands, s, allocs[i], openTrackOff, labels, timeBase, sequenceName, otherLabelTicks,
                ref loopStartTicks, ref loopEndTicks);
        }

        //Terminating fin.
        labels.Add("SMF_" + sequenceName + "_End", commands.Count);
        commands.Add(new SequenceCommand { CommandType = SequenceCommands.Fin });

        //Convert all indices to references.

        //Optimize the sequence.

        //Return commands.
        return commands;
    }

    /// <summary>
    ///     Read a track.
    /// </summary>
    /// <param name="commands">Commands.</param>
    /// <param name="s">Sequence.</param>
    /// <param name="trackNum">Track number.</param>
    /// <param name="openTrackOffset">Open track offset.</param>
    /// <param name="labels">Labels.</param>
    /// <param name="timeBase">Time base.</param>
    /// <param name="sequenceName">Sequence name.</param>
    public static void ReadTrack(List<SequenceCommand> commands, Sequence s, int trackNum, int openTrackOffset,
        Dictionary<string, int> labels, int timeBase, string sequenceName, Dictionary<string, int> otherLabelTicks,
        ref int loopStartTicks, ref int loopEndTicks)
    {
        //Event pointer.
        var startTrackPointer = commands.Count;

        //Events.
        var events = new List<MidiEvent>();
        for (var i = 0; i < s[trackNum].Count; i++) events.Add(s[trackNum].GetMidiEvent(i));
        events = events.OrderBy(x => x.AbsoluteTicks).ToList();

        //No notewait.
        commands.Add(new SequenceCommand { CommandType = SequenceCommands.NoteWait, Parameter = false });

        //Loop start command index.
        var loopStart = -1;
        var loopAdded = false;

        //Read each event.
        var eventNum = 0;
        var lastTick = 0;
        foreach (var e in events)
        {
            //Overtime.
            uint overtime = 0;

            //Loop.
            /*if (loopEndTicks != -1 && loopStartTicks != -1 && !loopAdded) {
                if (events.Count == 1 || (e.AbsoluteTicks <= loopStartTicks && loopStartTicks < events[eventNum + 1].AbsoluteTicks)) {
                    if (loopStartTicks < e.AbsoluteTicks) {
                        commands.Add(new SequenceCommand() { CommandType = SequenceCommands.Wait, Parameter = (uint)Midi2SequenceTicks(loopStartTicks - lastTick, s.Division, timeBase) });
                    } else if (loopStartTicks > e.AbsoluteTicks) {
                        commands.Add(new SequenceCommand() { CommandType = SequenceCommands.Wait, Parameter = (uint)Midi2SequenceTicks(loopStartTicks - e.AbsoluteTicks, s.Division, timeBase) });
                    }
                    labels.Add("SMF_" + sequenceName + "_Track_" + trackNum + "_SSN_LOOPSTART", commands.Count);
                    loopStart = commands.Count;
                }
            }
            if (loopEndTicks != -1 && loopStartTicks != -1 && !loopAdded) {
                if (events.Count == 1 || (e.AbsoluteTicks <= loopEndTicks && loopEndTicks < events[eventNum + 1].AbsoluteTicks)) {
                    if (loopEndTicks < e.AbsoluteTicks) {
                        commands.Add(new SequenceCommand() { CommandType = SequenceCommands.Wait, Parameter = (uint)Midi2SequenceTicks(loopEndTicks - lastTick, s.Division, timeBase) });
                    } else if (loopEndTicks > e.AbsoluteTicks) {
                        commands.Add(new SequenceCommand() { CommandType = SequenceCommands.Wait, Parameter = (uint)Midi2SequenceTicks(loopEndTicks - e.AbsoluteTicks, s.Division, timeBase) });
                    }
                    commands.Add(new SequenceCommand() { CommandType = SequenceCommands.Jump, Parameter = new UInt24Parameter() { m_Index = loopStart } });
                    loopAdded = true;
                }
            }*/

            //Switch event type.
            switch (e.MidiMessage.MessageType)
            {
                //Channel.
                case MessageType.Channel:
                    var con = e.MidiMessage as ChannelMessage;
                    switch (con.Command)
                    {
                        //Note.
                        case ChannelCommand.NoteOn:

                            //Get length.
                            var len = 0;
                            var key = con.Data1;
                            AddWaitTime();
                            for (var i = eventNum + 1; i < events.Count; i++)
                                if (events[i].MidiMessage as ChannelMessage != null &&
                                    (events[i].MidiMessage as ChannelMessage).Command == ChannelCommand.NoteOff &&
                                    (events[i].MidiMessage as ChannelMessage).Data1 == key)
                                {
                                    len = Midi2SequenceTicks(events[i].AbsoluteTicks - e.AbsoluteTicks, s.Division,
                                        timeBase);
                                    break;
                                }

                            commands.Add(new SequenceCommand
                            {
                                CommandType = SequenceCommands.Note,
                                Parameter = new NoteParameter
                                    { Note = (Notes)key, Velocity = (byte)con.Data2, Length = (uint)len }
                            });
                            overtime = (uint)len;
                            break;

                        //Program change.
                        case ChannelCommand.ProgramChange:
                            AddWaitTime();
                            commands.Add(new SequenceCommand
                                { CommandType = SequenceCommands.ProgramChange, Parameter = (uint)con.Data1 });
                            break;

                        //Pitch.
                        case ChannelCommand.PitchWheel:
                            AddWaitTime();
                            commands.Add(new SequenceCommand
                            {
                                CommandType = SequenceCommands.PitchBend,
                                Parameter = (sbyte)(Midi2PitchBend(con.Data2, con.Data1) * 127)
                            });
                            break;

                        //Controller.
                        case ChannelCommand.Controller:
                            switch ((ControllerType)con.Data1)
                            {
                                //Bank select.
                                case ControllerType.BankSelect:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.BankSelect, Parameter = (byte)con.Data2 });
                                    break;

                                //Mod depth.
                                case (ControllerType)1:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.ModDepth, Parameter = (byte)con.Data2 });
                                    break;

                                //Initial pan.
                                case (ControllerType)3:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.InitPan, Parameter = (byte)con.Data2 });
                                    break;

                                //Portament time.
                                case ControllerType.PortamentoTime:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.PortaTime, Parameter = (byte)con.Data2 });
                                    break;

                                //Volume.
                                case ControllerType.Volume:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.Volume, Parameter = (byte)con.Data2 });
                                    break;

                                //Surround pan.
                                case (ControllerType)9:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.SurroundPan, Parameter = (byte)con.Data2 });
                                    break;

                                //Pan.
                                case ControllerType.Pan:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.Pan, Parameter = (byte)con.Data2 });
                                    break;

                                //Volume 2.
                                case (ControllerType)11:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.Volume2, Parameter = (byte)con.Data2 });
                                    break;

                                //Main volume.
                                case (ControllerType)12:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.MainVolume, Parameter = (byte)con.Data2 });
                                    break;

                                //Transpose.
                                case (ControllerType)13:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                    {
                                        CommandType = SequenceCommands.Transpose, Parameter = (sbyte)(con.Data2 - 0x40)
                                    });
                                    break;

                                //Priority.
                                case (ControllerType)14:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.Prio, Parameter = (byte)con.Data2 });
                                    break;

                                //Set var 0.
                                case (ControllerType)16:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                    {
                                        CommandType = SequenceCommands.SetVar,
                                        Parameter = new U8S16Parameter { U8 = 0, S16 = (short)con.Data2 }
                                    });
                                    break;

                                //Set var 1.
                                case (ControllerType)17:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                    {
                                        CommandType = SequenceCommands.SetVar,
                                        Parameter = new U8S16Parameter { U8 = 1, S16 = (short)con.Data2 }
                                    });
                                    break;

                                //Set var 2.
                                case (ControllerType)18:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                    {
                                        CommandType = SequenceCommands.SetVar,
                                        Parameter = new U8S16Parameter { U8 = 2, S16 = (short)con.Data2 }
                                    });
                                    break;

                                //Set var 3.
                                case (ControllerType)19:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                    {
                                        CommandType = SequenceCommands.SetVar,
                                        Parameter = new U8S16Parameter { U8 = 3, S16 = (short)con.Data2 }
                                    });
                                    break;

                                //Bend range.
                                case (ControllerType)20:
                                    AddWaitTime();
                                    if (commands.Last().CommandType == SequenceCommands.BendRange) break;
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.BendRange, Parameter = (byte)con.Data2 });
                                    break;

                                //Mod speed.
                                case (ControllerType)21:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.ModSpeed, Parameter = (byte)con.Data2 });
                                    break;

                                //Mod type.
                                case (ControllerType)22:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.ModType, Parameter = (byte)con.Data2 });
                                    break;

                                //Mod range.
                                case (ControllerType)23:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.ModRange, Parameter = (byte)con.Data2 });
                                    break;

                                //Mod delay.
                                case (ControllerType)26:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.ModDelay, Parameter = (short)con.Data2 });
                                    break;

                                //Mod delay.
                                case (ControllerType)27:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                    {
                                        CommandType = SequenceCommands.ModDelay, Parameter = (short)(con.Data2 * 10)
                                    });
                                    break;

                                //Sweep pitch.
                                case (ControllerType)28:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                    {
                                        CommandType = SequenceCommands.SweepPitch, Parameter = (short)(con.Data2 - 0x40)
                                    });
                                    break;

                                //Sweep pitch.
                                case (ControllerType)29:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                    {
                                        CommandType = SequenceCommands.SweepPitch,
                                        Parameter = (short)((con.Data2 - 0x40) * 24)
                                    });
                                    break;

                                //Biquad type.
                                case (ControllerType)30:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.BiquadType, Parameter = (byte)con.Data2 });
                                    break;

                                //Biquad value.
                                case (ControllerType)31:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.BiquadValue, Parameter = (byte)con.Data2 });
                                    break;

                                //Damper.
                                case (ControllerType)64:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.Damper, Parameter = con.Data2 >= 64 });
                                    break;

                                //Porta switch.
                                case ControllerType.Portamento:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.PortaSw, Parameter = con.Data2 >= 64 });
                                    break;

                                //Monophonic.
                                case ControllerType.LegatoPedal:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.Monophonic, Parameter = con.Data2 >= 64 });
                                    break;

                                //Hold.
                                case (ControllerType)79:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.EnvHold, Parameter = (byte)con.Data2 });
                                    break;

                                //Set var 32.
                                case (ControllerType)80:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                    {
                                        CommandType = SequenceCommands.SetVar,
                                        Parameter = new U8S16Parameter { U8 = 32, S16 = (short)con.Data2 }
                                    });
                                    break;

                                //Set var 33.
                                case (ControllerType)81:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                    {
                                        CommandType = SequenceCommands.SetVar,
                                        Parameter = new U8S16Parameter { U8 = 33, S16 = (short)con.Data2 }
                                    });
                                    break;

                                //Set var 34.
                                case (ControllerType)82:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                    {
                                        CommandType = SequenceCommands.SetVar,
                                        Parameter = new U8S16Parameter { U8 = 34, S16 = (short)con.Data2 }
                                    });
                                    break;

                                //Set var 35.
                                case (ControllerType)83:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                    {
                                        CommandType = SequenceCommands.SetVar,
                                        Parameter = new U8S16Parameter { U8 = 35, S16 = (short)con.Data2 }
                                    });
                                    break;

                                //Porta.
                                case (ControllerType)84:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.Porta, Parameter = (byte)con.Data2 });
                                    break;

                                //Attack.
                                case (ControllerType)85:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.Attack, Parameter = (byte)con.Data2 });
                                    break;

                                //Decay.
                                case (ControllerType)86:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.Decay, Parameter = (byte)con.Data2 });
                                    break;

                                //Sustain.
                                case (ControllerType)87:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.Sustain, Parameter = (byte)con.Data2 });
                                    break;

                                //Release.
                                case (ControllerType)88:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.Release, Parameter = (byte)con.Data2 });
                                    break;

                                //Loop start.
                                case (ControllerType)89:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.LoopStart, Parameter = (byte)con.Data2 });
                                    break;

                                //Loop end.
                                case (ControllerType)90:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand { CommandType = SequenceCommands.LoopEnd });
                                    break;

                                //FX send A.
                                case (ControllerType)91:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.FxSendA, Parameter = (byte)con.Data2 });
                                    break;

                                //FX send B.
                                case (ControllerType)92:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.FxSendB, Parameter = (byte)con.Data2 });
                                    break;

                                //FX send C.
                                case (ControllerType)93:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.FxSendC, Parameter = (byte)con.Data2 });
                                    break;

                                //Main send.
                                case (ControllerType)95:
                                    AddWaitTime();
                                    commands.Add(new SequenceCommand
                                        { CommandType = SequenceCommands.MainSend, Parameter = (byte)con.Data2 });
                                    break;

                                //Pitch bend range.
                                case ControllerType.RegisteredParameterCoarse:
                                    if (con.Data2 == 0 && (RPNFine0(1) || RPNFine0(-1)))
                                    {
                                        if (RPNCourse(1))
                                        {
                                            AddWaitTime();
                                            if (commands.Last().CommandType == SequenceCommands.BendRange) break;
                                            commands.Add(new SequenceCommand
                                            {
                                                CommandType = SequenceCommands.BendRange,
                                                Parameter = (byte)(events[eventNum + 1].MidiMessage as ChannelMessage)
                                                    .Data2
                                            });
                                        }
                                        else if (RPNCourse(2))
                                        {
                                            AddWaitTime();
                                            if (commands.Last().CommandType == SequenceCommands.BendRange) break;
                                            commands.Add(new SequenceCommand
                                            {
                                                CommandType = SequenceCommands.BendRange,
                                                Parameter = (byte)(events[eventNum + 2].MidiMessage as ChannelMessage)
                                                    .Data2
                                            });
                                        }
                                    }

                                    bool RPNFine0(int eventOff)
                                    {
                                        var off = eventNum + eventOff;
                                        if (off < 0 || off >= events.Count) return false;
                                        if (events[off].MidiMessage as ChannelMessage != null)
                                            if ((events[off].MidiMessage as ChannelMessage).Command ==
                                                ChannelCommand.Controller &&
                                                (events[off].MidiMessage as ChannelMessage).Data1 ==
                                                (int)ControllerType.RegisteredParameterFine &&
                                                (events[off].MidiMessage as ChannelMessage).Data2 == 0)
                                                return true;
                                        return false;
                                    }

                                    bool RPNCourse(int eventOff)
                                    {
                                        var off = eventNum + eventOff;
                                        if (off < 0 || off >= events.Count) return false;
                                        if (events[off].MidiMessage as ChannelMessage != null)
                                            if ((events[off].MidiMessage as ChannelMessage).Command ==
                                                ChannelCommand.Controller &&
                                                (events[off].MidiMessage as ChannelMessage).Data1 == 6)
                                                return true;
                                        return false;
                                    }

                                    break;
                            }

                            break;
                    }

                    break;

                //Meta.
                case MessageType.Meta:
                    var met = e.MidiMessage as MetaMessage;
                    switch (met.MetaType)
                    {
                        //Tempo.
                        case MetaType.Tempo:
                            AddWaitTime();
                            var tempoRaw = met.GetBytes();
                            var tempoVal = (uint)((tempoRaw[0] << 16) | (tempoRaw[1] << 8) | tempoRaw[2]);
                            commands.Add(new SequenceCommand
                                { CommandType = SequenceCommands.Tempo, Parameter = (short)(60000000 / tempoVal) });
                            break;

                        //Jump.
                        case MetaType.CuePoint:
                        case MetaType.Marker:
                            AddWaitTime();
                            var dat = Encoding.UTF8.GetString(met.GetBytes());
                            if (dat.Contains(": "))
                            {
                                try
                                {
                                    var
                                        c = new SequenceCommand(); //THIS DOES NOT TAKE CARE OF JUMPS AS IT WILL JUMP TO THE MARKER TRACK.
                                    if (int.Parse(dat.Split(':')[0]) == trackNum)
                                    {
                                        c.FromString(dat.Substring(dat.IndexOf(":") + 2), labels,
                                            new Dictionary<string, int>());
                                        commands.Add(c);
                                    }
                                }
                                catch
                                {
                                }
                            }
                            else
                            {
                                var loopStartStr = "SMF_" + sequenceName + "_Track_" + trackNum + "_SSN_LOOPSTART";
                                var loopEndStr = "SMF_" + sequenceName + "_Track_" + trackNum + "_SSN_LOOPEND";
                                if (!labels.ContainsKey(loopStartStr) && (dat.Equals("[") ||
                                                                          dat.ToLower().Equals("loopstart") ||
                                                                          dat.ToLower().Equals("loop_start")))
                                {
                                    labels.Add(loopStartStr, commands.Count);
                                    loopStartTicks = e.AbsoluteTicks;
                                }
                                else if (!labels.ContainsKey(loopEndStr) && (dat.Equals("]") ||
                                                                             dat.ToLower().Equals("loopend") ||
                                                                             dat.ToLower().Equals("loop_end")))
                                {
                                    loopEndTicks = e.AbsoluteTicks;
                                }
                                else
                                {
                                    labels.Add(dat, commands.Count);
                                }
                            }

                            break;
                    }

                    break;
            }

            //Event number.
            eventNum++;

            //Prevent cutting off from notes.
            if (eventNum == events.Count && overtime != 0)
                commands.Add(new SequenceCommand { CommandType = SequenceCommands.Wait, Parameter = overtime });

            //Add wait time.
            void AddWaitTime()
            {
                var waitTime = e.AbsoluteTicks - lastTick;
                if (waitTime != 0)
                    commands.Add(new SequenceCommand
                    {
                        CommandType = SequenceCommands.Wait,
                        Parameter = (uint)Midi2SequenceTicks(waitTime, s.Division, timeBase)
                    });
                lastTick = e.AbsoluteTicks;
            }
        }

        //Add finish.
        commands.Add(new SequenceCommand { CommandType = SequenceCommands.Fin });

        //Set track offset.
        if (trackNum != 0)
            (commands[openTrackOffset + trackNum - 1].Parameter as OpenTrackParameter).ReferenceCommand =
                commands[startTrackPointer];
    }

    /// <summary>
    ///     Convert pitch bend to MIDI.
    /// </summary>
    /// <param name="pitchAmount">Change in pitch amount from -1 to 1.</param>
    /// <returns>The MSB and LSB.</returns>
    public static Tuple<int, int> PitchBend2Midi(double pitchAmount)
    {
        //0 value is 0x2000.
        ushort zeroPitch = 0x2000;

        //Get value.
        var pitch = (ushort)(zeroPitch + pitchAmount * 0x2000);
        if (pitch > 0x3FFF) pitch = 0x3FFF;

        //Get LSB and MSB.
        var msb = (pitch & 0x3F80) >> 7;
        var lsb = pitch & 0x7F;
        return new Tuple<int, int>(msb, lsb);
    }

    /// <summary>
    ///     Scale to an MIDI value.
    /// </summary>
    /// <param name="val">Value to scale.</param>
    /// <returns>Scaled value.</returns>
    public static Tuple<int, int> Scale2Midi(int val)
    {
        var v = (ushort)(val / 127d * 0x3FFF);
        var msb = (v & 0x3F80) >> 7;
        var lsb = v & 0x7F;
        return new Tuple<int, int>(msb, lsb);
    }

    /// <summary>
    ///     Convert MIDI to sequence ticks.
    /// </summary>
    /// <param name="midiTicks">Ticks in MIDI.</param>
    /// <param name="division">MIDI division value.</param>
    /// <param name="timeBase">Sequence time base.</param>
    /// <returns>Sequence ticks.</returns>
    public static int Midi2SequenceTicks(int midiTicks, int division, int timeBase = 48)
    {
        return (int)(midiTicks / (double)division * timeBase);
    }

    /// <summary>
    ///     Convert sequence to MIDI ticks.
    /// </summary>
    /// <param name="sequenceTicks">Sequence ticks.</param>
    /// <param name="division">Division.</param>
    /// <param name="timeBase">Time base.</param>
    /// <returns>Ticks in MIDI.</returns>
    public static int Sequence2MidiTicks(int sequenceTicks, int division, int timeBase = 48)
    {
        return (int)(sequenceTicks * division / (double)timeBase);
    }

    /// <summary>
    ///     MIDI to pitch.
    /// </summary>
    /// <param name="msb">MSB.</param>
    /// <param name="lsb">LSB.</param>
    /// <returns>Pitch value.</returns>
    public static double Midi2PitchBend(int msb, int lsb)
    {
        var val = (ushort)(msb << 7);
        val |= (ushort)(lsb & 0x7F);
        if (val > 0x3FFF) val = 0x3FFF;
        return (val - 0x2000) / (double)0x2000;
    }
}