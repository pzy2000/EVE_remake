package com.pzy.starfallodyssey;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Build;
import android.os.Bundle;
import android.text.Html;
import android.text.Spanned;
import android.text.method.LinkMovementMethod;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;

/**
 * First-launch privacy consent screen. Must run BEFORE Unity engine init so the
 * privacy popup precedes any SDK data access (e.g. Unity's sensor list query).
 * UI is built programmatically (no Android resources) so the plugin can ship as a
 * plain jar. The policy body is embedded from APK assets and is scrollable.
 */
public class ConsentActivity extends Activity {

    private static final String PREFS = "privacy_consent";
    private static final String KEY_ACCEPTED = "policy_v1_accepted";
    private static final String POLICY_ASSET = "privacy_policy.html";
    private static final String POLICY_URL = "https://pzy2000.github.io/EVE_remake/";

    private static final int BG = 0xFF0D1520;
    private static final int FG = 0xFFE8EEF6;
    private static final int SUBTLE = 0xFF9AAABB;
    private static final int ACCENT = 0xFF3EC9DF;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        SharedPreferences sp = getSharedPreferences(PREFS, MODE_PRIVATE);
        if (sp.getBoolean(KEY_ACCEPTED, false)) {
            launchUnity();
            return;
        }
        buildUi();
    }

    @Override
    public void onBackPressed() {
        confirmExit();
    }

    private void buildUi() {
        int pad = dp(20);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(BG);
        root.setPadding(pad, dp(16), pad, dp(14));

        TextView title = new TextView(this);
        title.setText("STARFALL ODYSSEY");
        title.setTextColor(ACCENT);
        title.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        title.setLetterSpacing(0.2f);
        title.setTypeface(Typeface.DEFAULT_BOLD);

        TextView heading = new TextView(this);
        heading.setText("欢迎！请先阅读《隐私政策》");
        heading.setTextColor(FG);
        heading.setTextSize(TypedValue.COMPLEX_UNIT_SP, 20);
        heading.setTypeface(Typeface.DEFAULT_BOLD);
        heading.setPadding(0, dp(6), 0, dp(2));

        TextView intro = new TextView(this);
        intro.setText(Html.fromHtml(
                "根据法律法规要求，启动游戏前请阅读并同意以下隐私政策要点。"
                        + "全文已内嵌于下方，可滚动阅读；网页版请点击 "
                        + "<a href=\"" + POLICY_URL + "\">" + POLICY_URL + "</a>"));
        intro.setTextColor(SUBTLE);
        intro.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        intro.setMovementMethod(LinkMovementMethod.getInstance());
        intro.setHighlightColor(0x333EC9DF);
        intro.setPadding(0, dp(4), 0, dp(8));

        TextView policy = new TextView(this);
        policy.setText(Html.fromHtml(loadPolicy()));
        policy.setTextColor(FG);
        policy.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
        policy.setLineSpacing(dp(3), 1f);
        policy.setMovementMethod(LinkMovementMethod.getInstance());
        policy.setHighlightColor(0x333EC9DF);
        policy.setTextIsSelectable(false);

        ScrollView scroller = new ScrollView(this);
        scroller.addView(policy);
        scroller.setFillViewport(true);
        scroller.setClipToPadding(false);
        scroller.setPadding(0, dp(4), 0, 0);

        TextView footer = new TextView(this);
        footer.setText("点击「同意并继续」即表示您已阅读、理解并同意上述《隐私政策》全部内容，并同意游戏为运行目的在设备上处理所述信息。");
        footer.setTextColor(SUBTLE);
        footer.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
        footer.setPadding(0, dp(10), 0, dp(10));

        LinearLayout buttons = new LinearLayout(this);
        buttons.setOrientation(LinearLayout.HORIZONTAL);
        LinearLayout.LayoutParams split =
                new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f);
        split.setMargins(0, 0, dp(6), 0);
        Button decline = makeButton("不同意并退出");
        decline.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { confirmExit(); }
        });
        Button agree = makeButton("同意并继续");
        agree.setTextColor(0xFF08222B);
        agree.setBackground(rounded(ACCENT));
        agree.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { accept(); }
        });
        buttons.addView(decline, split);
        LinearLayout.LayoutParams split2 =
                new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f);
        split2.setMargins(dp(6), 0, 0, 0);
        buttons.addView(agree, split2);

        root.addView(title);
        root.addView(heading);
        root.addView(intro);
        root.addView(scroller,
                new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        root.addView(footer);
        root.addView(buttons);
        setContentView(root);
    }

    private Button makeButton(String label) {
        Button b = new Button(this, null, 0);
        b.setText(label);
        b.setAllCaps(false);
        b.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15);
        b.setTextColor(FG);
        b.setBackground(rounded(0xFF1C2B3D));
        b.setPadding(0, dp(12), 0, dp(12));
        return b;
    }

    private android.graphics.drawable.Drawable rounded(int color) {
        GradientDrawable d = new GradientDrawable();
        d.setColor(color);
        d.setCornerRadius(dp(10));
        return d;
    }

    private void accept() {
        getSharedPreferences(PREFS, MODE_PRIVATE)
                .edit().putBoolean(KEY_ACCEPTED, true).apply();
        launchUnity();
    }

    private void confirmExit() {
        new AlertDialog.Builder(this)
                .setTitle("提示")
                .setMessage("同意《隐私政策》后才能进入游戏。确定要退出吗？")
                .setPositiveButton("退出", new DialogInterface.OnClickListener() {
                    @Override public void onClick(DialogInterface d, int w) { finishAffinity(); }
                })
                .setNegativeButton("重新查看", null)
                .show();
    }

    private void launchUnity() {
        startActivity(new Intent()
                .setClassName(getPackageName(), "com.unity3d.player.UnityPlayerGameActivity"));
        finish();
    }

    private String loadPolicy() {
        StringBuilder sb = new StringBuilder();
        try (InputStream in = getAssets().open(POLICY_ASSET);
             BufferedReader r = new BufferedReader(new InputStreamReader(in, StandardCharsets.UTF_8))) {
            String line;
            while ((line = r.readLine()) != null) sb.append(line).append('\n');
        } catch (Exception e) {
            return "加载《隐私政策》正文失败，请访问 " + POLICY_URL + " 查看全文。";
        }
        return sb.toString();
    }

    private int dp(int v) {
        return Math.round(v * getResources().getDisplayMetrics().density);
    }
}
