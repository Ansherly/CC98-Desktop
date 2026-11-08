// Copyright (c) Bili Copilot. All rights reserved.

using Mpv.Core.Args;
using Mpv.Core.Enums.Client;
using Mpv.Core.Enums.Player;
using Mpv.Core.Structs.Client;
using System.Runtime.InteropServices;

namespace Mpv.Core;

public sealed partial class Player
{
    private void EventLoop()
    {
        while (!_isDisposed)
        {
            var clientEvent = Client.WaitEvent();
            switch (clientEvent.EventId)
            {
                case MpvEventId.Shutdown:
                    _ = DisposeAsync();
                    Destroyed?.Invoke(this, EventArgs.Empty);
                    return;
                case MpvEventId.LogMessage:
                    {
                        if (!IsLoggingEnabled)
                        {
                            break;
                        }

                        var logMessage = clientEvent.GetData<MpvEventLogMessage>();
                        var args = new LogMessageReceivedEventArgs(logMessage.Prefix, logMessage.Text, logMessage.Level.ToLogLevel());
                        TranslateLogMessage(logMessage);
                        LogMessageReceived?.Invoke(this, args);
                    }
                    break;
                case MpvEventId.StartFile:
                    {
                        // var startFileData = clientEvent.GetData<MpvEventStartFile>();
                        ChangeState(PlaybackState.Opening);
                    }
                    break;
                case MpvEventId.FileLoaded:
                    _isLoaded = true;
                    ChangeState(PlaybackState.Decoding);
                    break;
                case MpvEventId.EndFile:
                    {
                        _isLoaded = false;
                        var endFileData = clientEvent.GetData<MpvEventEndFile>();
                        if (endFileData.Reason == MpvEndFileReason.Error)
                        {
                            ChangeState(PlaybackState.Failed);
                        }
                        else
                        {
                            ChangeState(PlaybackState.None);
                            RaiseEnd(endFileData);
                        }
                    }
                    break;
                case MpvEventId.PlaybackRestart:
                    {
                        ChangeState(PlaybackState.Playing);
                    }
                    break;
                case MpvEventId.PropertyChange:
                    {
                        var propData = clientEvent.GetData<MpvEventProperty>();
                        TranslateProperty(propData);
                    }
                    break;
            }
        }
    }

    private void TranslateLogMessage(MpvEventLogMessage logMessage)
    {
        if (logMessage.Prefix == "cplayer")
        {
            var text = logMessage.Text.Trim();
            if (text.StartsWith("built on"))
            {
                var t = DateTimeOffset.Parse(text.Replace("built on", string.Empty).Trim());
                Dependencies.BuildTime = t;
            }
            else if (text.StartsWith("FFmpeg version"))
            {
                Dependencies.FFmpegVersion = text.Replace("FFmpeg version:", string.Empty).Trim();
            }
            else if (text.StartsWith("libplacebo version"))
            {
                Dependencies.LibplaceboVersion = text.Replace("libplacebo version:", string.Empty).Trim();
            }
            else if (text.StartsWith("libavutil"))
            {
                Dependencies.LibavutilVersion = text.Replace("libavutil", string.Empty).Trim();
            }
            else if (text.StartsWith("libavcodec"))
            {
                Dependencies.LibavcodecVersion = text.Replace("libavcodec", string.Empty).Trim();
            }
            else if (text.StartsWith("libavformat"))
            {
                Dependencies.LibavformatVersion = text.Replace("libavformat", string.Empty).Trim();
            }
            else if (text.StartsWith("libavfilter"))
            {
                Dependencies.LibavfilterVersion = text.Replace("libavfilter", string.Empty).Trim();
            }
            else if (text.StartsWith("libswscale"))
            {
                Dependencies.LibswscaleVersion = text.Replace("libswscale", string.Empty).Trim();
            }
            else if (text.StartsWith("libswresample"))
            {
                Dependencies.LibswresampleVersion = text.Replace("libswresample", string.Empty).Trim();
            }
        }
    }

    private void TranslateProperty(MpvEventProperty property)
    {
        if (property.DataPtr == IntPtr.Zero)
        {
            return;
        }

        if (property.Name == PauseProperty)
        {
            var isPaused = Marshal.ReadInt32(property.DataPtr) == 1;
            ChangeState(isPaused ? PlaybackState.Paused : PlaybackState.Playing);
        }
        else if (property.Name == PositionProperty)
        {
            _currentPosition = Marshal.ReadInt64(property.DataPtr);
            RaisePositionChanged();
        }
        else if (property.Name == DurationProperty)
        {
            var duration = Marshal.ReadInt64(property.DataPtr);
            _currentDuration = duration;
            RaisePositionChanged();
        }
        else if (property.Name == PausedForCacheProperty)
        {
            var isPaused = Marshal.ReadInt32(property.DataPtr) == 1;
            if (isPaused)
            {
                ChangeState(PlaybackState.Buffering);
            }
            else
            {
                ChangeState(PlaybackState.Playing);
            }
        }
    }

    private void ChangeState(PlaybackState state)
    {
        if (state == PlaybackState)
        {
            return;
        }

        var args = new PlaybackStateChangedEventArgs(PlaybackState, state);
        PlaybackState = state;
        PlaybackStateChanged?.Invoke(this, args);
    }

    private void RaiseEnd(MpvEventEndFile e)
    {
        if (e.Reason == MpvEndFileReason.Quit || e.Reason == MpvEndFileReason.Redirect)
        {
            // Do not raise the event if the player shutdown or redirected.
            return;
        }

        var isInterrupted = e.Reason == MpvEndFileReason.Error;
        var errorMessage = isInterrupted ? $"Playback stopped due to an error. {e.Error}" : string.Empty;
        var args = new PlaybackStoppedEventArgs(isInterrupted, errorMessage);
        PlaybackStopped?.Invoke(this, args);
    }

    private void RaisePositionChanged()
    {
        if (_currentDuration > 0 && _currentPosition >= 0)
        {
            var args = new PlaybackPositionChangedEventArgs(_currentDuration, _currentPosition);
            PlaybackPositionChanged?.Invoke(this, args);
        }
    }
}
