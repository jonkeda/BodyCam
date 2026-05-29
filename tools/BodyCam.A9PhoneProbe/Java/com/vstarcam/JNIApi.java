package com.vstarcam;

import com.vstarcam.app_p2p_api.ClientCommandListener;
import com.vstarcam.app_p2p_api.ClientReleaseListener;
import com.vstarcam.app_p2p_api.ClientStateListener;

public final class JNIApi {
    static {
        System.loadLibrary("OKSMARTPPCS");
    }

    private JNIApi() {
    }

    public static native boolean changeId(long clientPtr, String deviceId);

    public static native int[] checkBuffer(long clientPtr, int channel);

    public static native int[] checkMode(long clientPtr);

    public static native boolean clientConnectBreak(long clientPtr);

    public static native int clientDevInfo(long clientPtr, String key, String value);

    public static native boolean clientSetVuid(long clientPtr, String vuid);

    public static native int connect(long clientPtr, int connectType, String serverParam, int p2pType);

    public static native long create(String did, String options);

    public static native void destroy(long clientPtr);

    public static native boolean disconnect(long clientPtr);

    public static native boolean init(
            ClientStateListener stateListener,
            ClientCommandListener commandListener,
            ClientReleaseListener releaseListener);

    public static native boolean login(long clientPtr, String user, String password);

    public static native void release(long clientPtr);

    public static native void retain(long clientPtr);

    public static native boolean sendBinaryFile(long clientPtr, String path, int channel);

    public static native int write(long clientPtr, int channel, byte[] payload, int length);

    public static native boolean writeCgi(long clientPtr, String cgi, int channel);

    public static native boolean writeCgiNVR(long clientPtr, String cgi, int channel);
}
