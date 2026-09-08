package com.pzy.starfall.mobile;

import android.os.Build;
import android.view.KeyEvent;

import com.unity3d.player.UnityPlayerGameActivity;

/** Unity GameActivity entry point with hardware-key and predictive Back support. */
public final class StarfallUnityPlayerActivity extends UnityPlayerGameActivity {
    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        StarfallMobileBridge.onUnityActivityWindowFocusChanged(this, hasFocus);
    }

    @Override
    public boolean onKeyDown(int keyCode, KeyEvent event) {
        // GameActivity owns a native key buffer and can bypass Activity's normal
        // dispatchKeyEvent/onBackPressed path. Consume the hardware Back down edge
        // on API 26-32 and dispatch once from the matching key-up callback.
        // On API 33+, OnBackInvokedDispatcher owns both gestures and Back keys;
        // intercepting the KeyEvent there would dispatch the same action twice.
        if (CompatBackPolicy.shouldHandleKeyEvent(Build.VERSION.SDK_INT)
                && keyCode == KeyEvent.KEYCODE_BACK
                && event.getRepeatCount() == 0) {
            return true;
        }
        return super.onKeyDown(keyCode, event);
    }

    @Override
    public boolean onKeyUp(int keyCode, KeyEvent event) {
        if (CompatBackPolicy.shouldHandleKeyEvent(Build.VERSION.SDK_INT)
                && keyCode == KeyEvent.KEYCODE_BACK
                && event.getRepeatCount() == 0
                && !event.isCanceled()
                && StarfallMobileBridge.dispatchCompatBack(this)) {
            return true;
        }
        return super.onKeyUp(keyCode, event);
    }

    @Override
    public boolean dispatchKeyEvent(KeyEvent event) {
        // Unity GameActivity forwards hardware keys to its native input queue before
        // Activity.onBackPressed() can observe them. Intercept the hardware Back
        // key-up path on API 26-32. API 33+ Back keys and gestures remain owned
        // by OnBackInvokedDispatcher so the managed router receives one action.
        if (CompatBackPolicy.shouldHandleKeyEvent(Build.VERSION.SDK_INT)
                && event.getKeyCode() == KeyEvent.KEYCODE_BACK
                && event.getAction() == KeyEvent.ACTION_UP
                && event.getRepeatCount() == 0
                && StarfallMobileBridge.dispatchCompatBack(this)) {
            return true;
        }
        return super.dispatchKeyEvent(event);
    }

    @Override
    @SuppressWarnings("deprecation")
    public void onBackPressed() {
        if (CompatBackPolicy.shouldHandleWithActivityCallback(Build.VERSION.SDK_INT)
                && StarfallMobileBridge.dispatchCompatBack(this)) {
            return;
        }
        super.onBackPressed();
    }
}
