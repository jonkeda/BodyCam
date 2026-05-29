package com.bodycam.a9phoneprobe;

import android.graphics.SurfaceTexture;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;
import android.view.Surface;

import com.veepai.AppPlayerApi;
import com.vstarcam.JNIApi;
import com.vstarcam.app_p2p_api.ClientCommandListener;
import com.vstarcam.app_p2p_api.ClientReleaseListener;
import com.vstarcam.app_p2p_api.ClientStateListener;

import java.io.File;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.atomic.AtomicReference;

public final class PpcsProbeBridge implements ClientStateListener, ClientCommandListener, ClientReleaseListener, AppPlayerApi.AppPlayerProgress {
    private static final String TAG = "A9PPCS";
    private static final int LIVE_SOURCE = 1;

    private final StringBuilder log = new StringBuilder();
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private int progressEvents;
    private int headEvents;
    private int gpsEvents;
    private int drawEvents;

    public static String runProbe(String clientId, String vuid, String serverParam, String user, String password, String cacheDir) {
        return new PpcsProbeBridge().run(clientId, vuid, serverParam, user, password, cacheDir);
    }

    private String run(String clientId, String vuid, String serverParam, String user, String password, String cacheDir) {
        long clientPtr = 0;
        line("PPCS bridge started.");
        line("clientId=" + safe(clientId) + " vuid=" + safe(vuid) + " serverLen=" + lengthOf(serverParam));

        load("c++_shared");
        load("vp_log");
        load("yuv");
        load("OKSMARTSHENGYIN");
        load("OKSMARTJIAMI");
        load("OKSMARTPPCS");
        load("OKSMARTPLAY");

        try {
            boolean init = onMain(new MainCall<Boolean>() {
                @Override
                public Boolean run() {
                    return JNIApi.init(PpcsProbeBridge.this, PpcsProbeBridge.this, PpcsProbeBridge.this);
                }
            });
            line("JNIApi.init=" + init);

            clientPtr = onMain(new MainCall<Long>() {
                @Override
                public Long run() {
                    return JNIApi.create(clientId, null);
                }
            });
            line("JNIApi.create=" + clientPtr);
            if (clientPtr == 0) {
                return log.toString();
            }

            final long ptrForCalls = clientPtr;
            if (vuid != null && vuid.length() > 0) {
                retain(ptrForCalls, "clientSetVuid");
                boolean setVuid = JNIApi.clientSetVuid(clientPtr, vuid);
                line("JNIApi.clientSetVuid=" + setVuid);
            }

            retain(ptrForCalls, "connect");
            int connect = JNIApi.connect(clientPtr, 0x3F, serverParam, 1);
            line("JNIApi.connect=" + connect + " connectType=0x3F p2pType=1");
            sleep(1200);

            if (user != null && password != null) {
                retain(ptrForCalls, "login");
                boolean login = JNIApi.login(clientPtr, user, password);
                line("JNIApi.login=" + login);
            }

            checkMode(clientPtr);
            tryLiveCgiOpen(clientPtr);
            runPlayerProbe(clientPtr, cacheDir);
            for (int i = 0; i < 8; i++) {
                checkBuffer(clientPtr, 1, i);
                sleep(500);
            }
        } catch (Throwable t) {
            line("PPCS exception=" + describe(t));
        } finally {
            if (clientPtr != 0) {
                try {
                    retain(clientPtr, "disconnect");
                    line("JNIApi.disconnect=" + JNIApi.disconnect(clientPtr));
                } catch (Throwable t) {
                    line("disconnect exception=" + describe(t));
                }

                try {
                    final long ptrToDestroy = clientPtr;
                    onMain(new MainCall<Object>() {
                        @Override
                        public Object run() {
                            JNIApi.destroy(ptrToDestroy);
                            return null;
                        }
                    });
                    line("JNIApi.destroy=done");
                } catch (Throwable t) {
                    line("destroy exception=" + describe(t));
                }
            }
        }

        return log.toString();
    }

    private void tryLiveCgiOpen(long clientPtr) {
        String cgi = "livestream.cgi?streamid=10&substream=0&";
        try {
            retain(clientPtr, "writeCgi live channel 1");
            boolean result = JNIApi.writeCgi(clientPtr, cgi, 1);
            line("JNIApi.writeCgi live channel=1 cgi=" + cgi + " result=" + result);
            sleep(700);
            checkBuffer(clientPtr, 1, 90);
        } catch (Throwable t) {
            line("writeCgi live exception=" + describe(t));
        }
    }

