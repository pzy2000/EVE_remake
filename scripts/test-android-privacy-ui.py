#!/usr/bin/env python3
"""Exercise production native consent with a protected player test double on an emulator."""
import argparse, json, os, pathlib, re, subprocess, time, xml.etree.ElementTree as E
p=argparse.ArgumentParser();p.add_argument('--apk',required=True);p.add_argument('--legacy-apk');p.add_argument('--output',required=True);p.add_argument('--serial',default='emulator-5554');args=p.parse_args()
out=pathlib.Path(args.output);out.mkdir(parents=True,exist_ok=True)
adb=str(pathlib.Path(os.environ['ANDROID_SDK_ROOT'])/'platform-tools/adb')
pkg='com.pzy.starfall.privacytest';launch=pkg+'/com.pzy.starfall.mobile.StarfallUnityGameActivity';player=pkg+'/com.pzy.starfall.mobile.StarfallUnityPlayerActivity'
def call(*cmd,check=True):
 r=subprocess.run([adb,'-s',args.serial,*cmd],capture_output=True,check=False)
 if check and r.returncode:raise AssertionError(r.stderr.decode(errors='replace'))
 return (r.stdout+r.stderr).decode(errors='replace')
def shell(*cmd,check=True):return call('shell',*cmd,check=check)
def ui():
 shell('uiautomator','dump','/sdcard/privacy-test-ui.xml',check=False)
 return E.fromstring(shell('cat','/sdcard/privacy-test-ui.xml'))
def wait_text(text):
 for _ in range(20):
  try:
   r=ui()
   if any(text in n.get('text','') for n in r.iter('node')):return r
  except (E.ParseError,AssertionError):pass
  time.sleep(.3)
 raise AssertionError('UI did not contain '+text)
def tap(text):
 r=wait_text(text)
 for n in r.iter('node'):
  if n.get('text')==text:
   b=list(map(int,re.findall(r'\d+',n.get('bounds',''))));assert len(b)==4
   shell('input','tap',str((b[0]+b[2])//2),str((b[1]+b[3])//2));return
 raise AssertionError('Exact control missing: '+text)
def shot(name):
 (out/(name+'.png')).write_bytes(subprocess.check_output([adb,'-s',args.serial,'exec-out','screencap','-p']))
 (out/(name+'.xml')).write_text(E.tostring(ui(),encoding='unicode'))
launch_time = 0.0
def no_player():
 assert not shell('pidof',pkg,check=False).strip(),'Main/player process started without consent'
 assert 'PLAYER_TEST_DOUBLE_STARTED' not in shell('logcat','-d','-s','StarfallPrivacyTest')
def start():
 global launch_time
 launch_time=float(shell('date','+%s.%N').strip())
 shell('am','start','-W','-n',launch)
def stop():shell('am','force-stop',pkg)
def reset():stop();shell('pm','clear',pkg);shell('logcat','-c')
for _ in range(60):
 if shell('getprop','sys.boot_completed',check=False).strip()=='1':break
 time.sleep(1)
else:raise AssertionError('Emulator failed to boot')
call('install','-r',args.apk)
results=[]
def passed(s):results.append(s);print('PASS',s,flush=True)
if args.legacy_apk:
 reset();call('install','-r',args.legacy_apk);start();wait_text('LEGACY_TEST_DOUBLE_STARTED')
 call('install','-r',args.apk);shell('logcat','-c');start();wait_text('隐私保护提示');no_player()
 passed('upgrade retaining the old launcher class requires new consent')
reset();start();wait_text('隐私保护提示');no_player();shot('01-first-launch');passed('fresh launch isolates the player process')
tap('阅读完整隐私政策');wait_text('STARFALL ODYSSEY 隐私政策');no_player();shot('02-offline-policy');tap('返回提示');no_player();passed('offline policy reading does not grant consent')
tap('不同意并退出');time.sleep(.5);no_player();start();wait_text('隐私保护提示');no_player();passed('reject exits and re-prompts next launch')
shell('input','keyevent','4');time.sleep(.5);no_player();start();wait_text('隐私保护提示');passed('Back does not grant consent')
blocked=shell('am','start','-W','-n',player,check=False);assert 'Permission Denial' in blocked or 'not exported' in blocked;no_player();passed('external explicit Unity activity launch is denied')
tap('同意并进入');wait_text('PLAYER_TEST_DOUBLE_STARTED');shot('03-accepted');assert shell('pidof',pkg,check=False).strip();passed('explicit acceptance starts protected player')
stop();shell('logcat','-c');start();wait_text('PLAYER_TEST_DOUBLE_STARTED');assert 'CONSENT_REQUIRED' not in shell('logcat','-d','-s','StarfallPrivacy');passed('cold relaunch remembers current policy')
# Mutate only the isolated test package to emulate policy version changes/corruption.
stop();shell('run-as',pkg,'sh','-c',"'printf stale > no_backup/starfall-privacy-consent'");shell('logcat','-c');start();wait_text('隐私保护提示');no_player();passed('stale policy consent cannot start player')
reset();shell('run-as',pkg,'mkdir','-p','no_backup');shell('run-as',pkg,'chmod','500','no_backup');start();wait_text('隐私保护提示');tap('同意并进入');time.sleep(.5);no_player();wait_text('隐私保护提示');passed('consent persistence failure keeps gate closed')
reset();start();wait_text('隐私保护提示');no_player();passed('clearing app data requires consent again')
(out/'result.json').write_text(json.dumps({'passed':results,'count':len(results),'scope':'production native launcher with player test double; NOT full Unity or TapTap compliance certification'},ensure_ascii=False,indent=2))
(out/'logcat.txt').write_text(shell('logcat','-d'))
stop()
