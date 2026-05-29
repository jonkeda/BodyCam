using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Android.Views;
using BodyCam.Services.Camera.A9.Vue990;
using Com.Veepai;
using Com.Vstarcam;
using Com.Vstarcam.App_p2p_api;
using JFile = Java.IO.File;

namespace BodyCam.A9PhoneProbe;

internal sealed class Vue990PpcsSession : Java.Lang.Object,
    IClientStateListener,
    IClientCommandListener,
    IClientReleaseListener,
    AppPlayerApi.IAppPlayerProgress
{
    private const string LogTag = "A9PPCS";
    private const int LiveSource = 1;
    private const string DefaultManagedLiveCgiMode = "d1-get-slash";
    private static readonly TimeSpan MainThreadCallTimeout = TimeSpan.FromSeconds(12);

    private readonly StringBuilder _log = new();
    private readonly Handler _mainHandler = new(Looper.MainLooper!);
    private int _progressEvents;
    private int _headEvents;
    private int _gpsEvents;
    private int _drawEvents;
    private int _lastDrawWidth;
    private int _lastDrawHeight;
    private int _lastFrameRate;

    public string Run(
        string clientId,
        string vuid,
        string serverParam,
        string user,
        string password,
        string cacheDir,
        string filesDir,
        bool captureImage,
        bool captureVideo,
        bool nativeChannelOracle = false,
        bool managedLiveCgi = false,
        string? managedLiveCgiMode = null)
    {
        long clientPtr = 0;
        var liveCgiMode = NormalizeManagedLiveCgiMode(managedLiveCgi, managedLiveCgiMode);
        Line("C# PPCS session started.");
        Line(
            $"clientId={Safe(clientId)} vuid={Safe(vuid)} serverLen={LengthOf(serverParam)} " +
            $"captureImage={captureImage} captureVideo={captureVideo} " +
            $"nativeChannelOracle={nativeChannelOracle} managedLiveCgi={managedLiveCgi} " +
            $"managedLiveCgiMode={liveCgiMode ?? "<native-writeCgi>"}");

        Load("c++_shared");
        Load("vp_log");
        Load("yuv");
        Load("OKSMARTSHENGYIN");
        Load("OKSMARTJIAMI");
        Load("OKSMARTPPCS");
        TryEnableNativeHlp2pLogging();
        Load("OKSMARTPLAY");

        try
        {
            var init = OnMain(() => JNIApi.Init(this, this, this));
            Line($"JNIApi.init={init}");

            clientPtr = OnMain(() => JNIApi.Create(clientId, null));
            Line($"JNIApi.create={clientPtr}");
            if (clientPtr == 0)
                return GetLog();

            if (!string.IsNullOrWhiteSpace(vuid))
            {
                Retain(clientPtr, "clientSetVuid");
                var setVuid = JNIApi.ClientSetVuid(clientPtr, vuid);
                Line($"JNIApi.clientSetVuid={setVuid}");
            }

            Retain(clientPtr, "connect");
            var connect = JNIApi.Connect(clientPtr, 0x3F, serverParam, 1);
            Line($"JNIApi.connect={connect} connectType=0x3F p2pType=1");
            LogSocketSnapshot("after-connect");
            Sleep(1200);

            Retain(clientPtr, "login");
            var login = JNIApi.Login(clientPtr, user, password);
            Line($"JNIApi.login={login}");
            LogSocketSnapshot("after-login");

            CheckMode(clientPtr);
            TryLiveCgiOpen(clientPtr, liveCgiMode, user, password);
            LogSocketSnapshot("after-live-cgi");
            if (nativeChannelOracle)
                RunNativeChannelOracle(clientPtr, filesDir);

            if (captureImage || captureVideo || !nativeChannelOracle)
            {
                RunPlayerProbe(clientPtr, cacheDir, filesDir, captureImage, captureVideo);
            }
            else
            {
                Line("AppPlayerApi skipped because nativeChannelOracle=true and capture=false");
            }

            for (var i = 0; i < 8; i++)
            {
                CheckBuffer(clientPtr, 1, i);
                Sleep(500);
            }
        }
        catch (Exception ex)
        {
            Line($"PPCS exception={Describe(ex)}");
        }
        finally
        {
            if (clientPtr != 0)
            {
                try
                {
                    Retain(clientPtr, "disconnect");
                    Line($"JNIApi.disconnect={JNIApi.Disconnect(clientPtr)}");
                }
                catch (Exception ex)
                {
                    Line($"disconnect exception={Describe(ex)}");
                }

                try
                {
                    var ptrToDestroy = clientPtr;
                    OnMain(() =>
                    {
                        JNIApi.Destroy(ptrToDestroy);
                        return true;
                    });
                    Line("JNIApi.destroy=done");
                }
                catch (Exception ex)
                {
                    Line($"destroy exception={Describe(ex)}");
                }
            }
        }

        return GetLog();
    }

    public void StateListener(long p0, int p1)
    {
        try
        {
            Line($"stateListener clientPtr={p0} state={p1}");
        }
        catch
        {
        }
    }

    public void CommandListener(long p0, byte[]? p1, int p2)
    {
        try
        {
            Line(
                $"commandListener clientPtr={p0} type={p2} len={LengthOf(p1)} " +
                $"prefix={Hex(p1, 24)} ascii={AsciiPreview(p1, 160)}");
        }
        catch
        {
        }
    }

    public void ReleaseListener(long p0)
    {
        try
        {
            Line($"releaseListener clientPtr={p0}");
            _mainHandler.Post(() =>
            {
                try
                {
                    JNIApi.Release(p0);
                    Line("releaseListener release=done");
                }
                catch (Exception ex)
                {
                    Line($"releaseListener exception={Describe(ex)}");
                }
            });
        }
        catch
        {
        }
    }

    public void App_player_draw_info(long p0, int p1, int p2, int p3, float p4, float p5, float p6, float p7)
    {
        try
        {
            _drawEvents++;
            _lastDrawWidth = p1;
            _lastDrawHeight = p2;
            if (_drawEvents <= 12 || _drawEvents % 30 == 0)
                Line($"app_player_draw_info textureId={p0} width={p1} height={p2} drawType={p3}");
        }
        catch
        {
        }
    }

    public void App_player_gps_info(long p0, int p1, int p2, float p3, float p4, float p5, float p6)
    {
        try
        {
            _gpsEvents++;
            if (_gpsEvents <= 12 || _gpsEvents % 30 == 0)
                Line($"app_player_gps_info textureId={p0} type={p1} value={p2}");
        }
        catch
        {
        }
    }

    public void App_player_head_info(long p0, int p1, int p2, int p3)
    {
        try
        {
            _headEvents++;
            if (_headEvents <= 24 || _headEvents % 30 == 0)
                Line($"app_player_head_info textureId={p0} type={p1} width={p2} height={p3}");
        }
        catch
        {
        }
    }

    public void App_player_progress(long p0, int p1, int p2, int p3, int p4, int p5, int p6, int p7, long p8, long p9)
    {
        try
        {
            _progressEvents++;
            if (p7 > 0)
                _lastFrameRate = p7;

            if (_progressEvents <= 24 || _progressEvents % 30 == 0)
            {
                Line(
                    $"app_player_progress textureId={p0} status={p1} current={p2} total={p3} width={p4} height={p5} " +
                    $"videoType={p6} frameRate={p7} position={p8} duration={p9}");
            }
        }
        catch
        {
        }
    }

    private void TryLiveCgiOpen(long clientPtr, string? managedLiveCgiMode, string user, string password)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(managedLiveCgiMode))
            {
                if (TryWriteNativeCommandCgi(clientPtr, managedLiveCgiMode, user, password))
                {
                    Sleep(700);
                    CheckBuffer(clientPtr, 1, 90);
                    return;
                }

                var frame = BuildManagedLiveCgiPayload(managedLiveCgiMode);
                Retain(clientPtr, $"write managed live CGI mode {managedLiveCgiMode} channel 1");
                var result = JNIApi.Write(clientPtr, 1, frame, frame.Length);
                Line(
                    $"JNIApi.write managedLiveCgi mode={managedLiveCgiMode} channel=1 bytes={frame.Length} " +
                    $"result={result} prefix={Hex(frame, 32)} ascii={AsciiPreview(frame, 96)}");
            }
            else
            {
                var cgi = A9Vue990CgiCommandBuilder.LiveStreamCgi;
                Retain(clientPtr, "writeCgi live channel 1");
                var result = JNIApi.WriteCgi(clientPtr, cgi, 1);
                Line($"JNIApi.writeCgi live channel=1 cgi={cgi} result={result}");
            }

            Sleep(700);
            CheckBuffer(clientPtr, 1, 90);
        }
        catch (Exception ex)
        {
            Line($"writeCgi live exception={Describe(ex)}");
        }
    }

    private bool TryWriteNativeCommandCgi(long clientPtr, string managedLiveCgiMode, string user, string password)
    {
        var mode = managedLiveCgiMode.Trim().ToLowerInvariant();
        if (mode is not ("command-cgi-split" or "command-cgi-combined"))
            return false;

        var body = A9Vue990CgiCommandBuilder.BuildNativeLiveStreamCgiCommandBody(user, password);
        var header = A9Vue990CgiCommandBuilder.BuildNativeCgiCommandHeader(body.Length);
        Line(
            $"managedLiveCgi nativeCommand mode={mode} header={Hex(header, header.Length)} " +
            $"bodyBytes={body.Length} bodyAscii={AsciiPreview(body, 160)}");

        if (mode == "command-cgi-combined")
        {
            var combined = new byte[header.Length + body.Length];
            Buffer.BlockCopy(header, 0, combined, 0, header.Length);
            Buffer.BlockCopy(body, 0, combined, header.Length, body.Length);

            Retain(clientPtr, $"write managed live CGI mode {mode} channel 0 combined");
            var combinedResult = JNIApi.Write(clientPtr, 0, combined, 5000);
            Line(
                $"JNIApi.write managedLiveCgi mode={mode} channel=0 bytes={combined.Length} " +
                $"retryBudget=5000 result={combinedResult} prefix={Hex(combined, 48)} ascii={AsciiPreview(combined, 160)}");
            return true;
        }

        Retain(clientPtr, $"write managed live CGI mode {mode} channel 0 header");
        var headerResult = JNIApi.Write(clientPtr, 0, header, 5000);
        Line(
            $"JNIApi.write managedLiveCgi mode={mode} part=header channel=0 bytes={header.Length} " +
            $"retryBudget=5000 result={headerResult} prefix={Hex(header, 16)} ascii={AsciiPreview(header, 16)}");

        Retain(clientPtr, $"write managed live CGI mode {mode} channel 0 body");
        var bodyResult = JNIApi.Write(clientPtr, 0, body, 5000);
        Line(
            $"JNIApi.write managedLiveCgi mode={mode} part=body channel=0 bytes={body.Length} " +
            $"retryBudget=5000 result={bodyResult} prefix={Hex(body, 48)} ascii={AsciiPreview(body, 160)}");
        return true;
    }

    private static string? NormalizeManagedLiveCgiMode(bool managedLiveCgi, string? managedLiveCgiMode)
    {
        if (!string.IsNullOrWhiteSpace(managedLiveCgiMode))
            return managedLiveCgiMode.Trim().ToLowerInvariant();

        return managedLiveCgi ? DefaultManagedLiveCgiMode : null;
    }

    private void TryEnableNativeHlp2pLogging()
    {
        try
        {
            var result = Hlp2pSetLogLevel(0x1f, 0xff, 1);
            Line($"HLP2P_SetLogLevel flags=0x1f level=0xff enabled=1 result={result}");
        }
        catch (Exception ex)
        {
            Line($"HLP2P_SetLogLevel exception={Describe(ex)}");
        }
    }

    private static byte[] BuildManagedLiveCgiPayload(string mode)
    {
        return mode.Trim().ToLowerInvariant() switch
        {
            "d1-get-slash" => A9Vue990CgiCommandBuilder.BuildGetRequest(
                A9Vue990CgiCommandBuilder.LiveStreamCgi,
                sequence: 1,
                leadingSlash: true),
            "d1-get-noslash" => A9Vue990CgiCommandBuilder.BuildGetRequest(
                A9Vue990CgiCommandBuilder.LiveStreamCgi,
                sequence: 1,
                leadingSlash: false),
            "raw-cgi" => A9Vue990CgiCommandBuilder.BuildRawCgiPathPayload(
                A9Vue990CgiCommandBuilder.LiveStreamCgi),
            "raw-cgi-null" => A9Vue990CgiCommandBuilder.BuildRawCgiPathPayload(
                A9Vue990CgiCommandBuilder.LiveStreamCgi,
                nullTerminated: true),
            "raw-get-slash" => A9Vue990CgiCommandBuilder.BuildHttpGetPayload(
                A9Vue990CgiCommandBuilder.LiveStreamCgi,
                leadingSlash: true),
            "raw-get-noslash" => A9Vue990CgiCommandBuilder.BuildHttpGetPayload(
                A9Vue990CgiCommandBuilder.LiveStreamCgi,
                leadingSlash: false),
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "Managed live CGI mode must be one of: d1-get-slash, d1-get-noslash, raw-cgi, raw-cgi-null, raw-get-slash, raw-get-noslash, command-cgi-split, command-cgi-combined."),
        };
    }

    private void RunPlayerProbe(long clientPtr, string cacheDir, string filesDir, bool captureImage, bool captureVideo)
    {
        Line("Player metadata probe:");
        SurfaceTexture? texture = null;
        Surface? surface = null;
        long playerPtr = 0;
        var captureAttempted = false;
        var videoAttempted = false;
        var videoStarted = false;
        var videoStopped = false;
        var videoStartWait = -1;
        string? videoPath = null;

        try
        {
            var cache = new JFile(string.IsNullOrWhiteSpace(cacheDir) ? "/data/local/tmp" : cacheDir);
            OnMain(() =>
            {
                AppPlayerApi.Init(cache);
                AppPlayerApi.SetProgressCallback(this);
                return true;
            });
            Line($"AppPlayerApi.init/setProgressCallback=done cache={cache.AbsolutePath}");

            texture = new SurfaceTexture(0);
            texture.SetDefaultBufferSize(640, 480);
            surface = new Surface(texture);

            var surfaceForCreate = surface;
            playerPtr = OnMain(() => AppPlayerApi.CreatePlayer(990001L, surfaceForCreate, 640, 480, 0, 0));
            Line($"AppPlayerApi.createPlayer={playerPtr}");
            if (playerPtr == 0)
                return;

            var sourceAlreadyOpen = AppPlayerApi.CheckPlayerSource(playerPtr, LiveSource, null, null, clientPtr, null);
            Line($"AppPlayerApi.checkPlayerSource live={sourceAlreadyOpen}");

            var setSource = AppPlayerApi.SetPlayerSource(playerPtr, LiveSource, null, null, clientPtr, null);
            Line($"AppPlayerApi.setPlayerSource live clientPtr={clientPtr} result={setSource}");

            var start = AppPlayerApi.Start(playerPtr);
            Line($"AppPlayerApi.start={start}");

            for (var i = 0; i < 8; i++)
            {
                Sleep(1000);
                Line($"player wait[{i}] callbacks progress={_progressEvents} head={_headEvents} draw={_drawEvents} gps={_gpsEvents}");
                if (i is 0 or 2 or 5)
                    LogSocketSnapshot($"player-wait-{i}");

                if (captureImage && !captureAttempted && _lastDrawWidth > 0 && _lastDrawHeight > 0)
                {
                    captureAttempted = true;
                    TryCaptureStill(playerPtr, filesDir, _lastDrawWidth, _lastDrawHeight);
                }

                if (captureVideo && !videoAttempted && _lastDrawWidth > 0 && _lastDrawHeight > 0)
                {
                    videoAttempted = true;
                    videoPath = TryStartVideoDownload(playerPtr, filesDir);
                    videoStarted = !string.IsNullOrWhiteSpace(videoPath);
                    if (videoStarted)
                    {
                        videoStartWait = i;
                    }
                    else
                    {
                        Line("captureVideo startDown fallback=mjpeg-avi-screenshot-sequence");
                        TryCaptureMjpegAvi(playerPtr, filesDir, _lastDrawWidth, _lastDrawHeight);
                    }
                }

                if (captureVideo && videoStarted && !videoStopped && i - videoStartWait >= 3)
                {
                    videoStopped = true;
                    TryStopVideoDownload(playerPtr, videoPath!, _lastDrawWidth, _lastDrawHeight);
                }

                if (i == 2 || i == 5)
                    CheckBuffer(clientPtr, 1, 100 + i);
            }

            if (captureImage && !captureAttempted)
                Line("captureImage skipped=no draw metadata before player stop");
            if (captureVideo && !videoAttempted)
                Line("captureVideo skipped=no draw metadata before player stop");
        }
        catch (Exception ex)
        {
            Line($"Player probe exception={Describe(ex)}");
        }
        finally
        {
            if (playerPtr != 0 && videoStarted && !videoStopped && !string.IsNullOrWhiteSpace(videoPath))
            {
                try
                {
                    TryStopVideoDownload(playerPtr, videoPath, _lastDrawWidth, _lastDrawHeight);
                }
                catch (Exception ex)
                {
                    Line($"captureVideo final stop exception={Describe(ex)}");
                }
            }

            if (playerPtr != 0)
            {
                try
                {
                    Line($"AppPlayerApi.stop={AppPlayerApi.Stop(playerPtr)}");
                }
                catch (Exception ex)
                {
                    Line($"AppPlayerApi.stop exception={Describe(ex)}");
                }

                try
                {
                    Line($"AppPlayerApi.destroy={AppPlayerApi.Destroy(playerPtr)}");
                }
                catch (Exception ex)
                {
                    Line($"AppPlayerApi.destroy exception={Describe(ex)}");
                }
            }

            surface?.Release();
            texture?.Release();
        }
    }

    private string? TryStartVideoDownload(long playerPtr, string filesDir)
    {
        try
        {
            var baseDir = string.IsNullOrWhiteSpace(filesDir) ? "/data/local/tmp" : filesDir;
            var captureDir = System.IO.Path.Combine(baseDir, "captures", "phase-16");
            Directory.CreateDirectory(captureDir);
            var file = System.IO.Path.Combine(captureDir, $"a9-video-{DateTimeOffset.Now:yyyy-MM-dd-HHmmss}.ts");

            var result = OnMain(() => AppPlayerApi.StartDown(playerPtr, file));
            Line($"captureVideo startDown path={file} result={result}");
            return result ? file : null;
        }
        catch (Exception ex)
        {
            Line($"captureVideo startDown exception={Describe(ex)}");
            return null;
        }
    }

    private void TryStopVideoDownload(long playerPtr, string path, int width, int height)
    {
        try
        {
            var result = OnMain(() => AppPlayerApi.StopDown(playerPtr));
            Line($"captureVideo stopDown result={result}");
        }
        catch (Exception ex)
        {
            Line($"captureVideo stopDown exception={Describe(ex)}");
        }

        VerifyVideoFile("captureVideo raw", path);
        TryConvertVideo(path, width, height, _lastFrameRate > 0 ? _lastFrameRate : 25);
    }

    private void TryConvertVideo(string tsPath, int width, int height, int frameRate)
    {
        var mp4Path = System.IO.Path.ChangeExtension(tsPath, ".mp4");

        try
        {
            var result = OnMain(() => AppPlayerApi.TsToMP4(tsPath, mp4Path));
            Line($"captureVideo tsToMP4 input={tsPath} output={mp4Path} result={result}");
            if (VerifyVideoFile("captureVideo mp4", mp4Path))
                return;
        }
        catch (Exception ex)
        {
            Line($"captureVideo tsToMP4 exception={Describe(ex)}");
        }

        try
        {
            var result = OnMain(() => AppPlayerApi.SaveMP4(tsPath, mp4Path, width, height, frameRate));
            Line($"captureVideo saveMP4 input={tsPath} output={mp4Path} width={width} height={height} frameRate={frameRate} result={result}");
            VerifyVideoFile("captureVideo mp4", mp4Path);
        }
        catch (Exception ex)
        {
            Line($"captureVideo saveMP4 exception={Describe(ex)}");
        }
    }

    private void TryCaptureMjpegAvi(long playerPtr, string filesDir, int width, int height)
    {
        const int targetFrames = 6;
        const int framesPerSecond = 2;

        try
        {
            var baseDir = string.IsNullOrWhiteSpace(filesDir) ? "/data/local/tmp" : filesDir;
            var captureDir = System.IO.Path.Combine(baseDir, "captures", "phase-16");
            Directory.CreateDirectory(captureDir);

            var baseName = $"a9-video-{DateTimeOffset.Now:yyyy-MM-dd-HHmmss}-mjpeg";
            var frameDir = System.IO.Path.Combine(captureDir, $"{baseName}-frames");
            Directory.CreateDirectory(frameDir);

            var frames = new List<CapturedFrame>(targetFrames);
            for (var i = 0; i < targetFrames; i++)
            {
                if (i > 0)
                    Sleep(1000 / framesPerSecond);

                var framePath = System.IO.Path.Combine(frameDir, $"frame-{i:000}.jpg");
                var result = OnMain(() => AppPlayerApi.Screenshot(playerPtr, framePath, width, height, (float)width, (float)height, 0));
                Line($"captureVideo frame[{i}] screenshot path={framePath} width={width} height={height} result={result}");

                if (TryReadVerifiedFrame($"captureVideo frame[{i}]", framePath, out var bytes))
                    frames.Add(new CapturedFrame(framePath, bytes));
            }

            if (frames.Count == 0)
            {
                Line("captureVideo frameSequence skipped=no verified frames");
                return;
            }

            var manifestPath = System.IO.Path.Combine(captureDir, $"{baseName}-frames.txt");
            WriteFrameSequenceManifest(manifestPath, frames, width, height, framesPerSecond);
            Line($"captureVideo frameSequence manifest={manifestPath} frames={frames.Count} fps={framesPerSecond} width={width} height={height}");
            Line("captureVideo mjpegAvi skipped=assemble-on-windows-csharp");
        }
        catch (Exception ex)
        {
            Line($"captureVideo frameSequence exception={Describe(ex)}");
        }
    }

    private void WriteFrameSequenceManifest(
        string path,
        IReadOnlyList<CapturedFrame> frames,
        int width,
        int height,
        int framesPerSecond)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? ".");

        var lines = new List<string>
        {
            $"frames={frames.Count}",
            $"fps={framesPerSecond}",
            $"width={width}",
            $"height={height}",
        };

        using var sha = SHA256.Create();
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            var hash = Convert.ToHexString(sha.ComputeHash(frame.Bytes));
            lines.Add($"frame[{i}]={frame.Path} bytes={frame.Bytes.Length} sha256={hash}");
        }

        File.WriteAllLines(path, lines);
    }

    private void TryCaptureStill(long playerPtr, string filesDir, int width, int height)
    {
        try
        {
            var baseDir = string.IsNullOrWhiteSpace(filesDir) ? "/data/local/tmp" : filesDir;
            var captureDir = System.IO.Path.Combine(baseDir, "captures", "phase-16");
            Directory.CreateDirectory(captureDir);
            var file = System.IO.Path.Combine(captureDir, $"a9-capture-{DateTimeOffset.Now:yyyy-MM-dd-HHmmss}.jpg");

            var result = OnMain(() => AppPlayerApi.Screenshot(playerPtr, file, width, height, (float)width, (float)height, 0));
            Line($"AppPlayerApi.screenshot path={file} width={width} height={height} result={result}");
            VerifyCapture(file);
        }
        catch (Exception ex)
        {
            Line($"captureImage exception={Describe(ex)}");
        }
    }

    private bool TryReadVerifiedFrame(string label, string path, out byte[] bytes)
    {
        bytes = [];

        try
        {
            if (!File.Exists(path))
            {
                Line($"{label} exists=false path={path}");
                return false;
            }

            bytes = File.ReadAllBytes(path);
            using var sha = SHA256.Create();
            var hash = Convert.ToHexString(sha.ComputeHash(bytes));
            var options = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(path, options);
            Line($"{label} exists=true path={path} bytes={bytes.Length} dimensions={options.OutWidth}x{options.OutHeight} sha256={hash}");
            return bytes.Length > 0 && options.OutWidth > 0 && options.OutHeight > 0;
        }
        catch (Exception ex)
        {
            Line($"{label} verify exception={Describe(ex)}");
            return false;
        }
    }

    private void VerifyCapture(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Line($"captureImage exists=false path={path}");
                return;
            }

            var bytes = File.ReadAllBytes(path);
            using var sha = SHA256.Create();
            var hash = Convert.ToHexString(sha.ComputeHash(bytes));
            var options = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(path, options);
            Line($"captureImage exists=true path={path} bytes={bytes.Length} dimensions={options.OutWidth}x{options.OutHeight} sha256={hash}");
        }
        catch (Exception ex)
        {
            Line($"captureImage verify exception={Describe(ex)}");
        }
    }

    private bool VerifyVideoFile(string label, string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Line($"{label} exists=false path={path}");
                return false;
            }

            var bytes = File.ReadAllBytes(path);
            using var sha = SHA256.Create();
            var hash = Convert.ToHexString(sha.ComputeHash(bytes));
            Line($"{label} exists=true path={path} bytes={bytes.Length} sha256={hash} prefix={Hex(bytes, 24)}");
            return bytes.Length > 0;
        }
        catch (Exception ex)
        {
            Line($"{label} verify exception={Describe(ex)}");
            return false;
        }
    }

    private void CheckMode(long clientPtr)
    {
        try
        {
            Retain(clientPtr, "checkMode");
            Line($"JNIApi.checkMode={Ints(JNIApi.CheckMode(clientPtr))}");
        }
        catch (Exception ex)
        {
            Line($"checkMode exception={Describe(ex)}");
        }
    }

    private void CheckBuffer(long clientPtr, int channel, int attempt)
    {
        try
        {
            Retain(clientPtr, $"checkBuffer[{attempt}]");
            Line($"JNIApi.checkBuffer[{attempt}] channel={channel} result={Ints(JNIApi.CheckBuffer(clientPtr, channel))}");
        }
        catch (Exception ex)
        {
            Line($"checkBuffer[{attempt}] exception={Describe(ex)}");
        }
    }

    private void RunNativeChannelOracle(long clientPtr, string filesDir)
    {
        Line("nativeChannelOracle started");
        var savedCount = 0;
        var totalBytes = 0L;

        try
        {
            Directory.CreateDirectory(filesDir);
        }
        catch (Exception ex)
        {
            Line($"nativeChannelOracle directory exception={Describe(ex)}");
        }

        for (var i = 0; i < 8; i++)
        {
            try
            {
                Retain(clientPtr, $"nativeChannelOracle-checkBuffer[{i}]");
                var bufferState = JNIApi.CheckBuffer(clientPtr, 1);
                Line($"nativeChannelOracle checkBuffer[{i}] channel=1 result={Ints(bufferState)}");

                var buffer = new byte[256 * 1024];
                var result = NativeClientRead(
                    new IntPtr(clientPtr),
                    1,
                    buffer,
                    buffer.Length,
                    450,
                    out var readBytes);
                var count = Math.Clamp(readBytes, 0, buffer.Length);
                var prefix = count > 0 ? Convert.ToHexString(buffer.AsSpan(0, Math.Min(count, 64))) : string.Empty;
                Line($"nativeChannelOracle read[{i}] result={result} bytes={count} prefix={prefix}");

                if (count > 0)
                {
                    var path = System.IO.Path.Combine(filesDir, $"native-channel-oracle-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{i:00}.bin");
                    File.WriteAllBytes(path, buffer.AsSpan(0, count).ToArray());
                    var hash = Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, count)));
                    savedCount++;
                    totalBytes += count;
                    Line($"nativeChannelOracle saved[{i}] path={path} bytes={count} sha256={hash}");
                }
            }
            catch (Exception ex)
            {
                Line($"nativeChannelOracle read[{i}] exception={Describe(ex)}");
            }

            Sleep(350);
        }

        Line($"nativeChannelOracle summary saved={savedCount} totalBytes={totalBytes}");
    }

    private void LogSocketSnapshot(string label)
    {
        try
        {
            var uid = Android.OS.Process.MyUid();
            var entries = ReadSocketTable("/proc/net/tcp", "tcp", uid)
                .Concat(ReadSocketTable("/proc/net/udp", "udp", uid))
                .Take(24)
                .ToArray();

            Line($"socketSnapshot {label} uid={uid} entries={entries.Length}");
            foreach (var entry in entries)
                Line($"socketSnapshot {label} {entry}");
        }
        catch (Exception ex)
        {
            Line($"socketSnapshot {label} exception={Describe(ex)}");
        }
    }

    private static IEnumerable<string> ReadSocketTable(string path, string protocol, int uid)
    {
        if (!File.Exists(path))
            yield break;

        foreach (var line in File.ReadLines(path).Skip(1))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 8 || !int.TryParse(parts[7], out var entryUid))
                continue;

            var local = DecodeAddressPort(parts[1]);
            var remote = DecodeAddressPort(parts[2]);
            if (entryUid != uid && !IsCameraSubnet(local) && !IsCameraSubnet(remote))
                continue;

            yield return $"{protocol} uid={entryUid} local={local} remote={remote} state={parts[3]} txrx={parts[4]}";
        }
    }

    private static bool IsCameraSubnet(string endpoint)
    {
        return endpoint.StartsWith("192.168.168.", StringComparison.Ordinal);
    }

    private static string DecodeAddressPort(string value)
    {
        var pieces = value.Split(':');
        if (pieces.Length != 2)
            return value;

        var addressHex = pieces[0];
        var portHex = pieces[1];
        var port = int.TryParse(portHex, System.Globalization.NumberStyles.HexNumber, null, out var parsedPort)
            ? parsedPort
            : 0;

        if (addressHex.Length != 8)
            return $"{addressHex}:{port}";

        Span<byte> bytes = stackalloc byte[4];
        for (var i = 0; i < 4; i++)
        {
            if (!byte.TryParse(
                    addressHex.AsSpan(i * 2, 2),
                    System.Globalization.NumberStyles.HexNumber,
                    null,
                    out bytes[3 - i]))
            {
                return $"{addressHex}:{port}";
            }
        }

        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{bytes[3]}:{port}";
    }

    private void Retain(long clientPtr, string operation)
    {
        try
        {
            OnMain(() =>
            {
                JNIApi.Retain(clientPtr);
                return true;
            });
            Line($"JNIApi.retain before {operation}=done");
        }
        catch (Exception ex)
        {
            Line($"retain before {operation} exception={Describe(ex)}");
        }
    }

    [DllImport("OKSMARTPPCS", EntryPoint = "client_read", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeClientRead(
        IntPtr clientPtr,
        int channel,
        [Out] byte[] buffer,
        int length,
        int timeoutMs,
        out int readBytes);

    [DllImport("OKSMARTPPCS", EntryPoint = "HLP2P_SetLogLevel", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Hlp2pSetLogLevel(int flags, int level, int enabled);

    private T OnMain<T>(Func<T> call)
    {
        if (Looper.MainLooper?.Thread == Java.Lang.Thread.CurrentThread())
            return call();

        var completed = new ManualResetEventSlim(false);
        T? result = default;
        Exception? error = null;
        _mainHandler.Post(() =>
        {
            try
            {
                result = call();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                completed.Set();
            }
        });
        if (!completed.Wait(MainThreadCallTimeout))
            throw new TimeoutException($"Timed out waiting for main-thread native call after {MainThreadCallTimeout.TotalSeconds:N0}s.");
        if (error is not null)
            throw error;
        return result!;
    }

    private void Load(string name)
    {
        try
        {
            Java.Lang.JavaSystem.LoadLibrary(name);
            Line($"load {name}=ok");
        }
        catch (Exception ex)
        {
            Line($"load {name}={Describe(ex)}");
        }
    }

    private string GetLog()
    {
        lock (_log)
        {
            return _log.ToString();
        }
    }

    private void Line(string value)
    {
        lock (_log)
        {
            _log.AppendLine(value);
        }

        Log.Info(LogTag, value);
    }

    private static void Sleep(long millis)
    {
        try
        {
            System.Threading.Thread.Sleep((int)millis);
        }
        catch (ThreadInterruptedException)
        {
            System.Threading.Thread.CurrentThread.Interrupt();
        }
    }

    private static string Safe(string? value)
    {
        return value ?? "<null>";
    }

    private static int LengthOf(string? value)
    {
        return value?.Length ?? 0;
    }

    private static int LengthOf(byte[]? value)
    {
        return value?.Length ?? 0;
    }

    private static string Ints(int[]? values)
    {
        return values is null ? "<null>" : $"[{string.Join(",", values)}]";
    }

    private static string Hex(byte[]? values, int max)
    {
        if (values is null)
            return "<null>";

        return string.Join(" ", values.Take(max).Select(value => value.ToString("X2")));
    }

    private static string AsciiPreview(byte[]? values, int max)
    {
        if (values is null)
            return "<null>";

        var builder = new StringBuilder();
        foreach (var value in values.Take(max))
        {
            builder.Append(value switch
            {
                0x0d => "\\r",
                0x0a => "\\n",
                0x09 => "\\t",
                >= 0x20 and <= 0x7e => ((char)value).ToString(),
                _ => ".",
            });
        }

        return builder.ToString();
    }

    private static string Describe(Exception ex)
    {
        return $"{ex.GetType().Name}: {ex.Message}";
    }

    private readonly record struct CapturedFrame(string Path, byte[] Bytes);
}