    private void runPlayerProbe(final long clientPtr, String cacheDir) {
        line("Player metadata probe:");
        SurfaceTexture texture = null;
        Surface surface = null;
        long playerPtr = 0;

        try {
            final File cache = new File(cacheDir == null || cacheDir.length() == 0 ? "/data/local/tmp" : cacheDir);
            onMain(new MainCall<Object>() {
                @Override
                public Object run() {
                    AppPlayerApi.init(cache);
                    AppPlayerApi.setProgressCallback(PpcsProbeBridge.this);
                    return null;
                }
            });
            line("AppPlayerApi.init/setProgressCallback=done cache=" + cache.getAbsolutePath());

            texture = new SurfaceTexture(0);
            texture.setDefaultBufferSize(640, 480);
            surface = new Surface(texture);

            final Surface surfaceForCreate = surface;
            playerPtr = onMain(new MainCall<Long>() {
                @Override
                public Long run() {
                    return AppPlayerApi.createPlayer(990001L, surfaceForCreate, 640, 480, 0, 0);
                }
            });
            line("AppPlayerApi.createPlayer=" + playerPtr);
            if (playerPtr == 0) {
                return;
            }

            boolean sourceAlreadyOpen = AppPlayerApi.checkPlayerSource(playerPtr, LIVE_SOURCE, null, null, clientPtr, null);
            line("AppPlayerApi.checkPlayerSource live=" + sourceAlreadyOpen);

            boolean setSource = AppPlayerApi.setPlayerSource(playerPtr, LIVE_SOURCE, null, null, clientPtr, null);
            line("AppPlayerApi.setPlayerSource live clientPtr=" + clientPtr + " result=" + setSource);

            boolean start = AppPlayerApi.start(playerPtr);
            line("AppPlayerApi.start=" + start);

            for (int i = 0; i < 8; i++) {
                sleep(1000);
                line("player wait[" + i + "] callbacks progress=" + progressEvents + " head=" + headEvents + " draw=" + drawEvents + " gps=" + gpsEvents);
                if (i == 2 || i == 5) {
                    checkBuffer(clientPtr, 1, 100 + i);
                }
            }
        } catch (Throwable t) {
            line("Player probe exception=" + describe(t));
        } finally {
            if (playerPtr != 0) {
                try {
                    line("AppPlayerApi.stop=" + AppPlayerApi.stop(playerPtr));
                } catch (Throwable t) {
                    line("AppPlayerApi.stop exception=" + describe(t));
                }

                try {
                    line("AppPlayerApi.destroy=" + AppPlayerApi.destroy(playerPtr));
                } catch (Throwable t) {
                    line("AppPlayerApi.destroy exception=" + describe(t));
                }
            }

            if (surface != null) {
                surface.release();
            }

            if (texture != null) {
                texture.release();
            }
        }
    }

    private void checkMode(long clientPtr) {
        try {
            retain(clientPtr, "checkMode");
            int[] mode = JNIApi.checkMode(clientPtr);
            line("JNIApi.checkMode=" + ints(mode));
        } catch (Throwable t) {
            line("checkMode exception=" + describe(t));
        }
    }

    private void checkBuffer(long clientPtr, int channel, int attempt) {
        try {
            retain(clientPtr, "checkBuffer[" + attempt + "]");
            int[] buffer = JNIApi.checkBuffer(clientPtr, channel);
            line("JNIApi.checkBuffer[" + attempt + "] channel=" + channel + " result=" + ints(buffer));
        } catch (Throwable t) {
            line("checkBuffer[" + attempt + "] exception=" + describe(t));
        }
    }

    private void load(String name) {
        try {
            System.loadLibrary(name);
            line("load " + name + "=ok");
        } catch (Throwable t) {
            line("load " + name + "=" + describe(t));
        }
    }

    @Override
    public void stateListener(long clientPtr, int state) {
        line("stateListener clientPtr=" + clientPtr + " state=" + state);
    }

    @Override
    public void commandListener(long clientPtr, byte[] payload, int type) {
        line("commandListener clientPtr=" + clientPtr + " type=" + type + " len=" + lengthOf(payload) + " prefix=" + hex(payload, 24));
    }

