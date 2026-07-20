package com.pzy.starfall.mobile;

final class StarfallMobileBridge {
    private StarfallMobileBridge() {
    }

    static boolean dispatchCompatBack(Object activity) {
        return activity != null;
    }

    static void onUnityActivityWindowFocusChanged(Object activity, boolean hasFocus) {
    }
}
