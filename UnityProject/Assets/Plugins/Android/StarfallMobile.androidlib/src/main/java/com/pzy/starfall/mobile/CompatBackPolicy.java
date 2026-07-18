package com.pzy.starfall.mobile;

/** Keeps legacy Activity back handling disjoint from Android 13 predictive back. */
final class CompatBackPolicy {
    private static final int MINIMUM_SUPPORTED_SDK = 26;
    private static final int PREDICTIVE_BACK_SDK = 33;

    private CompatBackPolicy() {
    }

    static boolean shouldHandleWithActivityCallback(int sdkInt) {
        return sdkInt >= MINIMUM_SUPPORTED_SDK && sdkInt < PREDICTIVE_BACK_SDK;
    }
}
