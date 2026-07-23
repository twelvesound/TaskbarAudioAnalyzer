#include "shared_audio_writer.h"

#include <array>
#include <iostream>

using namespace TaskbarAudioTap;

int main()
{
    SharedAudioWriter first;
    SharedAudioWriter second;
    if (!first.open() || !second.open())
    {
        std::cerr << "Could not open the shared audio mapping.\n";
        return 1;
    }

    first.setSampleRate(48000);
    second.setSampleRate(96000);

    auto mapping = OpenFileMappingW(FILE_MAP_READ, FALSE, SharedMemoryName);
    if (!mapping)
    {
        std::cerr << "Could not reopen the shared audio mapping.\n";
        return 2;
    }

    auto view = MapViewOfFile(mapping, FILE_MAP_READ, 0, 0, SharedMemoryHeaderBytes);
    if (!view)
    {
        CloseHandle(mapping);
        std::cerr << "Could not map the shared audio header.\n";
        return 3;
    }

    auto* header = static_cast<const SharedAudioHeader*>(view);
    const auto initialFrame = header->writeFrame;
    std::array<float, 16> firstSignal {};
    std::array<float, 16> secondSignal {};
    firstSignal.fill(0.25f);
    secondSignal.fill(-0.75f);

    first.write(firstSignal.data(), firstSignal.data() + 1, 8);
    const auto afterFirst = header->writeFrame;
    second.write(secondSignal.data(), secondSignal.data() + 1, 8);
    const auto afterRejectedSecond = header->writeFrame;

    if (afterFirst != initialFrame + 8 || afterRejectedSecond != afterFirst ||
        header->sampleRate != 48000)
    {
        UnmapViewOfFile(view);
        CloseHandle(mapping);
        std::cerr << "A second writer was not rejected while the first writer was active.\n";
        return 4;
    }

    first.close();
    second.write(secondSignal.data(), secondSignal.data() + 1, 8);
    const auto afterTakeover = header->writeFrame;
    const auto takeoverRate = header->sampleRate;

    UnmapViewOfFile(view);
    CloseHandle(mapping);

    if (afterTakeover != afterFirst + 8 || takeoverRate != 96000)
    {
        std::cerr << "The second writer did not take over after the first writer closed.\n";
        return 5;
    }

    std::cout << "Ownership test passed.\n";
    return 0;
}
