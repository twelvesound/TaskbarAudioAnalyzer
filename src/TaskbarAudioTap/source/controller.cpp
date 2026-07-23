#include "public.sdk/source/vst/vsteditcontroller.h"

namespace TaskbarAudioTap {

using namespace Steinberg;
using namespace Steinberg::Vst;

class Controller final : public EditController
{
public:
    tresult PLUGIN_API initialize(FUnknown* context) SMTG_OVERRIDE
    {
        return EditController::initialize(context);
    }
};

FUnknown* createControllerInstance(void*)
{
    return static_cast<IEditController*>(new Controller);
}

}
