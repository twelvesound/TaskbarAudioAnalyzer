#include "cids.h"
#include "shared_audio_writer.h"

#include "public.sdk/source/vst/utility/audiobuffers.h"
#include "public.sdk/source/vst/vstaudioeffect.h"

#include <algorithm>
#include <cstring>

namespace TaskbarAudioTap {

using namespace Steinberg;
using namespace Steinberg::Vst;

class Processor final : public AudioEffect
{
public:
    Processor()
    {
        setControllerClass(ControllerUID);
    }

    tresult PLUGIN_API initialize(FUnknown* context) SMTG_OVERRIDE
    {
        const auto result = AudioEffect::initialize(context);
        if (result != kResultOk)
            return result;

        addAudioInput(STR16("Stereo In"), SpeakerArr::kStereo);
        addAudioOutput(STR16("Stereo Out"), SpeakerArr::kStereo);
        writer_.open();
        return kResultOk;
    }

    tresult PLUGIN_API terminate() SMTG_OVERRIDE
    {
        writer_.close();
        return AudioEffect::terminate();
    }

    tresult PLUGIN_API setupProcessing(ProcessSetup& setup) SMTG_OVERRIDE
    {
        const auto result = AudioEffect::setupProcessing(setup);
        if (result == kResultOk)
            writer_.setSampleRate(setup.sampleRate);
        return result;
    }

    tresult PLUGIN_API setBusArrangements(
        SpeakerArrangement* inputs,
        int32 numInputs,
        SpeakerArrangement* outputs,
        int32 numOutputs) SMTG_OVERRIDE
    {
        if (numInputs != 1 || numOutputs != 1 ||
            inputs[0] != SpeakerArr::kStereo || outputs[0] != SpeakerArr::kStereo)
            return kResultFalse;

        return AudioEffect::setBusArrangements(inputs, numInputs, outputs, numOutputs);
    }

    tresult PLUGIN_API canProcessSampleSize(int32 symbolicSampleSize) SMTG_OVERRIDE
    {
        return symbolicSampleSize == kSample32 || symbolicSampleSize == kSample64
            ? kResultTrue
            : kResultFalse;
    }

    tresult PLUGIN_API process(ProcessData& data) SMTG_OVERRIDE
    {
        if (data.numSamples <= 0 || data.numInputs < 1 || data.numOutputs < 1)
            return kResultOk;

        if (processSetup.symbolicSampleSize == kSample64)
            processBlock<Sample64, kSample64>(data);
        else
            processBlock<Sample32, kSample32>(data);

        return kResultOk;
    }

    tresult PLUGIN_API setState(IBStream*) SMTG_OVERRIDE { return kResultOk; }
    tresult PLUGIN_API getState(IBStream*) SMTG_OVERRIDE { return kResultOk; }

private:
    template <typename Sample, SymbolicSampleSizes SampleSize>
    void processBlock(ProcessData& data)
    {
        auto inputChannels = getChannelBuffers<SampleSize>(data.inputs[0]);
        auto outputChannels = getChannelBuffers<SampleSize>(data.outputs[0]);
        const auto inputCount = data.inputs[0].numChannels;
        const auto outputCount = data.outputs[0].numChannels;

        const Sample* left = inputChannels && inputCount > 0 ? inputChannels[0] : nullptr;
        const Sample* right = inputChannels && inputCount > 1 ? inputChannels[1] : left;

        for (int32 channel = 0; channel < outputCount; ++channel)
        {
            auto* destination = outputChannels ? outputChannels[channel] : nullptr;
            const auto* source = inputChannels && channel < inputCount
                ? inputChannels[channel]
                : nullptr;
            if (!destination)
                continue;

            if (source && source != destination)
                std::memcpy(destination, source, sizeof(Sample) * data.numSamples);
            else if (!source)
                std::fill_n(destination, data.numSamples, static_cast<Sample>(0));
        }

        data.outputs[0].silenceFlags = data.inputs[0].silenceFlags;
        writer_.write(left, right, data.numSamples);
    }

    SharedAudioWriter writer_;
};

FUnknown* createProcessorInstance(void*)
{
    return static_cast<IAudioProcessor*>(new Processor);
}

}
