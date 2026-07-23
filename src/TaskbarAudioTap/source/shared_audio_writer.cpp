#include "shared_audio_writer.h"

#include <cmath>
#include <cstring>

namespace TaskbarAudioTap {

SharedAudioWriter::~SharedAudioWriter()
{
    close();
}

bool SharedAudioWriter::open()
{
    if (header_)
        return true;

    constexpr auto totalBytes = SharedMemoryHeaderBytes +
        SharedMemoryCapacityFrames * SharedMemoryChannels * sizeof(float);

    mapping_ = CreateFileMappingW(
        INVALID_HANDLE_VALUE,
        nullptr,
        PAGE_READWRITE,
        0,
        static_cast<DWORD>(totalBytes),
        SharedMemoryName);
    if (!mapping_)
        return false;

    const auto mappingAlreadyExisted = GetLastError() == ERROR_ALREADY_EXISTS;
    view_ = MapViewOfFile(mapping_, FILE_MAP_ALL_ACCESS, 0, 0, totalBytes);
    if (!view_)
    {
        CloseHandle(mapping_);
        mapping_ = nullptr;
        return false;
    }

    header_ = static_cast<SharedAudioHeader*>(view_);
    samples_ = reinterpret_cast<float*>(
        static_cast<std::uint8_t*>(view_) + SharedMemoryHeaderBytes);

    if (!mappingAlreadyExisted ||
        header_->magic != SharedMemoryMagic ||
        header_->version != SharedMemoryVersion ||
        header_->capacityFrames != SharedMemoryCapacityFrames)
    {
        std::memset(view_, 0, totalBytes);
        header_->magic = SharedMemoryMagic;
        header_->version = SharedMemoryVersion;
        header_->capacityFrames = SharedMemoryCapacityFrames;
        header_->channels = SharedMemoryChannels;
    }

    ownerToken_ = static_cast<LONG64>(
        (static_cast<std::uint64_t>(GetCurrentProcessId()) << 32) ^
        static_cast<std::uint64_t>(reinterpret_cast<std::uintptr_t>(this)) ^
        GetTickCount64());
    if (ownerToken_ == 0)
        ownerToken_ = 1;
    tryClaimOwnership();
    return true;
}

void SharedAudioWriter::close()
{
    if (header_ && ownerToken_ != 0)
        InterlockedCompareExchange64(&header_->ownerToken, 0, ownerToken_);

    header_ = nullptr;
    samples_ = nullptr;

    if (view_)
    {
        UnmapViewOfFile(view_);
        view_ = nullptr;
    }

    if (mapping_)
    {
        CloseHandle(mapping_);
        mapping_ = nullptr;
    }

    ownerToken_ = 0;
}

void SharedAudioWriter::setSampleRate(double sampleRate)
{
    if (!std::isfinite(sampleRate))
        return;

    sampleRate_ = static_cast<LONG>(std::clamp(std::lround(sampleRate), 8000L, 768000L));
    if (header_ &&
        InterlockedCompareExchange64(&header_->ownerToken, 0, 0) == ownerToken_)
        InterlockedExchange(&header_->sampleRate, sampleRate_);
}

bool SharedAudioWriter::tryClaimOwnership()
{
    if (!header_ || ownerToken_ == 0)
        return false;

    const auto currentOwner = InterlockedCompareExchange64(&header_->ownerToken, 0, 0);
    if (currentOwner == ownerToken_)
        return true;

    const auto now = static_cast<LONG64>(GetTickCount64());
    const auto heartbeat = InterlockedCompareExchange64(&header_->heartbeatMilliseconds, 0, 0);
    constexpr LONG64 OwnerTimeoutMilliseconds = 1000;
    if (currentOwner != 0 && heartbeat >= 0 && now >= heartbeat &&
        now - heartbeat < OwnerTimeoutMilliseconds)
        return false;

    if (InterlockedCompareExchange64(&header_->ownerToken, ownerToken_, currentOwner) != currentOwner)
        return false;

    if (sampleRate_ > 0)
        InterlockedExchange(&header_->sampleRate, sampleRate_);
    InterlockedExchange64(&header_->heartbeatMilliseconds, now);
    return true;
}

}