    @Override
    public void releaseListener(long clientPtr) {
        line("releaseListener clientPtr=" + clientPtr);
        final long ptrToRelease = clientPtr;
        mainHandler.post(new Runnable() {
            @Override
            public void run() {
                try {
                    JNIApi.release(ptrToRelease);
                    line("releaseListener release=done");
                } catch (Throwable t) {
                    line("releaseListener exception=" + describe(t));
                }
            }
        });
    }

    @Override
    public void app_player_draw_info(long textureId, int width, int height, int drawType, float x1, float y1, float x2, float y2) {
        drawEvents++;
        if (drawEvents <= 12 || drawEvents % 30 == 0) {
            line("app_player_draw_info textureId=" + textureId + " width=" + width + " height=" + height + " drawType=" + drawType);
        }
    }

    @Override
    public void app_player_gps_info(long textureId, int type, int value, float x1, float y1, float x2, float y2) {
        gpsEvents++;
        if (gpsEvents <= 12 || gpsEvents % 30 == 0) {
            line("app_player_gps_info textureId=" + textureId + " type=" + type + " value=" + value);
        }
    }

    @Override
    public void app_player_head_info(long textureId, int type, int width, int height) {
        headEvents++;
        if (headEvents <= 24 || headEvents % 30 == 0) {
            line("app_player_head_info textureId=" + textureId + " type=" + type + " width=" + width + " height=" + height);
        }
    }

    @Override
    public void app_player_progress(long textureId, int status, int current, int total, int width, int height, int videoType, int frameRate, long position, long duration) {
        progressEvents++;
        if (progressEvents <= 24 || progressEvents % 30 == 0) {
            line("app_player_progress textureId=" + textureId + " status=" + status + " current=" + current + " total=" + total + " width=" + width + " height=" + height + " videoType=" + videoType + " frameRate=" + frameRate + " position=" + position + " duration=" + duration);
        }
    }

    private synchronized void line(String value) {
        log.append(value).append('\n');
        Log.i(TAG, value);
    }

    private void retain(final long clientPtr, String operation) {
        try {
            onMain(new MainCall<Object>() {
                @Override
                public Object run() {
                    JNIApi.retain(clientPtr);
                    return null;
                }
            });
            line("JNIApi.retain before " + operation + "=done");
        } catch (Throwable t) {
            line("retain before " + operation + " exception=" + describe(t));
        }
    }

    private <T> T onMain(final MainCall<T> call) throws Throwable {
        if (Looper.getMainLooper().getThread() == Thread.currentThread()) {
            return call.run();
        }

        final CountDownLatch latch = new CountDownLatch(1);
        final AtomicReference<T> result = new AtomicReference<>();
        final AtomicReference<Throwable> error = new AtomicReference<>();
        mainHandler.post(new Runnable() {
            @Override
            public void run() {
                try {
                    result.set(call.run());
                } catch (Throwable t) {
                    error.set(t);
                } finally {
                    latch.countDown();
                }
            }
        });
        latch.await();
        if (error.get() != null) {
            throw error.get();
        }
        return result.get();
    }

    private interface MainCall<T> {
        T run() throws Throwable;
    }

    private static void sleep(long millis) {
        try {
            Thread.sleep(millis);
        } catch (InterruptedException ex) {
            Thread.currentThread().interrupt();
        }
    }

    private static String safe(String value) {
        return value == null ? "<null>" : value;
    }

    private static int lengthOf(String value) {
        return value == null ? 0 : value.length();
    }

    private static int lengthOf(byte[] value) {
        return value == null ? 0 : value.length;
    }

    private static String ints(int[] values) {
        if (values == null) {
            return "<null>";
        }

        StringBuilder builder = new StringBuilder("[");
        for (int i = 0; i < values.length; i++) {
            if (i > 0) {
                builder.append(',');
            }
            builder.append(values[i]);
        }
        return builder.append(']').toString();
    }

    private static String hex(byte[] values, int max) {
        if (values == null) {
            return "<null>";
        }

        int count = Math.min(values.length, max);
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < count; i++) {
            if (i > 0) {
                builder.append(' ');
            }
            int value = values[i] & 0xFF;
            if (value < 0x10) {
                builder.append('0');
            }
            builder.append(Integer.toHexString(value).toUpperCase());
        }
        return builder.toString();
    }

    private static String describe(Throwable t) {
        return t.getClass().getSimpleName() + ": " + t.getMessage();
    }
}
