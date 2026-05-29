package com.veepai;

import android.view.Surface;
import java.io.File;

public final class AppPlayerApi {
    public interface AppPlayerProgress {
        void app_player_draw_info(long textureId, int width, int height, int drawType, float x1, float y1, float x2, float y2);

        void app_player_gps_info(long textureId, int type, int value, float x1, float y1, float x2, float y2);

        void app_player_head_info(long textureId, int type, int width, int height);

        void app_player_progress(
                long textureId,
                int status,
                int current,
                int total,
                int width,
                int height,
                int videoType,
                int frameRate,
                long position,
                long duration);
    }

    static {
        System.loadLibrary("OKSMARTPLAY");
    }

    private AppPlayerApi() {
    }

    public static native boolean checkPlayerSource(long playerPtr, int sourceType, String path, String[] urls, long clientPtr, long[] values);

    public static native boolean destroy(long playerPtr);

    public static native long createPlayer(long textureId, Surface surface, int width, int height, int x, int y);

    public static native boolean setPlayerSource(long playerPtr, int sourceType, String path, String[] urls, long clientPtr, long[] values);

    public static native void setProgressCallback(AppPlayerProgress progress);

    public static native int save(long playerPtr, String path, int width, int height);

    public static native boolean saveMP4(String inputPath, String outputPath, int width, int height, int frameRate);

    public static native boolean screenshot(long playerPtr, String path, int width, int height, float baseWidth, float baseHeight, int mode);

    public static native boolean start(long playerPtr);

    public static native boolean startDown(long playerPtr, String path);

    public static native boolean stop(long playerPtr);

    public static native boolean stopDown(long playerPtr);

    public static native boolean tsToMP4(String inputPath, String outputPath);

    private static native void setCacheDir(String path);

    private static native String getTmpfileDirPath();

    public static void init(File cacheDir) {
        setCacheDir(cacheDir.getAbsolutePath());
        File tmpfileDir = new File(getTmpfileDirPath());
        try {
            if (tmpfileDir.exists()) {
                tmpfileDir.delete();
            }
            tmpfileDir.mkdirs();
        } catch (Throwable ignored) {
        }
    }
}
