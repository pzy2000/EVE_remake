package android.view;

public final class KeyEvent {
    public static final int ACTION_UP = 1;
    public static final int KEYCODE_BACK = 4;

    public int getAction() {
        return ACTION_UP;
    }

    public int getKeyCode() {
        return KEYCODE_BACK;
    }

    public int getRepeatCount() {
        return 0;
    }

    public boolean isCanceled() {
        return false;
    }
}
