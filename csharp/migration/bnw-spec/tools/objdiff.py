# usage: objdiff.py <bnw.db> <loc.db> <unciv G&K jsons dir>
import sys,sqlite3,json,re,os
db=sqlite3.connect(f'file:{sys.argv[1]}?mode=ro',uri=True); loc=sqlite3.connect(f'file:{sys.argv[2]}?mode=ro',uri=True)
tx={t:x for t,x in loc.execute("select Tag,Text from LocalizedText where Language='en_US'")}
def en(k): return re.sub(r'\[[^\]]*\]','',tx.get(k,k) or k).strip()
def lj(f):
    s=open(os.path.join(sys.argv[3],f),encoding='utf-8').read()
    o=[];i=0;q=False
    while i<len(s):
        c=s[i]
        if q:
            o.append(c)
            if c=='\\': o.append(s[i+1]); i+=1
            elif c=='"': q=False
        elif c=='"': q=True; o.append(c)
        elif s.startswith('//',i):
            while i<len(s) and s[i]!='\n': i+=1
            continue
        elif s.startswith('/*',i):
            i=s.index('*/',i)+2; continue
        else: o.append(c)
        i+=1
    s=re.sub(r',(\s*[\]}])',r'\1',''.join(o))
    return json.loads(s)
pairs=[('Buildings','Buildings.json',"select Type,Description from Buildings"),
 ('Units','Units.json',"select Type,Description from Units"),
 ('Techs','Techs.json',None),
 ('Policies','Policies.json',"select Type,Description from Policies"),
 ('Beliefs','Beliefs.json',"select Type,ShortDescription from Beliefs"),
 ('Improvements','TileImprovements.json',"select Type,Description from Improvements"),
 ('Resources','TileResources.json',"select Type,Description from Resources"),
 ('Promotions','UnitPromotions.json',"select Type,Description from UnitPromotions"),
 ('Features','Terrains.json',"select Type,Description from Features"),
 ('Nations','Nations.json',"select Type,ShortDescription from Civilizations union select Type,Description from MinorCivilizations"),
]
for name,f,q in pairs:
    js=lj(f)
    if name=='Techs': un={t['name'] for t in js} if isinstance(js,list) and 'name' in js[0] else {t['name'] for c in js for t in c['techs']}; q="select Type,Description from Technologies"
    elif name=='Policies': un={b['name'] for b in js}|{p['name'] for b in js for p in b.get('policies',[])}
    else: un={o['name'] for o in js}
    bn={en(d):t for t,d in db.execute(q)}
    print(f'== {name}: BNW {len(bn)} Unciv {len(un)}')
    print('  only BNW :',sorted(set(bn)-un))
    print('  only Unciv:',sorted(un-set(bn)))
