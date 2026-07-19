package com.pzy.starfall.mobile;

/** Keeps hardware-key and legacy Activity Back paths explicit across API levels. */
final class CompatBackPolicy {
    private static final int MINIMUM_SUPPORTED_SDK = 26;
    private static final int PREDICTIVE_BACK_SDK = 33;

    private CompatBackPolicy() {
    }

    static boolean shouldHandleWithActivityCallback(int sdkInt) {
        return sdkInt >= MINIMUM_SUPPORTED_SDK && sdkInt < PREDICTIVE_BACK_SDK;
    }

    static boolean shouldHandleKeyEvent(int sdkInt) {
        return sdkInt >= MINIMUM_SUPPORTED_SDK;
    }
}
