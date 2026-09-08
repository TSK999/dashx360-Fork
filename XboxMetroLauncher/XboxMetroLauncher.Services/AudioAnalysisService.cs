using System;
using XboxMetroLauncher.Utilities;
using System.Runtime.InteropServices;
using System.Threading;

namespace XboxMetroLauncher.Services;

public sealed class AudioAnalysisService : IDisposable
{
	private readonly record struct AudioFormat(int Channels, int SampleRate, int BitsPerSample, int BlockAlign, bool IsFloat)
	{
		public int BytesPerSample => Math.Max(1, BitsPerSample / 8);

		public static AudioFormat FromPointer(nint pointer)
		{
			int num = (ushort)Marshal.ReadInt16(pointer);
			int val = (ushort)Marshal.ReadInt16(pointer, 2);
			int val2 = Marshal.ReadInt32(pointer, 4);
			int val3 = (ushort)Marshal.ReadInt16(pointer, 12);
			int num2 = (ushort)Marshal.ReadInt16(pointer, 14);
			bool flag = num == 3;
			switch (num)
			{
			case 65534:
			{
				Guid guid = Marshal.PtrToStructure<Guid>(IntPtr.Add(pointer, 24));
				flag = guid == KsdSubTypeIeeeFloat;
				if (num2 == 0 && guid == KsdSubTypePcm)
				{
					num2 = 16;
				}
				break;
			}
			default:
				flag = true;
				break;
			case 1:
			case 3:
				break;
			}
			if (num2 <= 0)
			{
				num2 = (flag ? 32 : 16);
			}
			return new AudioFormat(Math.Max(1, val), Math.Max(8000, val2), num2, Math.Max(1, val3), flag);
		}
	}

	[ComImport]
	[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
	private sealed class MMDeviceEnumerator
	{
	}

	[ComImport]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
	private interface IMMDeviceEnumerator
	{
		int EnumAudioEndpoints(int dataFlow, int stateMask, out nint devices);

		int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);

		int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

		int RegisterEndpointNotificationCallback(nint client);

		int UnregisterEndpointNotificationCallback(nint client);
	}

	[ComImport]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
	private interface IMMDevice
	{
		int Activate(ref Guid iid, int clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);

		int OpenPropertyStore(int access, out nint properties);

		int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

		int GetState(out int state);
	}

	[ComImport]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
	private interface IAudioClient
	{
		int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, nint format, Guid audioSessionGuid);

		int GetBufferSize(out uint bufferFrames);

		int GetStreamLatency(out long latency);

		int GetCurrentPadding(out uint paddingFrames);

		int IsFormatSupported(int shareMode, nint format, out nint closestMatch);

		int GetMixFormat(out nint deviceFormat);

