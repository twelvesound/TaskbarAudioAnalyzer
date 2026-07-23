#pragma once

#include <algorithm>
#include <cstdint>
#include <type_traits>
#include <windows.h>

namespace TaskbarAudioTap {

constexpr wchar_t SharedMemoryName[] = L"Local\\TaskbarAudioAnalyzer.VstTap.v2";
constexpr std::uint32_t SharedMemoryMagic = 0x54424154;
constexpr std::uint32_t SharedMemoryVersion = 2;
constexpr std::uint32_t SharedMemoryCapacityFrames = 65536;
constexpr std::uint32_t SharedMemoryChannels = 2;
constexpr std::size_t SharedMemoryHeaderBytes = 64;

struct alignas(8) SharedAudioHeader
{
    std::uint32_t magic;
    std::uint32_t version;
    std::uint32_t capacityFrames;
    std::uint32_t channels;
    volatile LONG sampleRate;
    std::uint32_t reserved0;
    alignas(8) volatile LONG64 writeFrame;
    alignas(8) volatile LONG64 heartbeatMilliseconds;
    alignas(8) volatile LONG64 ownerToken;
    std::uint8_t reserved[16];
};

static_assert(sizeof(SharedAudioHeader) == SharedMemoryHeaderBytes);

class SharedAudioWriter
{
public:
    SharedAudioWriter() = default;
    ~SharedAudioWriter();

    bool open();
    void close();
    void setSampleRate(double sampleRate);

    template <typename Sample>
    void write(const Sample* left, const Sample* right, std::int32_t frameCount)
    {
        if (!header_ || !samples_ || frameCount <= 0 || !tryClaimOwnership())
            return;

        auto firstFrame = static_cast<std::uint64_t>(
            InterlockedCompareExchange64(&header_->writeFrame, 0, 0));

        for (std::int32_t frame = 0; frame < frameCount; ++frame)
        {
            const auto ringFrame = (firstFrame + static_cast<std::uint64_t>(frame)) %
                                   SharedMemoryCapacityFrames;
            const auto sampleIndex = static_cast<std::size_t>(ringFrame) * SharedMemoryChannels;
            samples_[sampleIndex] = left ? static_cast<float>(left[frame]) : 0.0f;
            samples_[sampleIndex + 1] = right ? static_cast<float>(right[frame]) : 0.0f;
        }

        MemoryBarrier();
        InterlockedExchange64(
            &header_->heartbeatMilliseconds,
            static_cast<LONG64>(GetTickCount64()));
        InterlockedExchange64(
            &header_->writeFrame,
            static_cast<LONG64>(firstFrame + static_cast<std::uint64_t>(frameCount)));
    }

private:
    bool tryClaimOwnership();

    HANDLE mapping_ {nullptr};
    void* view_ {nullptr};
    SharedAudioHeader* header_ {nullptr};
    float* samples_ {nullptr};
    LONG64 ownerToken_ {0};
    LONG sampleRate_ {0};
};

}
