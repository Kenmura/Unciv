import re,json,sys
R=sys.argv[1]; txt=open(R+'/decomp.c',errors='replace').read()
calls=json.load(open(R+'/calls.json'))
blocks={b.split()[0]:b for b in txt.split('\n// @')[1:]}
out=[]
for m in re.finditer(r'lua_pushcclosure\(param_1,FUN_([0-9a-f]+),0\);\n\s*lua_setfield\(param_1,param_2,"(\w+)"\);',txt):
    w,name=m.group(1),m.group(2)
    # class context: find enclosing block start
    b=blocks.get(w,'')
    callees=[c for c in re.findall(r'FUN_([0-9a-f]{8})\(',b) if c!=w]
    # drop common helpers: keep in order, unique
    seen=[];[seen.append(c) for c in callees if c not in seen]
    out.append((name,w,seen[:6]))
json.dump(out,open(R+'/luamap.json','w'))
print(len(out))
