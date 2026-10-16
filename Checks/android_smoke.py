"""Native Android smoke checks. Refuses to run on a physical phone."""
import os, re, subprocess, time, xml.etree.ElementTree as ET
from pathlib import Path

SERIAL = os.environ.get('AURA_TEST_SERIAL', 'emulator-5554')
OUTPUT = Path(os.environ.get('AURA_TEST_OUTPUT', 'android-smoke-output'))
OUTPUT.mkdir(parents=True, exist_ok=True)
PACKAGE = 'za.co.auraone.assistant'
COMPONENT = PACKAGE + '/' + PACKAGE + '.MainActivity'

def adb(*args):
    return subprocess.check_output(['adb', '-s', SERIAL, *args], text=True, stderr=subprocess.STDOUT, timeout=35)

if not SERIAL.startswith('emulator-') or adb('shell', 'getprop', 'ro.kernel.qemu').strip() != '1':
    raise SystemExit('Smoke tests require an emulator, never a physical phone')

def dump(name='current'):
    for attempt in range(3):
        try:
            adb('shell', 'uiautomator', 'dump', '/sdcard/aura-smoke.xml')
            adb('pull', '/sdcard/aura-smoke.xml', str(OUTPUT / (name + '.xml')))
            return ET.parse(OUTPUT / (name + '.xml')).getroot()
        except (ET.ParseError, subprocess.CalledProcessError):
            if attempt == 2:
                raise
            time.sleep(1)

def find(text, root=None):
    for node in (root if root is not None else dump()).iter('node'):
        if node.get('text') == text or node.get('content-desc') == text:
            points = [int(x) for x in re.findall(r'\d+', node.get('bounds', ''))]
            if len(points) == 4 and points[2] > points[0] and points[3] > points[1]:
                return node
    return None

