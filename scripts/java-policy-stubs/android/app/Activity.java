package android.app;

import android.view.KeyEvent;

public class Activity {
    public boolean dispatchKeyEvent(KeyEvent event) {
        return false;
    }

    public void onBackPressed() {
    }
}
