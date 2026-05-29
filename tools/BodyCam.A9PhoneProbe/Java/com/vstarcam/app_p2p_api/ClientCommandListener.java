package com.vstarcam.app_p2p_api;

public interface ClientCommandListener {
    void commandListener(long clientPtr, byte[] payload, int type);
}
