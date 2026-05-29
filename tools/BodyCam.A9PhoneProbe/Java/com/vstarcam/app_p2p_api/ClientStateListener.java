package com.vstarcam.app_p2p_api;

public interface ClientStateListener {
    void stateListener(long clientPtr, int state);
}
