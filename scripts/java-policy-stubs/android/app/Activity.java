package android.app;

import android.view.KeyEvent;

public class Activity {
    public void onWindowFocusChanged(boolean hasFocus) {
    }

    public boolean dispatchKeyEvent(KeyEvent event) {
        return false;
    }

    public boolean onKeyDown(int keyCode, KeyEvent event) {
        return false;
    }

    public boolean onKeyUp(int keyCode, KeyEvent event) {
        return false;
    }

    public void onBackPressed() {
    }
}
