#!/usr/bin/env bash
# Compile the production native privacy launcher against Android. The protected
# player is a clearly labelled test double, NOT a Unity release build.
set -Eeuo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
sdk="${ANDROID_SDK_ROOT:?Set ANDROID_SDK_ROOT}"
out="${1:-$root/artifacts/privacy-test}"
mkdir -p "$out/src/com/pzy/starfall/mobile" "$out/classes" "$out/dex"
out="$(cd "$out" && pwd)"
test_keystore="${STARFALL_PRIVACY_TEST_KEYSTORE:-$root/artifacts/privacy-test/test-key.jks}"
mkdir -p "$(dirname "$test_keystore")"
bt="$sdk/build-tools/36.0.0"
android="$sdk/platforms/android-36/android.jar"
cat > "$out/src/com/pzy/starfall/mobile/StarfallUnityPlayerActivity.java" <<'JAVA'
package com.pzy.starfall.mobile;
public final class StarfallUnityPlayerActivity extends android.app.Activity {
  @Override public void onCreate(android.os.Bundle b) {
    super.onCreate(b);
    android.util.Log.i("StarfallPrivacyTest", "PLAYER_TEST_DOUBLE_STARTED");
    android.widget.TextView t = new android.widget.TextView(this);
    t.setText("PLAYER_TEST_DOUBLE_STARTED — consent gate test only");
    setContentView(t);
  }
}
JAVA
cat > "$out/AndroidManifest.xml" <<'XML'
<manifest xmlns:android="http://schemas.android.com/apk/res/android" package="com.pzy.starfall.privacytest" android:versionCode="1" android:versionName="test-only">
<uses-sdk android:minSdkVersion="26" android:targetSdkVersion="36"/>
<application android:label="STARFALL Privacy Test" android:debuggable="true" android:theme="@android:style/Theme.Material.Light.NoActionBar">
<activity android:name="com.pzy.starfall.mobile.StarfallUnityGameActivity" android:process=":privacy" android:exported="true" android:screenOrientation="landscape">
<intent-filter><action android:name="android.intent.action.MAIN"/><category android:name="android.intent.category.LAUNCHER"/></intent-filter>
</activity>
<activity android:name="com.pzy.starfall.mobile.StarfallUnityPlayerActivity" android:exported="false"/>
</application></manifest>
XML
native="$root/UnityProject/Assets/Plugins/Android/StarfallMobile.androidlib/src/main"
launcher_source="$root/UnityProject/Assets/Starfall/Android/StarfallUnityGameActivity.java"
if [[ "${2:-}" == "legacy" ]]; then
  launcher_source="$out/src/com/pzy/starfall/mobile/StarfallUnityGameActivity.java"
  cat > "$launcher_source" <<'JAVA_LEGACY'
package com.pzy.starfall.mobile;
public final class StarfallUnityGameActivity extends android.app.Activity {
    @Override public void onCreate(android.os.Bundle b) {
        super.onCreate(b);
        android.widget.TextView t = new android.widget.TextView(this);
        t.setText("LEGACY_TEST_DOUBLE_STARTED");
        setContentView(t);
    }
}
JAVA_LEGACY
  python3 - "$out/AndroidManifest.xml" <<'PY_LEGACY'
import sys, pathlib
p=pathlib.Path(sys.argv[1]);p.write_text(p.read_text().replace(' android:process=":privacy"',''))
PY_LEGACY
fi
javac -source 8 -target 8 -Xlint:-options -cp "$android" -d "$out/classes" \
 "$native/java/com/pzy/starfall/mobile/PrivacyConsentActivity.java" \
 "$launcher_source" \
 "$out/src/com/pzy/starfall/mobile/StarfallUnityPlayerActivity.java"
jar cf "$out/classes.jar" -C "$out/classes" .
"$bt/d8" --lib "$android" --min-api 26 --output "$out/dex" "$out/classes.jar"
"$bt/aapt2" link --manifest "$out/AndroidManifest.xml" -I "$android" \
 -A "$native/assets" -o "$out/unsigned.apk"
(cd "$out/dex" && zip -q -j "$out/unsigned.apk" classes.dex)
"$bt/zipalign" -f 4 "$out/unsigned.apk" "$out/aligned.apk"
if [[ ! -f "$test_keystore" ]]; then
 keytool -genkeypair -keystore "$test_keystore" -storepass android -keypass android \
  -alias test -keyalg RSA -validity 30 -dname 'CN=STARFALL Consent Test Only' >/dev/null 2>&1
fi
"$bt/apksigner" sign --ks "$test_keystore" --ks-pass pass:android --ks-key-alias test \
 --out "$out/privacy-test.apk" "$out/aligned.apk"
"$bt/apksigner" verify "$out/privacy-test.apk"
echo "Test-only APK: $out/privacy-test.apk"
