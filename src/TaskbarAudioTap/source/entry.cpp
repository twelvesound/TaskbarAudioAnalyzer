#include "cids.h"
#include "version.h"

#include "pluginterfaces/vst/ivstaudioprocessor.h"
#include "pluginterfaces/vst/ivsteditcontroller.h"
#include "public.sdk/source/main/pluginfactory.h"

using namespace Steinberg;
using namespace Steinberg::Vst;

namespace TaskbarAudioTap {
FUnknown* createProcessorInstance(void*);
FUnknown* createControllerInstance(void*);
}

BEGIN_FACTORY_DEF(
    "12sound",
    "",
    "")

    DEF_CLASS2(
        INLINE_UID_FROM_FUID(TaskbarAudioTap::ProcessorUID),
        PClassInfo::kManyInstances,
        kVstAudioEffectClass,
        "Taskbar Audio Tap",
        Vst::kDistributable,
        "Fx|Analyzer",
        FULL_VERSION_STR,
        kVstVersionString,
        TaskbarAudioTap::createProcessorInstance)

    DEF_CLASS2(
        INLINE_UID_FROM_FUID(TaskbarAudioTap::ControllerUID),
        PClassInfo::kManyInstances,
        kVstComponentControllerClass,
        "Taskbar Audio Tap Controller",
        0,
        "",
        FULL_VERSION_STR,
        kVstVersionString,
        TaskbarAudioTap::createControllerInstance)

END_FACTORY
