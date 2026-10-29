import sys,os,re,xml.etree.ElementTree as ET
def schema(root):
    tabs={}
    for dp,_,fs in os.walk(root):
        for f in fs:
            if not f.lower().endswith('.xml') or '/Text' in dp: continue
            try: t=ET.parse(os.path.join(dp,f)).getroot()
            except Exception: continue
            for tb in t.iter('Table'):
                n=tb.get('name')
                if not n: continue
                tabs.setdefault(n,set()).update(c.get('name') or '' for c in tb.iter('Column'))
    return tabs
civ=sys.argv[1]
gk=schema(civ+'/Assets/DLC/Expansion/Gameplay')
bnw=schema(civ+'/Assets/DLC/Expansion2/Gameplay')
for t in sorted(bnw):
    if t not in gk: print('NEW TABLE',t,sorted(bnw[t]))
    else:
        d=sorted(bnw[t]-gk[t])
        if d: print('NEW COLS',t,d)
for t in sorted(gk):
    if t not in bnw: print('GONE TABLE',t)
    else:
        d=sorted(gk[t]-bnw[t])
        if d: print('GONE COLS',t,d)
