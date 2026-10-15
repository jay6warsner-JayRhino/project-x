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

def screenshot(name):
    with (OUTPUT / (name + '.png')).open('wb') as out:
        subprocess.run(['adb', '-s', SERIAL, 'exec-out', 'screencap', '-p'], stdout=out, check=True, timeout=20)

try:
    adb('logcat', '-c')
    adb('shell', 'wm', 'size', '540x1200')
    adb('shell', 'wm', 'density', '210')
    adb('shell', 'am', 'force-stop', PACKAGE)
    adb('shell', 'am', 'start', '-W', '-n', COMPONENT)
    time.sleep(3)
    assert_text('Start Gemini Live', 'native-home')
    screenshot('native-home')
    click('Start Gemini Live')
    assert_text('Save your Gemini API key here', 'missing-key-guidance')
    click('OK')
    click('Tools')
    click('Connection and permissions status', scroll=True)
    assert_text('accessibility:', 'java-bridge-status')
    assert_text('shizuku:', 'shizuku-status')
    click('Check secure storage', scroll=True)
    assert_text('Secure storage check passed.', 'keystore-self-test')
    click('OK')
    screenshot('native-diagnostics')
    click('Account authorization', scroll=True)
    click('GitHub access token', scroll=True)
    adb('shell', 'input', 'text', 'aura-smoke-dummy-token')
    entered=dump('dummy-token-entered')
    field=find('GitHub access token',entered)
    if field is None or field.get('text') in ('','GitHub access token'):
        raise AssertionError('Dummy credential was not entered in the GitHub field')
    print('PASS dummy credential entered into password field',flush=True)
    adb('shell', 'input', 'keyevent', '4')
    click('Save GitHub authorization', scroll=True)
    dump('immediately-after-save')
    click('GitHub', scroll=True)
    assert_text('GitHub authorization saved', 'keystore-save-and-decrypt')
    tree = dump('token-not-displayed')
    if any('aura-smoke-dummy-token' in node.get('text', '') for node in tree.iter('node')):
        raise AssertionError('Saved credential remained visible')
    print('PASS saved credential is not displayed', flush=True)
    adb('shell', 'am', 'force-stop', PACKAGE)
    adb('shell', 'am', 'start', '-W', '-n', COMPONENT)
    time.sleep(2)
    click('Tools')
    click('Account authorization', scroll=True)
    click('GitHub', scroll=True)
    assert_text('GitHub authorization saved', 'keystore-survives-restart')
    click('Disconnect GitHub from Aura', scroll=True)
    click('GitHub', scroll=True)
    assert_text('GitHub not authorized', 'account-disconnect')
    for service in ['BabyAccessibilityService', 'BabyNotificationService', 'BabyVoiceService', 'BabyVoiceSessionService']:
        resolved=adb('shell','cmd','package','query-services','--brief','--components','-n',PACKAGE+'/'+PACKAGE+'.bridge.'+service)
        if service not in resolved:
            raise AssertionError('Native service missing: ' + service)
    if 'ShizukuProvider' not in adb('shell','dumpsys','package',PACKAGE):
        raise AssertionError('Protected Shizuku provider missing')
    print('PASS native services registered', flush=True)
    logs = adb('logcat', '-d', '-s', 'AndroidRuntime:E')
    (OUTPUT / 'android-runtime.log').write_text(logs)
    if 'FATAL EXCEPTION' in logs:
        raise AssertionError('Android runtime exception occurred')
    print('PASS no Android runtime fatal exception', flush=True)
finally:
    try:
        screenshot('last-screen')
        (OUTPUT / 'all-logcat.txt').write_text(adb('logcat', '-d'))
    except Exception:
        pass