def click(text, scroll=False):
    for attempt in range(10 if scroll else 2):
        node = find(text)
        if node is not None:
            x1, y1, x2, y2 = map(int, re.findall(r'\d+', node.get('bounds')))
            adb('shell', 'input', 'tap', str((x1+x2)//2), str((y1+y2)//2))
            time.sleep(.7)
            return
        if scroll:
            adb('shell', 'input', 'swipe', '270', '950', '270', '300', '350')
            time.sleep(.3)
    raise AssertionError('Control not found: ' + text)

def assert_text(text, name):
    tree = dump(name)
    if not any(text in node.get('text', '') for node in tree.iter('node')):
        raise AssertionError('Missing text: ' + text)
    print('PASS ' + name, flush=True)

def wait_text(text, name, seconds=90):
    deadline=time.monotonic()+seconds
    while time.monotonic()<deadline:
        if any(text in node.get('text','') for node in dump(name).iter('node')):
            print('PASS ' + name,flush=True)
            return
        time.sleep(2)
    raise AssertionError('Timed out waiting for '+text)

def screenshot(name):
    with (OUTPUT / (name + '.png')).open('wb') as out:
        subprocess.run(['adb', '-s', SERIAL, 'exec-out', 'screencap', '-p'], stdout=out, check=True, timeout=20)

def settings():
    click('Settings')

def tools():
    settings()
    click('Tools and permissions', scroll=True)

def offline():
    settings()
    click('Offline voice setup', scroll=True)

try:
    adb('logcat', '-c')
    adb('shell', 'wm', 'size', '540x1200')
    adb('shell', 'wm', 'density', '210')
    adb('shell', 'am', 'force-stop', PACKAGE)
    adb('shell', 'am', 'start', '-W', '-n', COMPONENT)
    time.sleep(3)
    assert_text('NO API KEY OR CLOUD CREDITS', 'free-local-default')
    if any('EditText' in n.get('class','') for n in dump('voice-only-talk').iter('node')):
        raise AssertionError('Talk must not contain a typed chat input')
    print('PASS voice-only Talk has no text input',flush=True)
    screenshot('native-home')
    click('Chats')
    assert_text('A fresh start','read-only-chat-history-empty')
    screenshot('native-chats')
    click('Routines')
    assert_text('Good morning','starter-routines')
    for title in ['Good morning','Bedtime','Focus','Driving']:
        n=find('Enable '+title)
        if n is None or n.get('checked')!='false':
            raise AssertionError('Starter routine not paused: '+title)
    print('PASS all starter routines begin paused',flush=True)
    screenshot('native-routines')
    click('Enable Good morning')
    click('Talk')
    click('Routines')
    if find('Enable Good morning').get('checked')!='true':
        raise AssertionError('Routine toggle not persisted')
    print('PASS routine enabled state persists across navigation',flush=True)
    click('Enable Good morning')
    tools()
    click('Connection and permissions status', scroll=True)
    assert_text('accessibility:', 'java-bridge-status')
    assert_text('shizuku:', 'shizuku-status')
    click('Check secure storage', scroll=True)
    assert_text('Secure storage check passed.', 'keystore-self-test')
    click('OK')
    screenshot('native-diagnostics')
    click('Check PDF reader', scroll=True)
    wait_text('PDF reader check passed.', 'pdf-text-and-native-render')
    click('OK')
    offline()
    click('Check offline speech engine', scroll=True)
    wait_text('Offline speech engine check passed.', 'offline-vosk-jni-and-recognition')
    click('OK')
    # Inference must work with no cloud connection or provider key.
    adb('shell','svc','wifi','disable')
    adb('shell','svc','data','disable')
    adb('shell','cmd','connectivity','airplane-mode','enable')
    screenshot('offline-setup')
    click('Check on-device AI', scroll=True)
    wait_text('On-device AI check passed.', 'actual-local-inference-network-disabled',seconds=240)
    screenshot('local-ai-answer-offline')
    click('OK')
    adb('shell','pm','grant',PACKAGE,'android.permission.RECORD_AUDIO')
    offline()
    click('Start background wake word', scroll=True)
    time.sleep(3)
    click('Talk')
    click('Mute',scroll=True)
    offline()
    assert_text('running: True','background-microphone-service-start')
    assert_text('paused: True','mute-pauses-local-microphone')
    click('Stop local microphone service', scroll=True)
    offline()
    assert_text('running: False','background-microphone-service-stop')
    click('Start offline voice', scroll=True)
    time.sleep(3)
    assert_text('Tap to end voice','offline-voice-start-without-key')
    click('Camera',scroll=True)
    assert_text('Offline voice supports text and local commands.','offline-camera-requires-online-mode')
    click('OK')
    click('End voice',scroll=True)
    assert_text('Tap the microphone to speak','offline-voice-stop')
    tools()
    click('Account authorization', scroll=True)
    assert_text('Connect Google account','google-consent-entry')
    click('GitHub access token', scroll=True)
    adb('shell', 'input', 'text', 'aura-smoke-dummy-token')
    entered=dump('dummy-token-entered')
    field=find('GitHub access token',entered)
    if field is None or field.get('text') in ('','GitHub access token'):
        raise AssertionError('Dummy credential was not entered in the GitHub field')
    print('PASS dummy credential entered into password field',flush=True)
    adb('shell', 'input', 'keyevent', '4')
    click('Save GitHub authorization', scroll=True)
    click('GitHub', scroll=True)
    assert_text('GitHub authorization saved', 'keystore-save-and-decrypt')
    tree = dump('token-not-displayed')
    if any('aura-smoke-dummy-token' in node.get('text', '') for node in tree.iter('node')):
        raise AssertionError('Saved credential remained visible')
    print('PASS saved credential is not displayed', flush=True)
    adb('shell', 'am', 'force-stop', PACKAGE)
    adb('shell', 'am', 'start', '-W', '-n', COMPONENT)
    time.sleep(2)
    tools()
    click('Account authorization', scroll=True)
    click('GitHub', scroll=True)
    assert_text('GitHub authorization saved', 'keystore-survives-restart')
    click('Disconnect GitHub from Aura', scroll=True)
    click('GitHub', scroll=True)
    assert_text('GitHub not authorized', 'account-disconnect')
    for service in ['BabyAccessibilityService', 'BabyNotificationService', 'BabyVoiceService', 'BabyVoiceSessionService', 'AuraWakeService']:
        resolved=adb('shell','cmd','package','query-services','--brief','--components','-n',PACKAGE+'/'+PACKAGE+'.bridge.'+service)
        if service not in resolved:
            raise AssertionError('Native service missing: ' + service)
    if 'ShizukuProvider' not in adb('shell','dumpsys','package',PACKAGE):
        raise AssertionError('Protected Shizuku provider missing')
    print('PASS native services registered', flush=True)
    settings()
    click('Use on-device AI')
    click('Talk')
    assert_text('GOOGLE QUOTA APPLIES','gemini-mode-selected-explicitly')
    click('Start talking',scroll=True)
    assert_text('Save your Gemini API key here','gemini-mode-no-silent-local-substitution')
    click('OK')
    click('Talk')
    click('Share screen',scroll=True)
    consent=dump('screen-sharing-consent')
    consent=dump('screen-sharing-start-control')
    choices=[n.get('text','') for n in consent.iter('node') if n.get('text','').lower() in ['start','start now','start sharing','share screen'] and 'Button' in n.get('class','')]
    if not choices: raise AssertionError('Android screen-sharing approval control not found')
    click(choices[0])
    time.sleep(4)
    tools()
    click('Connection and permissions status',scroll=True)
    assert_text('Screen capture active: True','media-projection-approved-and-active')
    def frame_count():
        for n in dump().iter('node'):
            if n.get('text','').startswith('Captured frames: '): return int(n.get('text').split(': ')[1])
        raise AssertionError('Frame counter missing')
    first=frame_count()
    if first<1: raise AssertionError('Screen pixels were not captured')
    print('PASS real screen frames captured',flush=True)
    adb('shell','input','keyevent','3')
    time.sleep(3)
    adb('shell','am','start','-W','-n',COMPONENT)
    tools()
    click('Connection and permissions status',scroll=True)
    if frame_count()<=first: raise AssertionError('Screen capture stopped while using another app')
    print('PASS screen sharing continues with the assistant in background',flush=True)
    adb('shell','settings','put','system','accelerometer_rotation','0')
    adb('shell','settings','put','system','user_rotation','1')
    time.sleep(3)
    adb('shell','settings','put','system','user_rotation','0')
    time.sleep(3)
    tools()
    click('Connection and permissions status',scroll=True)
    assert_text('Screen capture active: True','screen-sharing-survives-rotation')
    screenshot('native-screen-sharing-diagnostics')
    click('Talk')
    click('Stop sharing',scroll=True)
    time.sleep(2)
    tools()
    click('Connection and permissions status',scroll=True)
    assert_text('Screen capture active: False','screen-sharing-explicit-stop')

    logs = adb('logcat', '-d', '-s', 'AndroidRuntime:E')
    (OUTPUT / 'android-runtime.log').write_text(logs)
    if 'FATAL EXCEPTION' in logs:
        raise AssertionError('Android runtime exception occurred')
    print('PASS no Android runtime fatal exception', flush=True)
finally:
    try:
        screenshot('last-screen')
        (OUTPUT / 'all-logcat.txt').write_text(adb('logcat', '-d'))
        adb('shell','cmd','connectivity','airplane-mode','disable')
        adb('shell','svc','wifi','enable')
        adb('shell','svc','data','enable')
    except Exception:
        pass
