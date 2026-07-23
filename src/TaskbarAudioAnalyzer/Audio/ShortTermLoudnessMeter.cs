namespace TaskbarAudioAnalyzer;

/// <summary>
/// Stereo short-term loudness meter based on ITU-R BS.1770 K-weighting and
/// the three-second EBU Tech 3341 short-term measurement window.
/// </summary>
internal sealed class ShortTermLoudnessMeter
{
    internal const double FloorLufs = -90;
    private const double LoudnessOffset = -0.691;
    private const int WindowSeconds = 3;

    private readonly double[] energyHistory;
    private readonly KWeightingFilter leftFilter = new();
    private readonly KWeightingFilter rightFilter = new();
    private int historyIndex;
    private double energySum;

    public ShortTermLoudnessMeter(int sampleRate)
    {
        if (sampleRate != 48000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate),
                "The BS.1770 filter coefficients are calibrated for the analyzer's 48 kHz mixer.");
        }

        energyHistory = new double[sampleRate * WindowSeconds];
    }

    public double LoudnessLufs
    {
        get
        {
            var meanChannelEnergy = energySum / energyHistory.Length;
            return meanChannelEnergy > 0
                ? Math.Max(FloorLufs, LoudnessOffset + 10 * Math.Log10(meanChannelEnergy))
                : FloorLufs;
        }
    }

    public void ProcessFrame(float left, float right)
    {
        var weightedLeft = leftFilter.Process(left);
        var weightedRight = rightFilter.Process(right);
        var energy = weightedLeft * weightedLeft + weightedRight * weightedRight;

        energySum += energy - energyHistory[historyIndex];
        energyHistory[historyIndex] = energy;
        historyIndex++;
        if (historyIndex == energyHistory.Length)
        {
            historyIndex = 0;
        }

        // Prevent a tiny negative value after prolonged rolling subtraction.
        if (energySum < 0 && energySum > -1e-12)
        {
            energySum = 0;
        }
    }

    public void Reset()
    {
        Array.Clear(energyHistory);
        historyIndex = 0;
        energySum = 0;
        leftFilter.Reset();
        rightFilter.Reset();
    }

    private sealed class KWeightingFilter
    {
        // ITU-R BS.1770 K-weighting coefficients at 48 kHz:
        // stage 1 is the head-response shelving filter, stage 2 is RLB high-pass.
        private readonly Biquad shelf = new(
            1.53512485958697,
            -2.69169618940638,
            1.19839281085285,
            -1.69065929318241,
            0.73248077421585);
        private readonly Biquad highPass = new(
            1,
            -2,
            1,
            -1.99004745483398,
            0.99007225036621);

        public double Process(double sample) => highPass.Process(shelf.Process(sample));

        public void Reset()
        {
            shelf.Reset();
            highPass.Reset();
        }
    }

    private sealed class Biquad(
        double b0,
        double b1,
        double b2,
        double a1,
        double a2)
    {
        private double input1;
        private double input2;
        private double output1;
        private double output2;

        public double Process(double input)
        {
            var output =
                b0 * input +
                b1 * input1 +
                b2 * input2 -
                a1 * output1 -
                a2 * output2;

            input2 = input1;
            input1 = input;
            output2 = output1;
            output1 = output;
            return output;
        }

        public void Reset()
        {
            input1 = 0;
            input2 = 0;
            output1 = 0;
            output2 = 0;
        }
    }
}
