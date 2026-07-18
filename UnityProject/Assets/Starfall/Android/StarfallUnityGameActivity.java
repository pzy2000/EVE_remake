package com.pzy.starfall.mobile;

import android.os.Build;

import com.unity3d.player.UnityPlayerGameActivity;

/** Unity GameActivity entry point with an explicit API 26-32 Back compatibility path. */
public final class StarfallUnityGameActivity extends UnityPlayerGameActivity {
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
