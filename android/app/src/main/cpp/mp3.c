// The MP3 encoder for Android, a thin bridge from Kotlin (com.motalim.adawat.media.Mp3Encoder) to LAME.
#include <jni.h>
#include <stdlib.h>
#include "lame.h"

#define EXPORT __attribute__((visibility("default")))

// VBR quality 2 (about 190 kbit/s) by default, the same as the desktop apps' "-q:a 2", or an average bitrate.
EXPORT JNIEXPORT jlong JNICALL
Java_com_motalim_adawat_media_Mp3Encoder_open(JNIEnv *env, jclass cls, jint rate, jint channels, jint quality, jint kbps) {
    lame_t lame = lame_init();
    if (!lame) return 0;
    lame_set_in_samplerate(lame, rate);
    lame_set_out_samplerate(lame, rate);
    lame_set_num_channels(lame, channels);
    lame_set_mode(lame, channels == 1 ? MONO : JOINT_STEREO);
    if (kbps > 0) {
        // An average bitrate, for compressing to a size.
        lame_set_VBR(lame, vbr_abr);
        lame_set_VBR_mean_bitrate_kbps(lame, kbps);
    } else {
        lame_set_VBR(lame, vbr_default);
        lame_set_VBR_quality(lame, (float) quality);
    }
    lame_set_quality(lame, 2);
    lame_set_write_id3tag_automatic(lame, 0);
    if (lame_init_params(lame) < 0) {
        lame_close(lame);
        return 0;
    }
    return (jlong) (intptr_t) lame;
}

// Interleaved 16-bit samples in, MP3 bytes out; returns how many bytes were written.
EXPORT JNIEXPORT jint JNICALL
Java_com_motalim_adawat_media_Mp3Encoder_encode(JNIEnv *env, jclass cls, jlong handle, jshortArray pcm, jint frames, jint channels,
                                                jbyteArray out) {
    lame_t lame = (lame_t) (intptr_t) handle;
    jshort *samples = (*env)->GetShortArrayElements(env, pcm, NULL);
    jbyte *buffer = (*env)->GetByteArrayElements(env, out, NULL);
    jsize size = (*env)->GetArrayLength(env, out);
    int written = channels == 1
        ? lame_encode_buffer(lame, samples, samples, frames, (unsigned char *) buffer, size)
        : lame_encode_buffer_interleaved(lame, samples, frames, (unsigned char *) buffer, size);
    (*env)->ReleaseShortArrayElements(env, pcm, samples, JNI_ABORT);
    (*env)->ReleaseByteArrayElements(env, out, buffer, 0);
    return written;
}

EXPORT JNIEXPORT jint JNICALL
Java_com_motalim_adawat_media_Mp3Encoder_flush(JNIEnv *env, jclass cls, jlong handle, jbyteArray out) {
    lame_t lame = (lame_t) (intptr_t) handle;
    jbyte *buffer = (*env)->GetByteArrayElements(env, out, NULL);
    jsize size = (*env)->GetArrayLength(env, out);
    int written = lame_encode_flush(lame, (unsigned char *) buffer, size);
    (*env)->ReleaseByteArrayElements(env, out, buffer, 0);
    return written;
}

// The Xing/LAME header that tells players the length of a VBR file; written over the first frame at the end.
EXPORT JNIEXPORT jint JNICALL
Java_com_motalim_adawat_media_Mp3Encoder_header(JNIEnv *env, jclass cls, jlong handle, jbyteArray out) {
    lame_t lame = (lame_t) (intptr_t) handle;
    jbyte *buffer = (*env)->GetByteArrayElements(env, out, NULL);
    jsize size = (*env)->GetArrayLength(env, out);
    size_t written = lame_get_lametag_frame(lame, (unsigned char *) buffer, size);
    (*env)->ReleaseByteArrayElements(env, out, buffer, 0);
    return (jint) written;
}

EXPORT JNIEXPORT void JNICALL
Java_com_motalim_adawat_media_Mp3Encoder_close(JNIEnv *env, jclass cls, jlong handle) {
    lame_close((lame_t) (intptr_t) handle);
}
