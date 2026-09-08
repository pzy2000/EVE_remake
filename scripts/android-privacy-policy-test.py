#!/usr/bin/env python3
"""Check the production manifest boundary; runtime tests exercise consent on Android."""
import pathlib
import xml.etree.ElementTree as ET
r = pathlib.Path(__file__).resolve().parents[1]
n = '{http://schemas.android.com/apk/res/android}'
p = r/'UnityProject/Assets/Plugins/Android'
root = ET.parse(p/'AndroidManifest.xml').getroot()
a = {x.get(n+'name'): x for x in root.findall('./application/activity')}
launcher = a['com.pzy.starfall.mobile.StarfallUnityGameActivity']
player = a['com.pzy.starfall.mobile.StarfallUnityPlayerActivity']
assert launcher.get(n+'process') == ':privacy'
assert launcher.get(n+'exported') == 'true'
assert player.get(n+'exported') == 'false'
assert not player.findall('intent-filter')
assert not launcher.findall('meta-data')
assert len(root.findall('.//category[@'+n+'name="android.intent.category.LAUNCHER"]')) == 1
source = (r/'UnityProject/Assets/Starfall/Android/StarfallUnityGameActivity.java').read_text()
assert 'extends PrivacyConsentActivity' in source and 'UnityPlayerGameActivity' not in source
policy = (p/'StarfallMobile.androidlib/src/main/assets/starfall-privacy-policy.txt').read_text()
assert policy.startswith('隐私政策版本：2026-09-07-v1\n')
assert 'pzy2000@sjtu.edu.cn' in policy and '传感器列表' in policy
print('Privacy launcher isolation and offline-policy contract passed.')
