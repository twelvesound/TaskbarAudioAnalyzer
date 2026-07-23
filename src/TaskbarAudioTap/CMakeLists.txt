cmake_minimum_required(VERSION 3.25.0)

if(NOT vst3sdk_SOURCE_DIR)
    message(FATAL_ERROR "Specify vst3sdk_SOURCE_DIR when configuring this project")
endif()

project(TaskbarAudioTap
    VERSION 1.1.0.0
    DESCRIPTION "Taskbar Audio Analyzer VST3 tap"
)

set(SMTG_ENABLE_VST3_HOSTING_EXAMPLES OFF CACHE BOOL "" FORCE)
set(SMTG_ENABLE_VST3_PLUGIN_EXAMPLES OFF CACHE BOOL "" FORCE)
set(SMTG_ENABLE_VSTGUI_SUPPORT OFF CACHE BOOL "" FORCE)

add_subdirectory(${vst3sdk_SOURCE_DIR} ${PROJECT_BINARY_DIR}/vst3sdk)
smtg_enable_vst3_sdk()

smtg_add_vst3plugin(TaskbarAudioTap
    source/cids.h
    source/controller.cpp
    source/entry.cpp
    source/processor.cpp
    source/shared_audio_writer.cpp
    source/shared_audio_writer.h
    source/version.h
)

target_compile_features(TaskbarAudioTap PUBLIC cxx_std_20)
target_compile_definitions(TaskbarAudioTap PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(TaskbarAudioTap PRIVATE sdk)
smtg_target_configure_version_file(TaskbarAudioTap)

if(MSVC)
    set_property(DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR} PROPERTY VS_STARTUP_PROJECT TaskbarAudioTap)
endif()

option(TASKBAR_AUDIO_TAP_BUILD_TESTS "Build Taskbar Audio Tap ownership tests" OFF)
if(TASKBAR_AUDIO_TAP_BUILD_TESTS)
    add_executable(TaskbarAudioTapOwnershipTest
        tests/ownership_test.cpp
        source/shared_audio_writer.cpp
        source/shared_audio_writer.h
    )
    target_compile_features(TaskbarAudioTapOwnershipTest PUBLIC cxx_std_20)
    target_compile_definitions(TaskbarAudioTapOwnershipTest PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_include_directories(TaskbarAudioTapOwnershipTest PRIVATE source)
endif()
