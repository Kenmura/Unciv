import re,json,sys
# usage: defs.py <decomp.c> <outdir>
src,outdir=sys.argv[1],sys.argv[2]
text=open(src,errors='replace').read()
blocks=text.split('\n// @')
# init function: the block containing AI_GS_RAND_ROLL load
init=[b for b in blocks if '"AI_GS_RAND_ROLL"' in b][0]
tab={};cur=None
for l in init.split('\n'):
    m=re.search(r'\("(\w+)",1\)',l)
    if m: cur=m.group(1); continue
    m=re.search(r'\*\(\w+ \*\)\(param_1 \+ (0x[0-9a-f]+)\) = \w+;',l)
    if m and cur: tab[int(m.group(1),16)]=cur; cur=None
# anchor via gift function
g=[b for b in blocks if 'good turn to make a gold gift to a minor' in b][0]
addrs=sorted(set(int(a,16) for a in re.findall(r'DAT_(10[0-9a-f]{6})',g)))
inv={v:k for k,v in tab.items()}
o1=inv['MC_ALWAYS_GIFT_DIPLO_THRESHOLD']; o2=inv['MC_SOMETIMES_GIFT_RAND_MULTIPLIER']
cands=[a-o1 for a in addrs if a-o1+ o2 in addrs]
print('defines',len(tab),'anchor cands',[hex(c) for c in cands], 'gift addrs',[hex(a) for a in addrs])
base=cands[0] if len(cands)==1 else None
json.dump({'base':base,'tab':{hex(k):v for k,v in tab.items()}},open(outdir+'/defines.json','w'))
