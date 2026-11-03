import argparse
import hashlib
import json
import shutil
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
MANAGEMENT = ROOT / '管理'
GENERATED = Path(r'C:\Users\wang2\.codex\generated_images').resolve()

def read_json(path, default=None):
    return json.loads(path.read_text(encoding='utf-8')) if path.exists() else default

def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')

def metadata(path):
    with Image.open(path) as im:
        im.load()
        alpha = im.getchannel('A') if 'A' in im.getbands() else None
        histogram = alpha.histogram() if alpha else None
        return {
            'size':list(im.size), 'mode':im.mode,
            'alpha_range':list(alpha.getextrema()) if alpha else None,
            'transparent_fraction':histogram[0]/(im.width*im.height) if histogram else 0,
            'bytes':path.stat().st_size,
            'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
        }

def initialize():
    plan = read_json(MANAGEMENT / '制作任务.json')
    (ROOT / '原始参考').mkdir(parents=True, exist_ok=True)
    (ROOT / '画风与相机参考').mkdir(parents=True, exist_ok=True)
    for item in plan['characters']:
        source = Path(plan['source_reference_root']) / f'{item["id"]}.webp'
        target = ROOT / '原始参考' / source.name
        if not target.exists():shutil.copy2(source,target)
        (ROOT / '角色' / item['id']).mkdir(parents=True,exist_ok=True)
    for key,label in [('style_reference','白子_画风与视角参考.png'),('camera_reference','手机竖屏_背景参考.png')]:
        shutil.copy2(plan[key],ROOT / '画风与相机参考' / label)
    state_path = MANAGEMENT / '状态.json'
    if not state_path.exists():write_json(state_path,{'expected':64,'saved':0,'files':{},'errors':[]})
    print(json.dumps({'initialized':True,'characters':len(plan['characters'])},ensure_ascii=False))

def save_batch(batch_file):
    batch = read_json(batch_file)
    state = read_json(MANAGEMENT / '状态.json', {'expected':64,'saved':0,'files':{},'errors':[]})
    for item in batch:
        src = Path(item['source']).resolve()
        target = (ROOT / '角色' / item['id'] / item['filename']).resolve()
        assert src.is_relative_to(GENERATED) and src.suffix.lower()=='.png',src
        assert target.is_relative_to(ROOT / '角色'),target
        if target.exists():
            assert hashlib.sha256(target.read_bytes()).digest()==hashlib.sha256(src.read_bytes()).digest(),'Refusing to overwrite a different image'
        else:shutil.copy2(src,target)
        info = metadata(target)
        assert info['size'][0]==info['size'][1],'Expected square canvas'
        assert info['alpha_range']==[0,255] and .20<info['transparent_fraction']<.99,'Missing genuine transparency or nearly empty asset'
        key = item['id']+'/'+item['state']
        state['files'][key] = {**item,**info,'local_path':target.relative_to(ROOT).as_posix(),'file_check':'passed','visual_review':'pending'}
        state['saved']=len(state['files'])
        write_json(MANAGEMENT / '状态.json',state)
    print(json.dumps({'saved':state['saved'],'expected':state['expected'],'batch':[{k:v for k,v in x.items() if k in ('id','state','filename')} for x in batch]},ensure_ascii=False))

def review(keys):
    state = read_json(MANAGEMENT / '状态.json')
    for key in keys:
        state['files'][key]['visual_review']='checked_static_key_pose'
    write_json(MANAGEMENT / '状态.json',state)
    print(json.dumps({'reviewed':keys,'saved':state['saved']},ensure_ascii=False))

def reject(key, reason):
    state=read_json(MANAGEMENT/'状态.json')
    item=state['files'].pop(key)
    source=(ROOT/item['local_path']).resolve()
    target=(MANAGEMENT/'退稿'/source.name).resolve()
    assert source.is_relative_to(ROOT/'角色') and target.is_relative_to(MANAGEMENT)
    target.parent.mkdir(parents=True,exist_ok=True)
    assert not target.exists()
    source.rename(target)
    state.setdefault('rejected',[]).append({**item,'local_path':target.relative_to(ROOT).as_posix(),'reason':reason})
    state['saved']=len(state['files'])
    write_json(MANAGEMENT/'状态.json',state)
    print(json.dumps({'rejected':key,'reason':reason,'saved':state['saved']},ensure_ascii=False))

parser=argparse.ArgumentParser()
parser.add_argument('--initialize',action='store_true')
parser.add_argument('--batch')
parser.add_argument('--review',nargs='*')
parser.add_argument('--reject')
parser.add_argument('--reason',default='visual mismatch')
args=parser.parse_args()
if args.initialize:initialize()
if args.batch:save_batch(Path(args.batch))
if args.review:review(args.review)
if args.reject:reject(args.reject,args.reason)
