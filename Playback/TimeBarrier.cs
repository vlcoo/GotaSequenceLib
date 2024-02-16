﻿using System.Diagnostics;
using System.Threading;

namespace GotaSequenceLib.Playback;

// Credit to ipatix
public class TimeBarrier
{
    private readonly Stopwatch _sw;
    private readonly double _timerInterval;
    private readonly double _waitInterval;
    private double _lastTimeStamp;
    private bool _started;

    public TimeBarrier(double framesPerSecond)
    {
        _waitInterval = 1.0 / framesPerSecond;
        _started = false;
        _sw = new Stopwatch();
        _timerInterval = 1.0 / Stopwatch.Frequency;
    }

    public void Wait()
    {
        if (!_started) return;
        var totalElapsed = _sw.ElapsedTicks * _timerInterval;
        var desiredTimeStamp = _lastTimeStamp + _waitInterval;
        var timeToWait = desiredTimeStamp - totalElapsed;
        if (timeToWait < 0) timeToWait = 0;
        Thread.Sleep((int)(timeToWait * 1000));
        _lastTimeStamp = desiredTimeStamp;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _lastTimeStamp = 0;
        _sw.Restart();
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _sw.Stop();
    }
}