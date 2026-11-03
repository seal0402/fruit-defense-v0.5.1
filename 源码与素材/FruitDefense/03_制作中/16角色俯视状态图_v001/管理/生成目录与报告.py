import html
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
plan=json.loads((ROOT/'管理'/'制作任务.json').read_text(encoding='utf-8'))
state=json.loads((ROOT/'管理'/'状态.json').read_text(encoding='utf-8'))
prompt_path=ROOT/'管理'/'全部生成提示词_v002.json'
if not prompt_path.exists():prompt_path=ROOT/'管理'/'全部生成提示词.json'
prompts=json.loads(prompt_path.read_text(encoding='utf-8'))
labels={'active':'上场待机','charge':'蓄力','fire':'发射/支援发动','reserve':'候场待机'}
rows=[]
report=['# 16角色俯视状态图制作记录','','日期：2026-10-14。工具：内置 image_gen。', '',f'目标：16位角色，每位4种静态关键姿势，共64张。当前已保存并通过文件检查：**{state["saved"]}/64张**。','','所有角色沿用固定俯视相机；战斗主要朝左上方，候场朝右下方。原图未手持枪械者保留相机、扫帚、应援棒、书本等道具，采用相应支援动作。','','| 原图编号 | 上场 | 蓄力 | 发射/支援 | 候场 |','| --- | --- | --- | --- | --- |']
for character in plan['characters']:
    cid=character['id']
    cells=[f'<figure><img src="原始参考/{cid}.webp" alt="{cid}原始参考"><figcaption>原始参考</figcaption></figure>']
    statuses=[]
    for phase in ['active','charge','fire','reserve']:
        item=state['files'].get(cid+'/'+phase)
        if item:
            rel=item['local_path'];cells.append(f'<figure><a href="{html.escape(rel)}"><img src="{html.escape(rel)}" alt="{cid}{labels[phase]}"></a><figcaption>{labels[phase]}</figcaption></figure>');statuses.append('已保存')
        else:
            cells.append(f'<figure><div class="missing">待生成</div><figcaption>{labels[phase]}</figcaption></figure>');statuses.append('待生成')
    rows.append(f'<section><h2>{cid}</h2><div class="grid">'+''.join(cells)+'</div></section>')
    report.append('| '+cid+' | '+' | '.join(statuses)+' |')
    cp=[j for j in prompts if j['id']==cid]
    (ROOT/'角色'/cid/'生成提示词.txt').write_text('\n\n'.join(labels[j['state']]+'\n'+state['files'].get(cid+'/'+j['state'],j)['prompt'] for j in cp),encoding='utf-8')
report.extend(['','文件核验包含图像解码、正方形画布、真实透明通道和SHA-256。详细参数见 `管理/状态.json`。','','当前限制：这是静态关键姿势，不是连续动画，也未覆盖八方向。AI生成的动作切换仍需精确校准脚底和枪口位置，透明边缘的细小杂点需要在最终导出时检查。阴影与粒子作为独立图层制作。','','未完成部分与接口错误以 `管理/运行进度.json` 为准。出现HTTP 429表示生成接口暂时限制请求，不代表图片已生成。'])
(ROOT/'制作记录.md').write_text('\n'.join(report),encoding='utf-8')
page='''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>16角色俯视状态图</title><style>*{box-sizing:border-box}body{margin:0;background:#edf1f4;color:#20374a;font:15px/1.6 "Microsoft YaHei",sans-serif}main{max-width:1500px;margin:auto;padding:24px}h1{margin:0;font-size:26px}h2{font-size:19px;margin:8px 0}.grid{display:grid;grid-template-columns:repeat(5,minmax(0,1fr));gap:12px}section{background:white;border-radius:12px;padding:18px;margin:18px 0}figure{margin:0;min-width:0;border:1px solid #d8e0e8;border-radius:8px;overflow:hidden}figure img{width:100%;height:270px;object-fit:contain;background:repeating-conic-gradient(#e4eaf0 0 25%,#fff 0 50%) 0/18px 18px}figcaption{text-align:center;padding:7px;background:#fff}.missing{height:270px;display:grid;place-items:center;color:#8b9aa6;background:#f3f5f7}a{color:#126780}@media(max-width:800px){.grid{grid-template-columns:repeat(2,minmax(0,1fr))}figure img,.missing{height:230px}}</style><main>'''
page+=f'<h1>16角色 · 俯视状态图</h1><p>已保存 {state["saved"]}/64 张。点击人物图可打开原始PNG。待生成位置明确标出。</p><p>四种静态关键姿势；战斗朝向左上，候场朝向右下。棋盘格为页面透明预览背景，不在PNG内。</p>'
page+=''.join(rows)+'</main></html>'
(ROOT/'素材总览.html').write_text(page,encoding='utf-8')
print(json.dumps({'saved':state['saved'],'expected':64,'gallery':'素材总览.html'},ensure_ascii=False))
