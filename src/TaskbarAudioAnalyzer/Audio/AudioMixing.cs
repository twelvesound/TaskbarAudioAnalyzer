using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace TaskbarAudioAnalyzer;

internal enum InputMixMode
{
    AutoMix,
    WindowsOnly,
    VstOnly
}

internal sealed class StereoSampleQueue
{
    private readonly object gate = new();
    private readonly float[] samples;
    private int readFrame;
    private int writeFrame;
    private int frameCount;

    public StereoSampleQueue(int capacityFrames)
    {
        samples = new float[Math.Max(1, capacityFrames) * 2];
    }

    public void Write(ReadOnlySpan<float> interleavedStereo)
    {
        var incomingFrames = interleavedStereo.Length / 2;
        if (incomingFrames <= 0)
        {
            return;
        }

        lock (gate)
        {
            var capacityFrames = samples.Length / 2;
            if (incomingFrames >= capacityFrames)
            {
                interleavedStereo = interleavedStereo[^samples.Length..];
                incomingFrames = capacityFrames;
                readFrame = 0;
                writeFrame = 0;
                frameCount = 0;
            }

            var overflowFrames = Math.Max(0, frameCount + incomingFrames - capacityFrames);
            if (overflowFrames > 0)
            {
                readFrame = (readFrame + overflowFrames) % capacityFrames;
                frameCount -= overflowFrames;
            }

            for (var frame = 0; frame < incomingFrames; frame++)
            {
                var destination = writeFrame * 2;
                samples[destination] = interleavedStereo[frame * 2];
                samples[destination + 1] = interleavedStereo[frame * 2 + 1];
                writeFrame = (writeFrame + 1) % capacityFrames;
            }

            frameCount += incomingFrames;
        }
    }

    public int Read(Span<float> destination, int requestedFrames)
    {
        lock (gate)
        {
            var capacityFrames = samples.Length / 2;
            var framesToRead = Math.Min(Math.Min(requestedFrames, frameCount), destination.Length / 2);
            for (var frame = 0; frame < framesToRead; frame++)
            {
                var source = readFrame * 2;
                destination[frame * 2] = samples[source];
                destination[frame * 2 + 1] = samples[source + 1];
                readFrame = (readFrame + 1) % capacityFrames;
            }

            frameCount -= framesToRead;
            return framesToRead;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            readFrame = 0;
            writeFrame = 0;
            frameCount = 0;
        }
    }
}

internal sealed class StreamingStereoResampler
{
    private readonly int targetSampleRate;
    private int sourceSampleRate;
    private double nextSourcePosition;
    private float previousLeft;
    private float previousRight;
    private bool hasPreviousFrame;

    public StreamingStereoResampler(int targetSampleRate)
    {
        this.targetSampleRate = targetSampleRate;
    }

    public float[] Process(ReadOnlySpan<float> interleavedStereo, int newSourceSampleRate)
    {
        var sourceFrames = interleavedStereo.Length / 2;
        if (sourceFrames <= 0 || newSourceSampleRate <= 0)
        {
            return [];
        }

        if (sourceSampleRate != newSourceSampleRate)
        {
            sourceSampleRate = newSourceSampleRate;
            nextSourcePosition = 0;
            hasPreviousFrame = false;
        }

        if (!hasPreviousFrame)
        {
            previousLeft = interleavedStereo[0];
            previousRight = interleavedStereo[1];
            hasPreviousFrame = true;
        }

        var step = (double)sourceSampleRate / targetSampleRate;
        var estimatedFrames = Math.Max(1, (int)Math.Ceiling((sourceFrames + 2) / step) + 2);
        var output = new float[estimatedFrames * 2];
        var outputFrames = 0;

        while (nextSourcePosition < sourceFrames - 1)
        {
            var lowerFrame = (int)Math.Floor(nextSourcePosition);
            var fraction = nextSourcePosition - lowerFrame;

            var lowerLeft = lowerFrame < 0 ? previousLeft : interleavedStereo[lowerFrame * 2];
            var lowerRight = lowerFrame < 0 ? previousRight : interleavedStereo[lowerFrame * 2 + 1];
            var upperFrame = lowerFrame + 1;
            var upperLeft = interleavedStereo[upperFrame * 2];
            var upperRight = interleavedStereo[upperFrame * 2 + 1];

            if ((outputFrames + 1) * 2 > output.Length)
            {
                Array.Resize(ref output, output.Length * 2);
            }

            output[outputFrames * 2] = (float)(lowerLeft + (upperLeft - lowerLeft) * fraction);
            output[outputFrames * 2 + 1] = (float)(lowerRight + (upperRight - lowerRight) * fraction);
            outputFrames++;
            nextSourcePosition += step;
        }

        previousLeft = interleavedStereo[(sourceFrames - 1) * 2];
        previousRight = interleavedStereo[(sourceFrames - 1) * 2 + 1];
        nextSourcePosition -= sourceFrames;

        Array.Resize(ref output, outputFrames * 2);
        return output;
    }

