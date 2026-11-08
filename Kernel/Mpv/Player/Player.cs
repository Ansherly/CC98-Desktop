// Copyright (c) Bili Copilot. All rights reserved.

using Mpv.Core.Args;
using Mpv.Core.Interop;
using Mpv.Core.Structs.Render;
using Mpv.Core.Structs.RenderGL;
using System.Runtime.InteropServices;
using System.Threading;

namespace Mpv.Core;

public sealed partial class Player
{
    public Player()
    {
        Client = new MpvClientNative();
        Dependencies = new Structs.Player.LibMpvDependencies();
    }

    public async Task DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Client.UnObserveProperties();
        RenderContext?.Destroy();
        await Client.DestroyAsync();
    }

    public async Task TerminateAsync()
    {
        _isDisposed = true;
        await Client.DestroyAsync(true);
    }

    public async Task InitializeAsync(InitializeArgument? argument = null)
    {
        if (Client.IsInitialized)
        {
            return;
        }

        AutoPlay = argument?.AutoPlay ?? true;
        await Client.InitializeAsync();
        RerunEventLoop();
        if (argument?.ConfigFile is not null)
        {
            await Client.LoadConfigAsync(argument.ConfigFile);
        }

        if (argument?.OpenGLGetProcAddress is not null)
        {
            var glParams = new MpvOpenGLInitParams
            {
                GetProcAddrFn = (ctx, name) =>
                {
                    return argument!.OpenGLGetProcAddress!(name);
                },

                GetProcAddressCtx = IntPtr.Zero
            };

            var glParamsPtr = Marshal.AllocHGlobal(Marshal.SizeOf(glParams));
            Marshal.StructureToPtr(glParams, glParamsPtr, false);
            var glStringPtr = Marshal.StringToCoTaskMemUTF8("opengl");
            RenderContext = new MpvRenderContextNative(
                Client.Handle,
                [
                    new MpvRenderParam { Type = Enums.Render.MpvRenderParamType.ApiType, Data = glStringPtr },
                    new MpvRenderParam { Type = Enums.Render.MpvRenderParamType.OpenGLInitParams, Data = glParamsPtr },
                    new MpvRenderParam { Type = Enums.Render.MpvRenderParamType.Invalid, Data = IntPtr.Zero },
                ]);

            Marshal.FreeHGlobal(glParamsPtr);
            Marshal.FreeCoTaskMem(glStringPtr);
        }
    }

    public void RenderGL(int width, int height, int fboInt)
    {
        var fbo = new MpvOpenGLFBO
        {
            Fbo = fboInt,
            W = width,
            H = height
        };

        var fboPtr = Marshal.AllocHGlobal(Marshal.SizeOf(fbo));
        Marshal.StructureToPtr(fbo, fboPtr, false);

        // 注意:AllocHGlobal(0) 不会真的分配内存,写进去就是踩堆,必须按 int 的大小申请
        var flipYPtr = Marshal.AllocHGlobal(sizeof(int));
        Marshal.WriteInt32(flipYPtr, 0);
        RenderContext!.Render([
            new MpvRenderParam {Type=Enums.Render.MpvRenderParamType.Fbo, Data = fboPtr },
            new MpvRenderParam {Type=Enums.Render.MpvRenderParamType.FlipY, Data = flipYPtr },
            new MpvRenderParam {Type=Enums.Render.MpvRenderParamType.Invalid, Data = IntPtr.Zero },
            ]);

        Marshal.FreeHGlobal(fboPtr);
        Marshal.FreeHGlobal(flipYPtr);
    }

    public void RerunEventLoop()
    {
        if (_eventLoopCancellationTokenSource != null)
        {
            _eventLoopCancellationTokenSource.Cancel();
            _eventLoopCancellationTokenSource.Dispose();
        }

        Client.UnObserveProperties();
        _eventLoopCancellationTokenSource = new CancellationTokenSource();
        _eventLoopTask = Task.Run(EventLoop, _eventLoopCancellationTokenSource.Token);
        Client.ObserveProperty(PauseProperty, Enums.Client.MpvFormat.Flag);
        Client.ObserveProperty(DurationProperty, Enums.Client.MpvFormat.Int64);
        Client.ObserveProperty(PositionProperty, Enums.Client.MpvFormat.Int64);
        Client.ObserveProperty(PausedForCacheProperty, Enums.Client.MpvFormat.Flag);
    }

    public bool IsMediaLoaded()
        => _isLoaded && !_isDisposed;

    public async Task ExecuteAfterMediaLoadedAsync(string command)
    {
        if (IsMediaLoaded())
        {
            await Client.ExecuteAsync(command);
        }
    }

    public void Play()
    {
        if (IsMediaLoaded())
        {
            Client.SetProperty(PauseProperty, false);
        }
    }

    public void Pause()
    {
        if (IsMediaLoaded())
        {
            Client.SetProperty(PauseProperty, true);
        }
    }

    public void Seek(TimeSpan ts)
    {
        var pos = ts.TotalSeconds;
        if (pos >= 0 && pos <= _currentDuration)
        {
            // Seek to the position.
            Client.SetProperty(PositionProperty, pos);
        }
        else if (pos > _currentDuration && _currentDuration > 0)
        {
            Client.SetProperty(PositionProperty, Math.Max(0, _currentDuration - 1));
        }
        else if (pos < 0)
        {
            Client.SetProperty(PositionProperty, 0);
        }
    }

    public void SetSpeed(double rate)
    {
        if (IsMediaLoaded())
        {
            Client.SetProperty(SpeedProperty, rate);
        }
    }

    public void SetVolume(int volume)
    {
        if (IsMediaLoaded())
        {
            Client.SetProperty(VolumeProperty, volume);
        }
    }

    public void ResetDuration()
    {
        if (IsMediaLoaded())
        {
            _currentDuration = Client.GetPropertyToLong(DurationProperty);
        }
    }

    public bool IsPaused()
        => Client.GetPropertyToBoolean(PauseProperty);

    public async Task TakeScreenshotAsync(string filePath)
    {
        if (IsMediaLoaded())
        {
            await Client.ExecuteAsync($"screenshot-to-file {filePath}");
        }
    }
}
