package com.pzy.starfall.mobile;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.ActivityNotFoundException;
import android.content.Intent;
import android.graphics.Color;
import android.os.Bundle;
import android.util.AtomicFile;
import android.util.Log;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;

/** Runs in :privacy, before the Unity activity/process is started. No Unity dependencies. */
public class PrivacyConsentActivity extends Activity {
    public static final String POLICY_VERSION = "2026-09-07-v1";
    public static final String POLICY_URL = "https://pzy2000.github.io/EVE_remake/privacy/";
    private static final String TAG = "StarfallPrivacy";
    private static final String PLAYER = "com.pzy.starfall.mobile.StarfallUnityPlayerActivity";
    private boolean launching;
    private String policyText;

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        getWindow().setStatusBarColor(Color.rgb(8, 23, 44));
        getWindow().setNavigationBarColor(Color.rgb(8, 23, 44));
        TextView background = new TextView(this);
        background.setBackgroundColor(Color.rgb(8, 23, 44));
        setContentView(background);
        try (InputStream input = getAssets().open("starfall-privacy-policy.txt")) {
            ByteArrayOutputStream buffer = new ByteArrayOutputStream();
            byte[] bytes = new byte[4096];
            int count;
            while ((count = input.read(bytes)) != -1) buffer.write(bytes, 0, count);
            policyText = new String(buffer.toByteArray(), StandardCharsets.UTF_8);
            if (!policyText.startsWith("隐私政策版本：" + POLICY_VERSION + "\n")) {
                throw new IOException("Policy version mismatch");
            }
        } catch (IOException exception) {
            new AlertDialog.Builder(this).setTitle("无法读取隐私政策")
                .setMessage("请重新安装完整安装包后再试。")
                .setPositiveButton("退出", (d, w) -> reject())
                .setOnCancelListener(d -> reject()).show();
            return;
        }
        if (hasConsent()) {
            launchPlayer();
            return;
        }
        Log.i(TAG, "CONSENT_REQUIRED");
        showConsent();
    }

    private AtomicFile consentFile() {
        // Consent must not be restored from device/cloud backup on another installation.
        return new AtomicFile(new File(getNoBackupFilesDir(), "starfall-privacy-consent"));
    }

    private boolean hasConsent() {
        try {
            return POLICY_VERSION.equals(new String(consentFile().readFully(), StandardCharsets.UTF_8));
        } catch (IOException | RuntimeException exception) {
            return false; // Missing, corrupt, or inaccessible consent fails closed.
        }
    }

    private boolean saveConsent() {
        AtomicFile file = consentFile();
        FileOutputStream stream = null;
        try {
            stream = file.startWrite();
            stream.write(POLICY_VERSION.getBytes(StandardCharsets.UTF_8));
            file.finishWrite(stream);
            return hasConsent();
        } catch (IOException | RuntimeException exception) {
            file.failWrite(stream);
            Log.e(TAG, "CONSENT_SAVE_FAILED");
            return false;
        }
    }

    private void showConsent() {
        AlertDialog dialog = new AlertDialog.Builder(this)
            .setTitle("隐私保护提示")
            .setMessage("欢迎使用 STARFALL ODYSSEY（pzy的工作室）。\n\n"
                + "在启动游戏引擎前，请阅读《隐私政策》：\n"
                + "• 游戏在本机保存飞行员名称、进度和设置；导入仅读取您主动选择的文件。\n"
                + "• Unity Technologies 提供的 Unity3D 引擎会在启动和运行适配时，通过 Android 接口读取设备、屏幕及特定类型传感器列表，用于图形、输入和兼容适配。\n"
                + "• 本版本核心玩法无需联网，无内购和广告；TapTap/TapPlay 的平台服务适用其独立规则。\n\n"
                + "您可先离线阅读完整政策，了解第三方组件、处理方式、时机及权利。拒绝不会启动游戏引擎。\n"
                + "隐私联系邮箱：pzy2000@sjtu.edu.cn")
            .setNegativeButton("不同意并退出", (d, which) -> reject())
            .setNeutralButton("阅读完整隐私政策", null)
            .setPositiveButton("同意并进入", null)
            .create();
        dialog.setCanceledOnTouchOutside(false);
        dialog.setOnCancelListener(d -> reject());
        dialog.setOnShowListener(d -> {
            dialog.getButton(AlertDialog.BUTTON_NEUTRAL).setOnClickListener(v -> showPolicy());
            dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v -> {
                if (!saveConsent()) {
                    Toast.makeText(this, "无法保存同意记录，请重试或退出。", Toast.LENGTH_LONG).show();
                    return;
                }
                Log.i(TAG, "CONSENT_ACCEPTED " + POLICY_VERSION);
                dialog.dismiss();
                launchPlayer();
            });
        });
        dialog.show();
    }

    private void showPolicy() {
        TextView text = new TextView(this);
        text.setText(policyText);
        text.setTextColor(Color.rgb(25, 43, 61));
        text.setTextSize(16);
        text.setTextIsSelectable(true);
        int padding = (int) (20 * getResources().getDisplayMetrics().density);
        text.setPadding(padding, padding, padding, padding);
        ScrollView scroll = new ScrollView(this);
        scroll.addView(text);
        new AlertDialog.Builder(this).setTitle("STARFALL ODYSSEY 隐私政策")
            .setView(scroll).setPositiveButton("返回提示", (d, w) -> { })
            .show();
        Log.i(TAG, "POLICY_OPENED");
    }

    private void reject() {
        Log.i(TAG, "CONSENT_REJECTED");
        finishAndRemoveTask();
    }

    private void launchPlayer() {
        if (launching || !hasConsent()) return;
        launching = true;
        Intent player = new Intent().setClassName(this, PLAYER);
        player.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        // Only retain the documented Unity graphics switch used by CI, never arbitrary extras.
        String graphics = getIntent().getStringExtra("unity");
        if ("-force-gles30".equals(graphics) || "-force-vulkan".equals(graphics)) {
            player.putExtra("unity", graphics);
        }
        try {
            startActivity(player);
            Log.i(TAG, "PLAYER_STARTED");
            finish();
        } catch (ActivityNotFoundException exception) {
            launching = false;
            Toast.makeText(this, "游戏组件不可用，请重新安装。", Toast.LENGTH_LONG).show();
        }
    }
}
