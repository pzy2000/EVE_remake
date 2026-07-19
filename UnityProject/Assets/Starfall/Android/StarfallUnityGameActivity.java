package com.pzy.starfall.mobile;

import android.os.Build;
import android.view.KeyEvent;

import com.unity3d.player.UnityPlayerGameActivity;

/** Unity GameActivity entry point with hardware-key and predictive Back support. */
public final class StarfallUnityGameActivity extends UnityPlayerGameActivity {
    @Override
    public boolean onKeyDown(int keyCode, KeyEvent event) {
        // GameActivity owns a native key buffer and can bypass Activity's normal
        // dispatchKeyEvent/onBackPressed path. Consume the hardware Back down edge
        // on every supported API and dispatch once from the matching key-up callback.
        // Gesture Back on API 33+ remains owned by OnBackInvokedDispatcher.
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
        // key-up path on every supported API. API 33+ Back gestures do not emit
        // this KeyEvent and remain owned by OnBackInvokedDispatcher.
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
