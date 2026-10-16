"""Prepare the Apache-2.0 Qwen3 0.6B asset for a fully local Android build."""
from pathlib import Path
import hashlib, os, shutil, urllib.request

SIZE = 344671744
SHA256 = '03e7da1eb1108b50dffaa9bb52cc7bcbad2eb0c66ca990267f480c1e545d2856'
URL = 'https://huggingface.co/litert-community/Qwen3-0.6B/resolve/main/Qwen3-0.6B_dynamic_wi4b32_afp32.litertlm'
ASSET = Path(__file__).resolve().parent / 'Assets/offline-model.litertlm'

def verified(path):
    if not path.is_file() or path.stat().st_size != SIZE:
        return False
    with path.open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest() == SHA256

if verified(ASSET):
    print('Bundled local model verified')
else:
    ASSET.parent.mkdir(parents=True, exist_ok=True)
    cache = ASSET.parent.parent.parent / 'ModelCache/Qwen3-0.6B.litertlm'
    temporary = ASSET.with_suffix('.downloading')
    if verified(cache):
        if temporary.exists():
            temporary.unlink()
        try:
            os.link(cache, temporary)
        except OSError:
            shutil.copyfile(cache, temporary)
    else:
        with urllib.request.urlopen(URL, timeout=120) as response, temporary.open('wb') as target:
            total = 0
            while block := response.read(1024*1024):
                total += len(block)
                if total > SIZE:
                    raise ValueError('Local model download exceeds expected size')
                target.write(block)
    if not verified(temporary):
        raise ValueError('Local model integrity check failed')
    temporary.replace(ASSET)
    print('Bundled local model prepared and verified')
