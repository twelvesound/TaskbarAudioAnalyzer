using TaskbarAudioAnalyzer;

const int sampleRate = 48000;
const int durationFrames = sampleRate * 3;
const double amplitude = 0.1; // -20 dBFS peak
const double frequency = 1000;

VerifySine("single-channel 1 kHz", rightChannelEnabled: false, expectedLufs: -23.01);
VerifySine("stereo 1 kHz", rightChannelEnabled: true, expectedLufs: -20.00);
VerifySilence();

Console.WriteLine("All short-term loudness calibration tests passed.");
return;

static void VerifySine(string name, bool rightChannelEnabled, double expectedLufs)
{
    var meter = new ShortTermLoudnessMeter(sampleRate);
    for (var frame = 0; frame < durationFrames; frame++)
    {
        var sample = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * frame / sampleRate));
        meter.ProcessFrame(sample, rightChannelEnabled ? sample : 0);
    }

    var actual = meter.LoudnessLufs;
    if (Math.Abs(actual - expectedLufs) > 0.1)
    {
        throw new InvalidOperationException(
            $"{name}: expected {expectedLufs:0.00} LUFS, actual {actual:0.00} LUFS.");
    }

    Console.WriteLine($"{name}: {actual:0.00} LUFS");
}

static void VerifySilence()
{
    var meter = new ShortTermLoudnessMeter(sampleRate);
    for (var frame = 0; frame < durationFrames; frame++)
    {
        meter.ProcessFrame(0, 0);
    }

    if (meter.LoudnessLufs != ShortTermLoudnessMeter.FloorLufs)
    {
        throw new InvalidOperationException($"silence: actual {meter.LoudnessLufs:0.00} LUFS.");
    }

    Console.WriteLine("silence: -∞ LUFS");
}