		int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);

		int Start();

		int Stop();

		int Reset();

		int SetEventHandle(nint eventHandle);

		int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
	}

	[ComImport]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
	private interface IAudioCaptureClient
	{
		int GetBuffer(out nint data, out uint frames, out int flags, out long devicePosition, out long qpcPosition);

		int ReleaseBuffer(uint frames);

		int GetNextPacketSize(out uint frames);
	}

	private const int ClsctxAll = 23;

	private const int AudclntSharemodeShared = 0;

	private const int AudclntStreamflagsLoopback = 131072;

	private const int WaveFormatIeeeFloat = 3;

	private const int WaveFormatPcm = 1;

	private const int WaveFormatExtensible = 65534;

	private static readonly Guid AudioClientGuid = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");

	private static readonly Guid AudioCaptureClientGuid = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

	private static readonly Guid KsdSubTypeIeeeFloat = new Guid("00000003-0000-0010-8000-00aa00389b71");

	private static readonly Guid KsdSubTypePcm = new Guid("00000001-0000-0010-8000-00aa00389b71");

	private readonly CaptureWorker _worker;

    public AudioAnalysisService() => _worker = new CaptureWorker(CaptureLoop, ex => App.LogException(ex, "AudioAnalysisService.Capture"));

	private readonly float[] _analysisBuffer = new float[4096];


	private int _bufferWriteIndex;

	private double _bass;

	private double _mid;

	private double _treble;

	private double _loudness;

	private double _peak;

	private double _lastBass;

	private double _lastLoudness;

    public bool IsRunning => _worker.IsRunning;
    public event EventHandler<AudioAnalysisFrame>? FrameReady;
    public void Start() => _worker.Start();
    public void Stop() => _worker.Stop();
    public void Dispose() => _worker.Dispose();

	private void CaptureLoop(CancellationToken token)
	{
		IMMDeviceEnumerator iMMDeviceEnumerator = null;
		IMMDevice endpoint = null;
		IAudioClient audioClient = null;
		IAudioCaptureClient audioCaptureClient = null;
		nint deviceFormat = IntPtr.Zero;
		try
		{
			iMMDeviceEnumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumerator();
			Marshal.ThrowExceptionForHR(iMMDeviceEnumerator.GetDefaultAudioEndpoint(0, 0, out endpoint));
			Guid iid = AudioClientGuid;
			Marshal.ThrowExceptionForHR(endpoint.Activate(ref iid, 23, IntPtr.Zero, out object interfacePointer));
			audioClient = (IAudioClient)interfacePointer;
			Marshal.ThrowExceptionForHR(audioClient.GetMixFormat(out deviceFormat));
			AudioFormat format = AudioFormat.FromPointer(deviceFormat);
			Marshal.ThrowExceptionForHR(audioClient.Initialize(0, 131072, 1000000L, 0L, deviceFormat, Guid.Empty));
			Guid iid2 = AudioCaptureClientGuid;
			Marshal.ThrowExceptionForHR(audioClient.GetService(ref iid2, out object service));
			audioCaptureClient = (IAudioCaptureClient)service;
			Marshal.ThrowExceptionForHR(audioClient.Start());
			while (!token.IsCancellationRequested)
			{
				Marshal.ThrowExceptionForHR(audioCaptureClient.GetNextPacketSize(out var frames));
				while (frames != 0)
				{
					Marshal.ThrowExceptionForHR(audioCaptureClient.GetBuffer(out var data, out var frames2, out var flags, out var _, out var _));
					try
					{
						if ((flags & 2) != 0)
						{
							PushSilence((int)frames2);
						}
						else
						{
							ReadSamples(data, (int)frames2, format);
						}
					}
					finally
					{
						Marshal.ThrowExceptionForHR(audioCaptureClient.ReleaseBuffer(frames2));
					}
					Analyze(format.SampleRate);
					Marshal.ThrowExceptionForHR(audioCaptureClient.GetNextPacketSize(out frames));
				}
				Thread.Sleep(8);
			}
		}
		catch
		{
		}
		finally
		{
			try
			{
				audioClient?.Stop();
			}
			catch
			{
			}
			if (deviceFormat != IntPtr.Zero)
			{
				Marshal.FreeCoTaskMem(deviceFormat);
			}
			ReleaseCom(audioCaptureClient);
			ReleaseCom(audioClient);
			ReleaseCom(endpoint);
			ReleaseCom(iMMDeviceEnumerator);
		}
	}

	private void ReadSamples(nint data, int frames, AudioFormat format)
	{
		int num = Math.Max(1, format.Channels);
		for (int i = 0; i < frames; i++)
		{
			double num2 = 0.0;
			for (int j = 0; j < num; j++)
			{
				int offset = i * format.BlockAlign + j * format.BytesPerSample;
				num2 += ReadSample(data, offset, format);
			}
			PushSample((float)(num2 / (double)num));
		}
	}

	private static double ReadSample(nint data, int offset, AudioFormat format)
	{
		if (format.IsFloat)
		{
			return Marshal.PtrToStructure<float>(IntPtr.Add(data, offset));
		}
		if (format.BitsPerSample == 16)
		{
			return (double)Marshal.ReadInt16(data, offset) / 32768.0;
		}
		if (format.BitsPerSample == 24)
		{
			byte num = Marshal.ReadByte(data, offset);
			byte b = Marshal.ReadByte(data, offset + 1);
			byte b2 = Marshal.ReadByte(data, offset + 2);
			int num2 = num | (b << 8) | (b2 << 16);
			if ((num2 & 0x800000) != 0)
			{
				num2 |= -16777216;
			}
			return (double)num2 / 8388608.0;
		}
		if (format.BitsPerSample == 32)
		{
			return (double)Marshal.ReadInt32(data, offset) / 2147483648.0;
		}
		return 0.0;
	}

	private void PushSilence(int frames)
	{
		for (int i = 0; i < frames; i++)
		{
			PushSample(0f);
		}
	}

	private void PushSample(float sample)
	{
		_analysisBuffer[_bufferWriteIndex] = Math.Clamp(sample, -1f, 1f);
		_bufferWriteIndex = (_bufferWriteIndex + 1) % _analysisBuffer.Length;
	}

	private void Analyze(int sampleRate)
	{
		if (sampleRate <= 0)
		{
			sampleRate = 48000;
		}
		double num = 0.0;
		for (int i = 0; i < _analysisBuffer.Length; i++)
		{
			float num2 = _analysisBuffer[i];
			num += (double)(num2 * num2);
		}
		num = Math.Sqrt(num / (double)_analysisBuffer.Length);
		double value = BandEnergy(sampleRate, 45.0, 65.0, 85.0, 110.0, 145.0);
		double value2 = BandEnergy(sampleRate, 260.0, 420.0, 720.0, 1100.0, 1800.0);
		double value3 = BandEnergy(sampleRate, 3200.0, 5200.0, 7600.0, 10200.0);
		double num3 = NormalizeBand(value, 42.0);
		double target = NormalizeBand(value2, 24.0);
		double target2 = NormalizeBand(value3, 42.0);
		double num4 = NormalizeBand(num, 12.0);
		double target3 = Math.Clamp(Math.Max(num3 - _lastBass * 0.72, num4 - _lastLoudness * 0.78) * 6.2, 0.0, 1.0);
		_bass = Envelope(_bass, num3, 0.86, 0.16);
		_mid = Envelope(_mid, target, 0.58, 0.14);
		_treble = Envelope(_treble, target2, 0.72, 0.22);
		_loudness = Envelope(_loudness, num4, 0.62, 0.12);
		_peak = Envelope(_peak, target3, 0.96, 0.11);
		_lastBass = _bass;
		_lastLoudness = _loudness;
		this.FrameReady?.Invoke(this, new AudioAnalysisFrame(_bass, _mid, _treble, _loudness, _peak));
	}

	private double BandEnergy(int sampleRate, params double[] frequencies)
	{
		double num = 0.0;
		foreach (double frequency in frequencies)
		{
			num += Goertzel(sampleRate, frequency);
		}
		return num / (double)frequencies.Length;
	}

	private double Goertzel(int sampleRate, double frequency)
	{
		double num = 2.0 * Math.Cos(Math.PI * 2.0 * frequency / (double)sampleRate);
		double num2 = 0.0;
		double num3 = 0.0;
		for (int i = 0; i < _analysisBuffer.Length; i++)
		{
			int num4 = (_bufferWriteIndex + i) % _analysisBuffer.Length;
			double num5 = 0.5 - 0.5 * Math.Cos(Math.PI * 2.0 * (double)i / (double)(_analysisBuffer.Length - 1));
			double num6 = (double)_analysisBuffer[num4] * num5 + num * num2 - num3;
			num3 = num2;
			num2 = num6;
		}
		double val = num2 * num2 + num3 * num3 - num * num2 * num3;
		return Math.Sqrt(Math.Max(0.0, val)) / ((double)_analysisBuffer.Length * 0.5);
	}

	private static double NormalizeBand(double value, double gain)
	{
		return Math.Clamp(1.0 - Math.Exp((0.0 - Math.Max(0.0, value)) * gain), 0.0, 1.0);
	}

	private static double Envelope(double current, double target, double attack, double release)
	{
		return current + (target - current) * ((target > current) ? attack : release);
	}

	private static void ReleaseCom(object? instance)
	{
		if (instance != null && Marshal.IsComObject(instance))
		{
			Marshal.ReleaseComObject(instance);
		}
	}
}
