using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.CoreAudioApi.Interfaces;

namespace winsndrec
{
    internal class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr CreateEventExW(IntPtr lpEventAttributes, IntPtr lpName, EventFlags dwFlags, EventAccess dwDesiredAccess);

        [DllImport("kernel32.dll")]
        internal static extern bool ResetEvent(IntPtr handle);

        [DllImport("kernel32.dll")]
        internal static extern bool SetEvent(IntPtr handle);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        public static extern int WaitForSingleObjectEx(IntPtr hEvent, int milliseconds, bool bAlertable);

        [DllImport("kernel32.dll")]
        public static extern uint WaitForMultipleObjects(uint nCount, IntPtr[] lpHandles, bool bWaitAll, uint dwMilliseconds);

        public const uint INFINITE = 0xFFFFFFFF;
        public const uint WAIT_TIMEOUT = 0x00000102;
        public const uint WAIT_FAILED = 0xFFFFFFFF;
        public const uint WAIT_OBJECT_0 = 0;
        public const uint WAIT_ABANDONED_0 = 128;
    }

    [Flags]
    internal enum EventFlags
    {
        NONE = 0,
        CREATE_EVENT_INITIAL_SET = 0x2,
        CREATE_EVENT_MANUAL_RESET = 0x1
    }

    [Flags]
    internal enum EventAccess
    {
        STANDARD_RIGHTS_REQUIRED = 0xF0000,
        SYNCHRONIZE = 0x100000,
        EVENT_ALL_ACCESS = STANDARD_RIGHTS_REQUIRED | SYNCHRONIZE | 0x3,
        EVENT_MODIFY_STATE = 0x02
    }

    internal class WASAPICapture : IDisposable, IAudioSessionEventsHandler, IMMNotificationClient
    {
        MMDeviceEnumerator? deviceEnumerator;
        MMDevice? endpoint;
        AudioClient? audioClient;
        AudioCaptureClient? audioCaptureClient;
        AudioSessionControl? audioSessionControl;
        WaveFormat? mixFormat;
        IntPtr shutdownEvent;
        IntPtr audioSamplesReadyEvent;
        IntPtr streamSwitchEvent;
        IntPtr streamSwitchCompleteEvent;
        bool inStreamSwitch = false;
        WaveFileWriter? waveFileWriter;
        string? outputBaseFileName;
        string? outputFileName;
        int? outputBitsPerSample;
        float? truncateSilenceThreshold;
        Thread? thread;
        long currentFrameCount;

        public WASAPICapture(string? outputBaseFileName = null, int? outputBitsPerSample = null, float? truncateSilence = null)
        {
            this.outputBaseFileName = outputBaseFileName;
            this.outputBitsPerSample = outputBitsPerSample;
            truncateSilenceThreshold = Utility.decibelToFloat(truncateSilence);
        }

        public bool Start()
        {
            shutdownEvent = NativeMethods.CreateEventExW(IntPtr.Zero, IntPtr.Zero, 0, EventAccess.EVENT_MODIFY_STATE | EventAccess.SYNCHRONIZE);
            audioSamplesReadyEvent = NativeMethods.CreateEventExW(IntPtr.Zero, IntPtr.Zero, 0, EventAccess.EVENT_MODIFY_STATE | EventAccess.SYNCHRONIZE);
            streamSwitchEvent = NativeMethods.CreateEventExW(IntPtr.Zero, IntPtr.Zero, 0, EventAccess.EVENT_MODIFY_STATE | EventAccess.SYNCHRONIZE);
            streamSwitchCompleteEvent = NativeMethods.CreateEventExW(IntPtr.Zero, IntPtr.Zero, EventFlags.CREATE_EVENT_INITIAL_SET | EventFlags.CREATE_EVENT_MANUAL_RESET, EventAccess.EVENT_MODIFY_STATE | EventAccess.SYNCHRONIZE);
            deviceEnumerator = new MMDeviceEnumerator();

            if (!startAudioClient())
            {
                return false;
            }

            thread = new Thread(new ThreadStart(captureThread));
            thread.Start();

            return true;
        }

        public void Shutdown()
        {
            if (shutdownEvent != IntPtr.Zero)
                NativeMethods.SetEvent(shutdownEvent);
        }

        public void Wait()
        {
            if (thread != null)
            {
                thread.Join();
            }
        }

        bool startAudioClient()
        {
            endAudioClient();

            var waitResult = NativeMethods.WaitForSingleObjectEx(streamSwitchCompleteEvent, 500, false);
            if (waitResult == NativeMethods.WAIT_TIMEOUT)
            {
                Console.Error.WriteLine("Stream switch timeout");
                return false;
            }

            NativeMethods.ResetEvent(audioSamplesReadyEvent);
            NativeMethods.ResetEvent(streamSwitchCompleteEvent);

            if (deviceEnumerator == null)
                throw new ArgumentNullException("deviceEnumerator");
            endpoint = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            if (endpoint == null)
            {
                Console.Error.WriteLine("Unable to retrieve default audio device");
                return false;
            }

            audioClient = endpoint.AudioClient;
            mixFormat = audioClient.MixFormat;
            if (outputBitsPerSample != null)
            {
                mixFormat = new WaveFormat(mixFormat.SampleRate, (int)outputBitsPerSample, mixFormat.Channels);
            }

            if (waveFileWriter != null)
            {
                waveFileWriter.Close();
                waveFileWriter.Dispose();
                waveFileWriter = null;
            }

            const int engineLatencyInMS = 100;
            var streamFlags = AudioClientStreamFlags.EventCallback | AudioClientStreamFlags.NoPersist | AudioClientStreamFlags.Loopback;

            audioClient.Initialize(AudioClientShareMode.Shared, streamFlags, engineLatencyInMS * 10000, 0, mixFormat, Guid.Empty);
            audioClient.SetEventHandle(audioSamplesReadyEvent);

            audioCaptureClient = audioClient.AudioCaptureClient;

            audioSessionControl = endpoint.AudioSessionManager.AudioSessionControl;
            audioSessionControl.RegisterEventClient(this);

            deviceEnumerator.RegisterEndpointNotificationCallback(this);

            Console.Error.WriteLine("{0} ({1}khz {2}bit {3}kb/s)",
                endpoint.FriendlyName,
                mixFormat.SampleRate / 1000,
                mixFormat.BitsPerSample,
                mixFormat.AverageBytesPerSecond / 1000);

            if (waveFileWriter == null)
            {
                outputFileName = getOutputFileName(outputBaseFileName);
                if (outputFileName == null)
                {
                    Console.Error.WriteLine("Unable to generate audio file's name");
                    endAudioClient();
                    return false;
                }
                currentFrameCount = 0;

                var fullpath = Path.GetFullPath(outputFileName);
                Console.Error.WriteLine("{0}", fullpath);

                waveFileWriter = new WaveFileWriter(fullpath, mixFormat);
                if (waveFileWriter == null)
                {
                    Console.Error.WriteLine("Unable to create audio file");
                    endAudioClient();
                    return false;
                }
            }

            inStreamSwitch = false;
            return true;
        }

        void endAudioClient()
        {
            try
            {
                deviceEnumerator?.UnregisterEndpointNotificationCallback(this);
            }
            catch { }

            if (audioClient != null)
            {
                audioClient.Stop();
            }

            if (audioSessionControl != null)
            {
                try
                {
                    audioSessionControl.UnRegisterEventClient(this);
                }
                catch { }
                audioSessionControl.Dispose();
                audioSessionControl = null;
            }

            if (audioCaptureClient != null)
            {
                audioCaptureClient.Dispose();
                audioCaptureClient = null;
            }

            if (audioClient != null)
            {
                audioClient.Dispose();
                audioClient = null;
            }

            if (endpoint != null)
            {
                endpoint.Dispose();
                endpoint = null;
            }
        }

        void captureThread()
        {
            if (audioClient == null)
                throw new ArgumentNullException("audioClient");
            audioClient.Start();

            bool stillPlaying = true;
            while (stillPlaying)
            {
                var waitArray = new IntPtr[3] { shutdownEvent, streamSwitchEvent, audioSamplesReadyEvent };
                var waitResult = NativeMethods.WaitForMultipleObjects(3, waitArray, false, NativeMethods.INFINITE);
                switch (waitResult)
                {
                    case NativeMethods.WAIT_OBJECT_0 + 0:
                        stillPlaying = false;
                        break;
                    case NativeMethods.WAIT_OBJECT_0 + 1:
                        Console.Error.WriteLine("");
                        if (startAudioClient())
                        {
                            if (audioClient == null)
                                throw new ArgumentNullException("audioClient");
                            audioClient.Start();
                        }
                        else
                        {
                            stillPlaying = false;
                        }
                        break;
                    case NativeMethods.WAIT_OBJECT_0 + 2:
                        if (audioCaptureClient == null)
                            throw new ArgumentNullException("audioCaptureClient");
                        if (waveFileWriter == null)
                            throw new ArgumentNullException("waveFileWriter");
                        if (mixFormat == null)
                            throw new ArgumentNullException("mixFormat");
                        var buffer = audioCaptureClient.GetBuffer(out int framesAvailable, out AudioClientBufferFlags flags);
                        var bytesAvailable = framesAvailable * mixFormat.BlockAlign;
                        byte[] bytes = new byte[bytesAvailable];
                        bool isSilent = false;
                        if ((flags & AudioClientBufferFlags.Silent) != 0)
                        {
                            isSilent = true;
                        }
                        else
                        {
                            Marshal.Copy(buffer, bytes, 0, bytesAvailable);
                            if (truncateSilenceThreshold != null)
                            {
                                var samples = Utility.readSamples(mixFormat, bytes);
                                if (samples != null)
                                {
                                    float maxSample = samples.Select(x => Math.Abs(x)).Max();
                                    if (maxSample < truncateSilenceThreshold)
                                    {
                                        isSilent = true;
                                    }
                                }
                            }
                        }
                        audioCaptureClient.ReleaseBuffer(framesAvailable);
                        if (isSilent)
                        {
                        }
                        else
                        {
                            waveFileWriter.Write(bytes, 0, bytesAvailable);
                            waveFileWriter.Flush();
                            currentFrameCount += framesAvailable;
                            showCurrentFrameTime();
                        }
                        break;
                }
            }
        }

        void showCurrentFrameTime()
        {
            if (mixFormat == null)
                throw new ArgumentException("mixFormat");
            var ms = (Double)(currentFrameCount * 1000f / mixFormat.SampleRate);
            var str = String.Format("\r{0}       ", milliSecondsToString(ms));
            Console.Error.WriteAsync(str);
        }

        string milliSecondsToString(Double milliSeconds)
        {
            var t = TimeSpan.FromMilliseconds(milliSeconds);
            return t.ToString(@"hh\:mm\:ss\.fff");
        }


        string? getOutputFileName(string? baseFileName)
        {
            if (baseFileName != null)
            {
                baseFileName = baseFileName.Trim();
            }

            if (baseFileName == null || baseFileName.Length == 0)
            {
                baseFileName = "audio";
            }


            if (Path.GetExtension(baseFileName) != ".wav")
            {
                baseFileName += ".wav";
            }

            if (!File.Exists(baseFileName))
                return baseFileName;

            for (int i = 0; i < 10000; i++)
            {
                var fileName = String.Format("{0}.{1:0000}{2}", Path.GetFileNameWithoutExtension(baseFileName), i, Path.GetExtension(baseFileName));
                var baseDir = Path.GetDirectoryName(baseFileName);
                if (baseDir != null)
                    fileName = Path.Combine(baseDir, fileName);
                if (!File.Exists(fileName))
                    return fileName;
            }

            return null;
        }

        #region IDisposable Members
        public void Dispose()
        {
            endAudioClient();
            Shutdown();
            Wait();
            thread = null;

            if (waveFileWriter != null)
            {
                waveFileWriter.Close();
                waveFileWriter.Dispose();
                waveFileWriter = null;
            }

            if (deviceEnumerator != null)
            {
                deviceEnumerator.Dispose();
                deviceEnumerator = null;
            }
        }
        #endregion

        #region IAudioSessionEventsHandler Members
        public void OnVolumeChanged(float volume, bool isMuted) { }
        public void OnDisplayNameChanged(string displayName) { }
        public void OnIconPathChanged(string iconPath) { }
        public void OnChannelVolumeChanged(UInt32 channelCount, IntPtr newVolumes, UInt32 channelIndex) { }
        public void OnGroupingParamChanged(ref Guid groupingId) { }
        public void OnStateChanged(AudioSessionState state) { }
        public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
        {
            if (disconnectReason == AudioSessionDisconnectReason.DisconnectReasonDeviceRemoval)
            {
                inStreamSwitch = true;
                NativeMethods.SetEvent(streamSwitchEvent);
            }
            else if (disconnectReason == AudioSessionDisconnectReason.DisconnectReasonFormatChanged)
            {
                inStreamSwitch = true;
                NativeMethods.SetEvent(streamSwitchEvent);
                NativeMethods.SetEvent(streamSwitchCompleteEvent);
            }
        }
        #endregion

        #region IMMNotificationClient Members
        public void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.I4)] DeviceState newState) { }
        public void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId) { }
        public void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId) { }
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, [MarshalAs(UnmanagedType.LPWStr)] string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Console)
            {
                if (!inStreamSwitch)
                {
                    inStreamSwitch = true;
                    NativeMethods.SetEvent(streamSwitchEvent);
                }
                NativeMethods.SetEvent(streamSwitchCompleteEvent);
            }
        }
        public void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, PropertyKey key) { }
        #endregion
    }
}
