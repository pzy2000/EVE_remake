package com.pzy.starfall.mobile;

import android.os.Build;
import android.view.KeyEvent;

import com.unity3d.player.UnityPlayerGameActivity;

/** Unity GameActivity entry point with an explicit API 26-32 Back compatibility path. */
public final class StarfallUnityGameActivity extends UnityPlayerGameActivity {
    @Override
    public boolean onKeyDown(int keyCode, KeyEvent event) {
        // GameActivity owns a native key buffer and can bypass Activity's normal
        // dispatchKeyEvent/onBackPressed path. Consume the legacy Back down edge
        // here and dispatch exactly once from the matching key-up callback.
        if (CompatBackPolicy.shouldHandleWithActivityCallback(Build.VERSION.SDK_INT)
                && keyCode == KeyEvent.KEYCODE_BACK
                && event.getRepeatCount() == 0) {
            return true;
        }
        return super.onKeyDown(keyCode, event);
    }

    @Override
    public boolean onKeyUp(int keyCode, KeyEvent event) {
        if (CompatBackPolicy.shouldHandleWithActivityCallback(Build.VERSION.SDK_INT)
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
        // Activity.onBackPressed() can observe them. Intercept only the legacy
        // Back key-up path; API 33+ remains owned by OnBackInvokedDispatcher.
        if (CompatBackPolicy.shouldHandleWithActivityCallback(Build.VERSION.SDK_INT)
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
