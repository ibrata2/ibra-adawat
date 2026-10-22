cmake_minimum_required(VERSION 3.22)
project(adawat_native C)

# The libraries come from tools/fetch-native.sh, not from this repository.
set(THIRD ${CMAKE_CURRENT_SOURCE_DIR}/third_party)
foreach(name lame libarchive xz bzip2)
    if(NOT EXISTS ${THIRD}/${name}/.fetched)
        message(FATAL_ERROR "${name} source is missing. Run android/tools/fetch-native.sh first.")
    endif()
endforeach()

# 16 KB pages, which Google Play requires for native code.
set(PAGES "-Wl,-z,max-page-size=16384")

# MP3: LAME.
set(LAME ${THIRD}/lame)
file(GLOB LAME_SOURCES ${LAME}/libmp3lame/*.c)
list(FILTER LAME_SOURCES EXCLUDE REGEX ".*/mpglib_interface\\.c$")
add_library(adawat-mp3 SHARED mp3.c ${LAME_SOURCES})
target_include_directories(adawat-mp3 PRIVATE ${LAME}/include ${LAME}/libmp3lame)
target_compile_definitions(adawat-mp3 PRIVATE STDC_HEADERS HAVE_STDINT_H HAVE_LIMITS_H HAVE_STRING_H ieee754_float32_t=float)
target_compile_options(adawat-mp3 PRIVATE -O2 -w -fvisibility=hidden)
target_link_options(adawat-mp3 PRIVATE ${PAGES})
target_link_libraries(adawat-mp3 m log)

# LZMA and XZ: liblzma, built static with xz's own CMake and none of its tools.
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(XZ_NLS OFF CACHE BOOL "" FORCE)
set(XZ_DOC OFF CACHE BOOL "" FORCE)
foreach(tool XZ_TOOL_XZ XZ_TOOL_XZDEC XZ_TOOL_LZMADEC XZ_TOOL_LZMAINFO XZ_TOOL_SCRIPTS XZ_TOOL_SYMLINKS XZ_TOOL_SYMLINKS_LZMA)
    set(${tool} OFF CACHE BOOL "" FORCE)
endforeach()
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
add_subdirectory(${THIRD}/xz ${CMAKE_BINARY_DIR}/xz EXCLUDE_FROM_ALL)

# bzip2.
set(BZ ${THIRD}/bzip2)
add_library(bz2 STATIC ${BZ}/blocksort.c ${BZ}/huffman.c ${BZ}/crctable.c ${BZ}/randtable.c ${BZ}/compress.c ${BZ}/decompress.c ${BZ}/bzlib.c)
target_include_directories(bz2 PUBLIC ${BZ})
target_compile_options(bz2 PRIVATE -O2 -w)

# Archives: libarchive with the source list and config it ships for Android.
set(LA ${THIRD}/libarchive)
file(READ ${LA}/contrib/android/Android.mk ANDROID_MK)
string(REGEX MATCHALL "libarchive/[A-Za-z0-9_]+\\.c" LA_FILES "${ANDROID_MK}")
list(REMOVE_DUPLICATES LA_FILES)
list(TRANSFORM LA_FILES PREPEND ${LA}/)
add_library(adawat-archive SHARED archive.c ${LA_FILES})
target_include_directories(adawat-archive PRIVATE ${LA}/libarchive ${LA}/contrib/android/include ${THIRD}/xz/src/liblzma/api)
target_compile_definitions(adawat-archive PRIVATE HAVE_CONFIG_H=1 PLATFORM_CONFIG_H="${LA}/contrib/android/config/android.h"
                           HAVE_LZMA_H=1 HAVE_LIBLZMA=1 HAVE_BZLIB_H=1 HAVE_LIBBZ2=1 HAVE_ZLIB_H=1 HAVE_LIBZ=1 LZMA_API_STATIC=1)
target_compile_options(adawat-archive PRIVATE -O2 -w -fvisibility=hidden)
target_link_options(adawat-archive PRIVATE ${PAGES})
target_link_libraries(adawat-archive liblzma bz2 z log)