    public void Reset()
    {
        sourceSampleRate = 0;
        nextSourcePosition = 0;
        hasPreviousFrame = false;
    }
}

internal sealed class VstTapReceiver : IDisposable
{
    private const string MappingName = @"Local\TaskbarAudioAnalyzer.VstTap.v2";
    private const uint Magic = 0x54424154;
    private const uint Version = 2;
    private const int HeaderBytes = 64;
    private const int MaximumFramesPerRead = 4096;

    private readonly CancellationTokenSource cancellation = new();
    private readonly Thread receiverThread;
    private long lastFreshHeartbeatAt;

    public VstTapReceiver()
    {
        receiverThread = new Thread(ReceiverLoop)
        {
            IsBackground = true,
            Name = "Taskbar Audio VST receiver"
        };
    }

    public event Action<float[], int>? SamplesAvailable;

    public bool IsConnected => Environment.TickCount64 - Volatile.Read(ref lastFreshHeartbeatAt) < 1000;

    public void Start() => receiverThread.Start();

    private void ReceiverLoop()
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                using var mapping = MemoryMappedFile.OpenExisting(MappingName, MemoryMappedFileRights.Read);
                using var accessor = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                ReadMapping(accessor);
            }
            catch (FileNotFoundException)
            {
                cancellation.Token.WaitHandle.WaitOne(250);
            }
            catch (Exception) when (!cancellation.IsCancellationRequested)
            {
                cancellation.Token.WaitHandle.WaitOne(250);
            }
        }
    }

    private void ReadMapping(MemoryMappedViewAccessor accessor)
    {
        if (accessor.ReadUInt32(0) != Magic || accessor.ReadUInt32(4) != Version)
        {
            return;
        }

        var capacityFrames = checked((int)accessor.ReadUInt32(8));
        var channels = checked((int)accessor.ReadUInt32(12));
        if (capacityFrames <= 0 || channels != 2)
        {
            return;
        }

        long readFrame = 0;
        var isAligned = false;
        while (!cancellation.IsCancellationRequested)
        {
            var sampleRate = accessor.ReadInt32(16);
            var writeFrame = accessor.ReadInt64(24);
            var heartbeat = accessor.ReadInt64(32);
            Thread.MemoryBarrier();

            var now = Environment.TickCount64;
            var heartbeatIsFresh = sampleRate > 0 && now >= heartbeat && now - heartbeat < 1000;
            if (!heartbeatIsFresh)
            {
                isAligned = false;
                cancellation.Token.WaitHandle.WaitOne(20);
                continue;
            }

            Volatile.Write(ref lastFreshHeartbeatAt, now);
            if (!isAligned || writeFrame < readFrame)
            {
                readFrame = writeFrame;
                isAligned = true;
            }

            var availableFrames = writeFrame - readFrame;
            if (availableFrames > capacityFrames)
            {
                readFrame = writeFrame - capacityFrames;
                availableFrames = capacityFrames;
            }

            if (availableFrames <= 0)
            {
                cancellation.Token.WaitHandle.WaitOne(5);
                continue;
            }

            var framesToRead = (int)Math.Min(availableFrames, MaximumFramesPerRead);
            var block = new float[framesToRead * 2];
            var firstRingFrame = (int)(readFrame % capacityFrames);
            var firstPartFrames = Math.Min(framesToRead, capacityFrames - firstRingFrame);
            accessor.ReadArray(HeaderBytes + firstRingFrame * 2L * sizeof(float), block, 0, firstPartFrames * 2);

            var remainingFrames = framesToRead - firstPartFrames;
            if (remainingFrames > 0)
            {
                accessor.ReadArray(HeaderBytes, block, firstPartFrames * 2, remainingFrames * 2);
            }

            readFrame += framesToRead;
            SamplesAvailable?.Invoke(block, sampleRate);
        }
    }

    public void Dispose()
    {
        cancellation.Cancel();
        if (receiverThread.IsAlive)
        {
            receiverThread.Join(1000);
        }

        cancellation.Dispose();
    }
}
